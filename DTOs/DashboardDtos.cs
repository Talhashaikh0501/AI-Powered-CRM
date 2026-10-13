using System;
using System.Collections.Generic;
using AI_Powered_CRM.Models.Entities;

namespace AI_Powered_CRM.DTOs
{
    public class DashboardSummaryDto
    {
        public int TotalActiveLeads { get; set; }
        public int NewLeadsCount { get; set; }
        public int HotLeadsCount { get; set; }
        public int ConvertedCount { get; set; }
        public int LostCount { get; set; }
        public decimal TotalPipelineValue { get; set; }
        public decimal ConvertedRevenue { get; set; }
        public double WinRatePercentage { get; set; }

        public int FollowUpsDueTodayCount { get; set; }
        public int FollowUpsOverdueCount { get; set; }

        public List<FollowUp> UrgentFollowUps { get; set; } = new List<FollowUp>();
        public List<Lead> RecentLeads { get; set; } = new List<Lead>();
        public List<LeadActivity> RecentActivities { get; set; } = new List<LeadActivity>();

        public Dictionary<string, int> StageDistribution { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, int> SourceDistribution { get; set; } = new Dictionary<string, int>();

        public ManagerPipelineSummaryDto? AIPipelineSummary { get; set; }
    }

    public class ConversionReportDto
    {
        public int TotalLeads { get; set; }
        public int ConvertedLeads { get; set; }
        public int LostLeads { get; set; }
        public double ConversionRate { get; set; }
        public decimal TotalWonRevenue { get; set; }
        public decimal AverageDealSize { get; set; }
        public Dictionary<string, int> StageDropoffs { get; set; } = new Dictionary<string, int>();
    }

    public class SourcePerformanceDto
    {
        public string Source { get; set; } = string.Empty;
        public int LeadCount { get; set; }
        public int ConvertedCount { get; set; }
        public double ConversionRate { get; set; }
        public decimal TotalRevenue { get; set; }
    }

    public class StaffPerformanceDto
    {
        public string StaffUserId { get; set; } = string.Empty;
        public string StaffName { get; set; } = string.Empty;
        public int AssignedLeadsCount { get; set; }
        public int CompletedFollowUpsCount { get; set; }
        public int ConvertedLeadsCount { get; set; }
        public double ConversionRate { get; set; }
        public decimal ClosedRevenue { get; set; }
    }
}
