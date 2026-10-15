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

        /// <summary>
        /// Total estimated deal budget of converted leads. Note: Converted lead budgets represent estimated won deal value, not realized accounting revenue.
        /// </summary>
        public decimal EstimatedConvertedValue { get; set; }

        // Backward compatibility alias
        public decimal ConvertedRevenue
        {
            get => EstimatedConvertedValue;
            set => EstimatedConvertedValue = value;
        }

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

        /// <summary>
        /// Total estimated deal value of won/converted leads. Note: Lead budgets represent conversion estimates, not realized accounting revenue.
        /// </summary>
        public decimal TotalWonDealValue { get; set; }

        // Backward compatibility alias
        public decimal TotalWonRevenue
        {
            get => TotalWonDealValue;
            set => TotalWonDealValue = value;
        }

        public decimal AverageDealSize { get; set; }
        public Dictionary<string, int> StageDropoffs { get; set; } = new Dictionary<string, int>();
    }

    public class SourcePerformanceDto
    {
        public string Source { get; set; } = string.Empty;
        public int LeadCount { get; set; }
        public int ConvertedCount { get; set; }
        public double ConversionRate { get; set; }

        /// <summary>
        /// Estimated deal value from converted leads for this acquisition source.
        /// </summary>
        public decimal EstimatedWonValue { get; set; }

        public decimal TotalRevenue
        {
            get => EstimatedWonValue;
            set => EstimatedWonValue = value;
        }
    }

    public class StaffPerformanceDto
    {
        public string StaffUserId { get; set; } = string.Empty;
        public string StaffName { get; set; } = string.Empty;
        public int AssignedLeadsCount { get; set; }
        public int CompletedFollowUpsCount { get; set; }
        public int ConvertedLeadsCount { get; set; }
        public double ConversionRate { get; set; }

        /// <summary>
        /// Total estimated deal value of converted leads owned by this representative.
        /// </summary>
        public decimal ClosedWonDealValue { get; set; }

        public decimal ClosedRevenue
        {
            get => ClosedWonDealValue;
            set => ClosedWonDealValue = value;
        }
    }
}
