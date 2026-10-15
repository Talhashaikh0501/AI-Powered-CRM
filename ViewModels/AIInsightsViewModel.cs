using System;
using System.Collections.Generic;
using AI_Powered_CRM.Models.Entities;

namespace AI_Powered_CRM.ViewModels
{
    public class AIInsightsViewModel
    {
        public int TotalAnalyzedLeads { get; set; }
        public int HotLeadsCount { get; set; }
        public int WarmLeadsCount { get; set; }
        public int ColdLeadsCount { get; set; }
        public int UnscoredLeadsCount { get; set; }

        public double HotPercent => TotalAnalyzedLeads > 0 ? Math.Round((double)HotLeadsCount / TotalAnalyzedLeads * 100, 1) : 0;
        public double WarmPercent => TotalAnalyzedLeads > 0 ? Math.Round((double)WarmLeadsCount / TotalAnalyzedLeads * 100, 1) : 0;
        public double ColdPercent => TotalAnalyzedLeads > 0 ? Math.Round((double)ColdLeadsCount / TotalAnalyzedLeads * 100, 1) : 0;

        public decimal HighProbabilityPipelineValue { get; set; }
        public decimal TotalActivePipelineValue { get; set; }

        public int MonthlyAiRequestLimit { get; set; }
        public int CurrentMonthAiUsageCount { get; set; }
        public int AiQuotaPercent => MonthlyAiRequestLimit > 0 
            ? Math.Min(100, (int)Math.Round((double)CurrentMonthAiUsageCount / MonthlyAiRequestLimit * 100)) 
            : 0;

        public List<PriorityOpportunityDto> PriorityOpportunities { get; set; } = new List<PriorityOpportunityDto>();
    }

    public class PriorityOpportunityDto
    {
        public int LeadId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Company { get; set; }
        public decimal? BudgetInr { get; set; }
        public int Score { get; set; }
        public string Classification { get; set; } = "Warm";
        public string? NextBestAction { get; set; }
        public string? RiskLevel { get; set; }
        public string? OwnerName { get; set; }
        public DateTime LastAnalyzedAt { get; set; }
    }
}
