using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AI_Powered_CRM.AI;
using AI_Powered_CRM.Data;
using AI_Powered_CRM.DTOs;
using AI_Powered_CRM.Models.Entities;
using AI_Powered_CRM.Models.Enums;
using AI_Powered_CRM.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AI_Powered_CRM.Services.Implementations
{
    public class ReportService : IReportService
    {
        private readonly ApplicationDbContext _context;
        private readonly IAIService _aiService;

        public ReportService(ApplicationDbContext context, IAIService aiService)
        {
            _context = context;
            _aiService = aiService;
        }

        public async Task<DashboardSummaryDto> GetDashboardSummaryAsync(string? currentUserId, string? role)
        {
            var leadQuery = _context.Leads
                .Include(l => l.OwnerUser)
                .Include(l => l.AIInsights)
                .Where(l => !l.IsArchived);

            var followUpQuery = _context.FollowUps
                .Include(f => f.Lead)
                .Include(f => f.AssignedUser)
                .Where(f => !f.Lead.IsArchived);

            if (role != "Admin" && role != "Manager")
            {
                leadQuery = leadQuery.Where(l => l.OwnerUserId == currentUserId);
                followUpQuery = followUpQuery.Where(f => f.AssignedUserId == currentUserId);
            }

            var allLeads = await leadQuery.ToListAsync();

            var totalActive = allLeads.Count(l => l.Status != LeadStatus.Converted && l.Status != LeadStatus.Lost);
            var newLeads = allLeads.Count(l => l.Status == LeadStatus.New);
            var converted = allLeads.Count(l => l.Status == LeadStatus.Converted);
            var lost = allLeads.Count(l => l.Status == LeadStatus.Lost);

            var hotLeads = allLeads.Count(l =>
                l.Status != LeadStatus.Converted &&
                l.Status != LeadStatus.Lost &&
                l.AIInsights.OrderByDescending(i => i.GeneratedAt).FirstOrDefault()?.Classification == AIClassification.Hot);

            var pipelineValue = allLeads
                .Where(l => l.Status != LeadStatus.Converted && l.Status != LeadStatus.Lost && l.Budget.HasValue)
                .Sum(l => l.Budget!.Value);

            // Converted lead budgets represent estimated won deal value, not realized accounting revenue
            var estimatedConvertedValue = allLeads
                .Where(l => l.Status == LeadStatus.Converted && l.Budget.HasValue)
                .Sum(l => l.Budget!.Value);

            var closedTotal = converted + lost;
            var winRate = closedTotal > 0 ? (double)converted / closedTotal * 100.0 : 0.0;

            // Follow-ups
            var now = DateTime.UtcNow;
            var startOfDay = now.Date;
            var endOfDay = startOfDay.AddDays(1).AddTicks(-1);

            var overdueCount = await followUpQuery
                .Where(f => f.Status == FollowUpStatus.Pending && f.DueAt < now)
                .CountAsync();

            var dueTodayCount = await followUpQuery
                .Where(f => f.Status == FollowUpStatus.Pending && f.DueAt >= startOfDay && f.DueAt <= endOfDay)
                .CountAsync();

            var urgentFollowUps = await followUpQuery
                .Where(f => f.Status == FollowUpStatus.Pending && f.DueAt <= endOfDay)
                .OrderBy(f => f.DueAt)
                .Take(5)
                .ToListAsync();

            var recentLeads = allLeads
                .OrderByDescending(l => l.CreatedAt)
                .Take(5)
                .ToList();

            var activityQuery = _context.LeadActivities
                .Include(a => a.User)
                .Include(a => a.Lead)
                .Where(a => !a.Lead.IsArchived);

            if (role != "Admin" && role != "Manager")
            {
                activityQuery = activityQuery.Where(a => a.Lead.OwnerUserId == currentUserId);
            }

            var recentActivities = await activityQuery
                .OrderByDescending(a => a.OccurredAt)
                .Take(6)
                .ToListAsync();

            // Stage distribution
            var stageDist = allLeads
                .GroupBy(l => l.Status.ToString())
                .ToDictionary(g => g.Key, g => g.Count());

            // Source distribution
            var sourceDist = allLeads
                .GroupBy(l => l.Source)
                .ToDictionary(g => g.Key, g => g.Count());

            var summary = new DashboardSummaryDto
            {
                TotalActiveLeads = totalActive,
                NewLeadsCount = newLeads,
                HotLeadsCount = hotLeads,
                ConvertedCount = converted,
                LostCount = lost,
                TotalPipelineValue = pipelineValue,
                EstimatedConvertedValue = estimatedConvertedValue,
                ConvertedRevenue = estimatedConvertedValue,
                WinRatePercentage = Math.Round(winRate, 1),
                FollowUpsDueTodayCount = dueTodayCount,
                FollowUpsOverdueCount = overdueCount,
                UrgentFollowUps = urgentFollowUps,
                RecentLeads = recentLeads,
                RecentActivities = recentActivities,
                StageDistribution = stageDist,
                SourceDistribution = sourceDist
            };

            // Generate AI Executive Pipeline Summary for Admins and Managers
            if (role == "Admin" || role == "Manager")
            {
                summary.AIPipelineSummary = await _aiService.GeneratePipelineSummaryAsync(summary);
            }

            return summary;
        }

        public async Task<ConversionReportDto> GetConversionReportAsync()
        {
            var leads = await _context.Leads.Where(l => !l.IsArchived).ToListAsync();

            var total = leads.Count;
            var converted = leads.Count(l => l.Status == LeadStatus.Converted);
            var lost = leads.Count(l => l.Status == LeadStatus.Lost);
            var closed = converted + lost;
            var winRate = closed > 0 ? (double)converted / closed * 100.0 : 0.0;

            // Won lead budgets represent estimated conversion value, not realized accounting revenue
            var wonDealValue = leads.Where(l => l.Status == LeadStatus.Converted && l.Budget.HasValue).Sum(l => l.Budget!.Value);
            var avgDeal = converted > 0 ? wonDealValue / converted : 0m;

            var stageDropoffs = leads
                .GroupBy(l => l.Status.ToString())
                .ToDictionary(g => g.Key, g => g.Count());

            return new ConversionReportDto
            {
                TotalLeads = total,
                ConvertedLeads = converted,
                LostLeads = lost,
                ConversionRate = Math.Round(winRate, 1),
                TotalWonDealValue = wonDealValue,
                TotalWonRevenue = wonDealValue,
                AverageDealSize = Math.Round(avgDeal, 2),
                StageDropoffs = stageDropoffs
            };
        }

        public async Task<List<SourcePerformanceDto>> GetSourcePerformanceAsync()
        {
            var leads = await _context.Leads.Where(l => !l.IsArchived).ToListAsync();

            return leads
                .GroupBy(l => l.Source)
                .Select(g =>
                {
                    var count = g.Count();
                    var converted = g.Count(l => l.Status == LeadStatus.Converted);
                    var rate = count > 0 ? (double)converted / count * 100.0 : 0.0;
                    var estValue = g.Where(l => l.Status == LeadStatus.Converted && l.Budget.HasValue).Sum(l => l.Budget!.Value);

                    return new SourcePerformanceDto
                    {
                        Source = g.Key,
                        LeadCount = count,
                        ConvertedCount = converted,
                        ConversionRate = Math.Round(rate, 1),
                        EstimatedWonValue = estValue,
                        TotalRevenue = estValue
                    };
                })
                .OrderByDescending(s => s.EstimatedWonValue)
                .ThenByDescending(s => s.LeadCount)
                .ToList();
        }

        public async Task<List<StaffPerformanceDto>> GetStaffPerformanceAsync()
        {
            var leads = await _context.Leads
                .Include(l => l.OwnerUser)
                .Where(l => !l.IsArchived && l.OwnerUserId != null)
                .ToListAsync();

            var followUps = await _context.FollowUps.ToListAsync();

            return leads
                .GroupBy(l => l.OwnerUserId!)
                .Select(g =>
                {
                    var owner = g.First().OwnerUser;
                    var leadCount = g.Count();
                    var converted = g.Count(l => l.Status == LeadStatus.Converted);
                    var rate = leadCount > 0 ? (double)converted / leadCount * 100.0 : 0.0;
                    var estValue = g.Where(l => l.Status == LeadStatus.Converted && l.Budget.HasValue).Sum(l => l.Budget!.Value);
                    var compFollowUps = followUps.Count(f => f.AssignedUserId == g.Key && f.Status == FollowUpStatus.Completed);

                    return new StaffPerformanceDto
                    {
                        StaffUserId = g.Key,
                        StaffName = owner?.FullName ?? owner?.UserName ?? "Unassigned",
                        AssignedLeadsCount = leadCount,
                        CompletedFollowUpsCount = compFollowUps,
                        ConvertedLeadsCount = converted,
                        ConversionRate = Math.Round(rate, 1),
                        ClosedWonDealValue = estValue,
                        ClosedRevenue = estValue
                    };
                })
                .OrderByDescending(s => s.ClosedWonDealValue)
                .ToList();
        }
    }
}
