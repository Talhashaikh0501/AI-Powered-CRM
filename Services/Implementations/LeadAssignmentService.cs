using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AI_Powered_CRM.Data;
using AI_Powered_CRM.Models.Entities;
using AI_Powered_CRM.Models.Enums;
using AI_Powered_CRM.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AI_Powered_CRM.Services.Implementations
{
    public class LeadAssignmentService : ILeadAssignmentService
    {
        private readonly ApplicationDbContext _db;
        private readonly IAuditService _auditService;
        private readonly ILogger<LeadAssignmentService> _logger;

        public LeadAssignmentService(
            ApplicationDbContext db,
            IAuditService auditService,
            ILogger<LeadAssignmentService> logger)
        {
            _db = db;
            _auditService = auditService;
            _logger = logger;
        }

        public async Task<string> DetermineLeadOwnerAsync(Guid tenantId, string? requestedOwnerId, string defaultUserId)
        {
            // 1. If explicit owner requested, verify they are an active member in this tenant
            if (!string.IsNullOrWhiteSpace(requestedOwnerId))
            {
                var isExplicitMemberValid = await _db.TenantMemberships
                    .IgnoreQueryFilters()
                    .AnyAsync(m => m.TenantId == tenantId && m.UserId == requestedOwnerId && m.IsActive);

                if (isExplicitMemberValid)
                {
                    var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == requestedOwnerId && u.IsActive);
                    if (user != null)
                    {
                        return requestedOwnerId;
                    }
                }

                _logger.LogWarning("Requested owner {RequestedOwnerId} is not an active member in tenant {TenantId}. Falling back to assignment rules.",
                    requestedOwnerId, tenantId);
            }

            // 2. Fetch tenant settings
            var settings = await _db.TenantSettings
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId);

            var mode = settings?.LeadAssignmentMode ?? "Manual";

            if (!string.Equals(mode, "RoundRobin", StringComparison.OrdinalIgnoreCase))
            {
                // Manual or default mode: keep default user
                return defaultUserId;
            }

            // 3. Round-Robin Distribution among active Sales Representatives
            // First look for active SalesExecutives
            var salesRepIds = await _db.TenantMemberships
                .IgnoreQueryFilters()
                .Where(m => m.TenantId == tenantId && m.IsActive && m.CompanyRole == CompanyRole.SalesExecutive)
                .Select(m => m.UserId)
                .ToListAsync();

            // Fallback: If no dedicated SalesExecutives found, expand to all active tenant members
            if (!salesRepIds.Any())
            {
                salesRepIds = await _db.TenantMemberships
                    .IgnoreQueryFilters()
                    .Where(m => m.TenantId == tenantId && m.IsActive)
                    .Select(m => m.UserId)
                    .ToListAsync();
            }

            if (!salesRepIds.Any())
            {
                _logger.LogWarning("No active members found in tenant {TenantId} for RoundRobin assignment. Using default user {DefaultUserId}.",
                    tenantId, defaultUserId);
                return defaultUserId;
            }

            // Filter by active ApplicationUser accounts
            var activeCandidates = await _db.Users
                .AsNoTracking()
                .Where(u => salesRepIds.Contains(u.Id) && u.IsActive)
                .Select(u => u.Id)
                .ToListAsync();

            if (!activeCandidates.Any())
            {
                return defaultUserId;
            }

            // 4. Calculate active workload and recency of assignment for each candidate in this tenant
            var candidateWorkloads = await _db.Leads
                .IgnoreQueryFilters()
                .Where(l => l.TenantId == tenantId && !l.IsArchived &&
                            l.Status != LeadStatus.Converted && l.Status != LeadStatus.Lost &&
                            l.OwnerUserId != null && activeCandidates.Contains(l.OwnerUserId))
                .GroupBy(l => l.OwnerUserId!)
                .Select(g => new
                {
                    UserId = g.Key,
                    ActiveCount = g.Count(),
                    LastAssignedAt = g.Max(l => l.CreatedAt)
                })
                .ToListAsync();

            var candidateStats = activeCandidates.Select(userId =>
            {
                var work = candidateWorkloads.FirstOrDefault(w => w.UserId == userId);
                return new
                {
                    UserId = userId,
                    ActiveCount = work?.ActiveCount ?? 0,
                    LastAssignedAt = work?.LastAssignedAt ?? DateTime.MinValue
                };
            })
            // Least loaded first, then least recently assigned
            .OrderBy(c => c.ActiveCount)
            .ThenBy(c => c.LastAssignedAt)
            .ThenBy(c => c.UserId)
            .ToList();

            var selected = candidateStats.First().UserId;

            await _auditService.LogAsync(
                defaultUserId,
                "Tenant",
                tenantId.ToString(),
                "LeadAssignedRoundRobin",
                new
                {
                    AssignedUserId = selected,
                    CandidateCount = activeCandidates.Count,
                    SelectedActiveLeadCount = candidateStats.First().ActiveCount
                });

            _logger.LogInformation("RoundRobin lead assignment in tenant {TenantId}: assigned to {UserId} out of {Count} candidates.",
                tenantId, selected, activeCandidates.Count);

            return selected;
        }
    }
}
