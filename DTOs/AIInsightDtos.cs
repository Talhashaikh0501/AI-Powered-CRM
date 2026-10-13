using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using AI_Powered_CRM.Models.Enums;

namespace AI_Powered_CRM.DTOs
{
    public class AILeadAnalysisResult
    {
        [JsonPropertyName("classification")]
        public string Classification { get; set; } = "Warm";

        [JsonPropertyName("score")]
        public int Score { get; set; } = 50;

        [JsonPropertyName("summary")]
        public string Summary { get; set; } = string.Empty;

        [JsonPropertyName("nextBestAction")]
        public string NextBestAction { get; set; } = string.Empty;

        [JsonPropertyName("riskLevel")]
        public string RiskLevel { get; set; } = "Low";

        [JsonPropertyName("urgency")]
        public string Urgency { get; set; } = "Normal";

        [JsonPropertyName("reasons")]
        public List<string> Reasons { get; set; } = new List<string>();

        [JsonPropertyName("suggestedResponse")]
        public string? SuggestedResponse { get; set; }

        public string Provider { get; set; } = "Groq";

        public string Model { get; set; } = "openai/gpt-oss-20b";

        public bool IsFallback { get; set; } = false;

        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    }

    public class AIDraftResponseRequest
    {
        public int LeadId { get; set; }
        public string? UserTone { get; set; } = "Professional and persuasive";
        public string? CustomInstructions { get; set; }
    }

    public class ManagerPipelineSummaryDto
    {
        public string ObservationSummary { get; set; } = string.Empty;
        public string UrgentAttentionItems { get; set; } = string.Empty;
        public string RevenueOpportunityForecast { get; set; } = string.Empty;
        public string GeneratedAt { get; set; } = DateTime.UtcNow.ToString("g");
    }
}
