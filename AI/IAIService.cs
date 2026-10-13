using System.Collections.Generic;
using System.Threading.Tasks;
using AI_Powered_CRM.DTOs;
using AI_Powered_CRM.Models.Entities;

namespace AI_Powered_CRM.AI
{
    public interface IAIService
    {
        Task<AILeadAnalysisResult> AnalyzeLeadAsync(Lead lead, List<LeadActivity> activities, List<FollowUp> followUps);
        Task<string> GenerateDraftResponseAsync(Lead lead, List<LeadActivity> activities, string? userTone, string? customInstructions);
        Task<ManagerPipelineSummaryDto> GeneratePipelineSummaryAsync(DashboardSummaryDto dashboardData);
    }

    public interface IDeterministicFallbackScorer
    {
        AILeadAnalysisResult CalculateDeterministicScore(Lead lead, List<LeadActivity> activities, List<FollowUp> followUps);
    }
}
