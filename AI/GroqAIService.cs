using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AI_Powered_CRM.DTOs;
using AI_Powered_CRM.Helpers;
using AI_Powered_CRM.Models.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AI_Powered_CRM.AI
{
    public class GroqAIService : IAIService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly IDeterministicFallbackScorer _fallbackScorer;
        private readonly ILogger<GroqAIService> _logger;
        private readonly Services.Interfaces.ISubscriptionQuotaService? _quotaService;
        private readonly Security.ITenantContext? _tenantContext;

        public GroqAIService(
            HttpClient httpClient,
            IConfiguration configuration,
            IDeterministicFallbackScorer fallbackScorer,
            ILogger<GroqAIService> logger,
            Services.Interfaces.ISubscriptionQuotaService? quotaService = null,
            Security.ITenantContext? tenantContext = null)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _fallbackScorer = fallbackScorer;
            _logger = logger;
            _quotaService = quotaService;
            _tenantContext = tenantContext;

            _httpClient.Timeout = TimeSpan.FromSeconds(10);
        }

        public async Task<AILeadAnalysisResult> AnalyzeLeadAsync(Lead lead, List<LeadActivity> activities, List<FollowUp> followUps)
        {
            if (_tenantContext != null && _tenantContext.IsResolved && !_tenantContext.IsPlatformSuperAdmin)
            {
                if (lead.TenantId != Guid.Empty && lead.TenantId != _tenantContext.CurrentTenantId)
                {
                    throw new System.Security.SecurityException(
                        $"Cross-tenant AI inference rejected: Lead belongs to tenant {lead.TenantId}, but active tenant context is {_tenantContext.CurrentTenantId}.");
                }

                if (lead.TenantId == Guid.Empty)
                {
                    lead.TenantId = _tenantContext.CurrentTenantId;
                }
            }

            if (_quotaService != null && lead.TenantId != Guid.Empty)
            {
                var canExecute = await _quotaService.CanExecuteAiInferenceAsync(lead.TenantId);
                if (!canExecute)
                {
                    _logger.LogWarning("Monthly AI request limit exceeded for tenant {TenantId}. Falling back to deterministic scorer.", lead.TenantId);
                    return _fallbackScorer.CalculateDeterministicScore(lead, activities, followUps);
                }
            }

            var apiKey = GetApiKey();
            var model = _configuration["AI:Model"] ?? "openai/gpt-oss-20b";
            var endpoint = _configuration["AI:Endpoint"] ?? "https://api.groq.com/openai/v1/chat/completions";

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _logger.LogWarning("No AI API key found in configuration. Falling back to deterministic scorer.");
                return _fallbackScorer.CalculateDeterministicScore(lead, activities, followUps);
            }

            try
            {
                var systemPrompt = @"You are a senior CRM AI analyst. Analyze the provided sales lead and its history.
Return ONLY valid JSON matching this exact schema:
{
  ""classification"": ""Hot"" | ""Warm"" | ""Cold"",
  ""score"": integer between 0 and 100,
  ""summary"": ""concise factual summary of current lead state (max 2 sentences)"",
  ""nextBestAction"": ""specific recommended next sales action with timing"",
  ""riskLevel"": ""Low"" | ""Medium"" | ""High"",
  ""urgency"": ""Normal"" | ""High"" | ""Urgent"",
  ""reasons"": [""clear reason 1"", ""clear reason 2"", ""clear reason 3""],
  ""suggestedResponse"": ""personalized, professional follow-up message draft to send the lead""
}
Do NOT invent fake facts. Base your analysis purely on the lead details and activity history.";

                var userPrompt = BuildLeadContext(lead, activities, followUps);

                var payload = new
                {
                    model = model,
                    messages = new object[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = userPrompt }
                    },
                    temperature = 0.2,
                    response_format = new { type = "json_object" }
                };

                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("AI API returned status code {StatusCode}. Falling back to deterministic rules.", response.StatusCode);
                    return _fallbackScorer.CalculateDeterministicScore(lead, activities, followUps);
                }

                var responseJson = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(responseJson);
                var content = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();

                if (string.IsNullOrWhiteSpace(content))
                {
                    return _fallbackScorer.CalculateDeterministicScore(lead, activities, followUps);
                }

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var result = JsonSerializer.Deserialize<AILeadAnalysisResult>(content, options);

                if (result == null)
                {
                    return _fallbackScorer.CalculateDeterministicScore(lead, activities, followUps);
                }

                // Sanitize and validate
                result.Score = Math.Clamp(result.Score, 0, 100);
                result.Classification = NormalizeClassification(result.Classification, result.Score);
                result.RiskLevel = NormalizeRisk(result.RiskLevel);
                result.Urgency = NormalizeUrgency(result.Urgency);
                result.Provider = "Groq";
                result.Model = model;
                result.IsFallback = false;
                result.GeneratedAt = DateTime.UtcNow;

                if (_quotaService != null && lead.TenantId != Guid.Empty)
                {
                    await _quotaService.RecordAiUsageAsync(lead.TenantId);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing AI API call for Lead {LeadId}. Engaging deterministic fallback.", lead.LeadId);
                return _fallbackScorer.CalculateDeterministicScore(lead, activities, followUps);
            }
        }

        public async Task<string> GenerateDraftResponseAsync(Lead lead, List<LeadActivity> activities, string? userTone, string? customInstructions)
        {
            if (_tenantContext != null && _tenantContext.IsResolved && !_tenantContext.IsPlatformSuperAdmin)
            {
                if (lead.TenantId != Guid.Empty && lead.TenantId != _tenantContext.CurrentTenantId)
                {
                    throw new System.Security.SecurityException(
                        $"Cross-tenant AI draft rejected: Lead belongs to tenant {lead.TenantId}, but active tenant context is {_tenantContext.CurrentTenantId}.");
                }
            }

            var apiKey = GetApiKey();
            var model = _configuration["AI:Model"] ?? "openai/gpt-oss-20b";
            var endpoint = _configuration["AI:Endpoint"] ?? "https://api.groq.com/openai/v1/chat/completions";

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return $"Hi {lead.Name}, following up on your interest in {lead.InterestedProduct}. When would be a convenient time for a brief 10-minute discussion this week?";
            }

            try
            {
                var safeTone = !string.IsNullOrWhiteSpace(userTone) 
                    ? SanitizePromptInput(userTone, 100) 
                    : "Professional, warm and persuasive";
                var safeInstructions = !string.IsNullOrWhiteSpace(customInstructions) 
                    ? SanitizePromptInput(customInstructions, 200) 
                    : "Keep it concise and end with a clear, low-friction call to action.";

                var systemPrompt = $"You are an expert sales communications copywriter. Tone: {safeTone}. Additional instructions: {safeInstructions}";
                var userPrompt = $"Write a personalized follow-up message to client '{lead.Name}' from company '{lead.Company ?? "Individual"}' who is interested in '{lead.InterestedProduct}'. Recent activity summary: {string.Join(" | ", activities.Take(3).Select(a => $"{a.Type}: {a.Description}"))}";

                var payload = new
                {
                    model = model,
                    messages = new object[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = userPrompt }
                    },
                    temperature = 0.5
                };

                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(responseJson);
                    var text = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
                    if (!string.IsNullOrWhiteSpace(text)) return text.Trim();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI draft response generation failed.");
            }

            return $"Hi {lead.Name}, following up on your interest in {lead.InterestedProduct}. Would you be open to a quick call tomorrow to discuss your requirements?";
        }

        public async Task<ManagerPipelineSummaryDto> GeneratePipelineSummaryAsync(DashboardSummaryDto dashboardData)
        {
            var apiKey = GetApiKey();
            var model = _configuration["AI:Model"] ?? "openai/gpt-oss-20b";
            var endpoint = _configuration["AI:Endpoint"] ?? "https://api.groq.com/openai/v1/chat/completions";

            var fallback = new ManagerPipelineSummaryDto
            {
                ObservationSummary = $"Pipeline health is solid with {dashboardData.TotalActiveLeads} active deals totaling {CurrencyHelper.FormatINRRounded(dashboardData.TotalPipelineValue)} in active pipeline value. Win rate currently sits at {dashboardData.WinRatePercentage:F1}%.",
                UrgentAttentionItems = dashboardData.FollowUpsOverdueCount > 0
                    ? $"{dashboardData.FollowUpsOverdueCount} follow-ups are overdue and require immediate rep follow-up."
                    : "No overdue follow-ups detected. Sales reps are maintaining active momentum.",
                RevenueOpportunityForecast = $"With {dashboardData.HotLeadsCount} high-probability hot deals, projected near-term closings represent an estimated {CurrencyHelper.FormatINRRounded(dashboardData.TotalPipelineValue * 0.45m)} in conversion potential.",
                GeneratedAt = DateTime.UtcNow.ToString("g")
            };

            if (string.IsNullOrWhiteSpace(apiKey)) return fallback;

            try
            {
                var prompt = $@"Given these CRM metrics:
- Active Leads: {dashboardData.TotalActiveLeads}
- Hot Leads: {dashboardData.HotLeadsCount}
- Pipeline Value: {CurrencyHelper.FormatINRRounded(dashboardData.TotalPipelineValue)}
- Won Deal Value (Est.): {CurrencyHelper.FormatINRRounded(dashboardData.EstimatedConvertedValue)}
- Win Rate: {dashboardData.WinRatePercentage:F1}%
- Overdue Follow-ups: {dashboardData.FollowUpsOverdueCount}

Provide an executive sales summary in valid JSON format:
{{
  ""observationSummary"": ""2-3 sentence strategic executive summary"",
  ""urgentAttentionItems"": ""Key risks or bottlenecks needing immediate management intervention"",
  ""revenueOpportunityForecast"": ""Actionable revenue forecast and closing outlook""
}}";

                var payload = new
                {
                    model = model,
                    messages = new object[]
                    {
                        new { role = "system", content = "You are a Chief Revenue Officer delivering concise executive pipeline intelligence." },
                        new { role = "user", content = prompt }
                    },
                    temperature = 0.3,
                    response_format = new { type = "json_object" }
                };

                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(responseJson);
                    var text = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        var res = JsonSerializer.Deserialize<ManagerPipelineSummaryDto>(text, options);
                        if (res != null)
                        {
                            res.GeneratedAt = DateTime.UtcNow.ToString("g");
                            return res;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to generate AI pipeline manager summary.");
            }

            return fallback;
        }

        private string? GetApiKey()
        {
            return _configuration["AI:ApiKey"]
                ?? _configuration["Groq:ApiKey"]
                ?? Environment.GetEnvironmentVariable("GROQ_API_KEY");
        }

        private string BuildLeadContext(Lead lead, List<LeadActivity> activities, List<FollowUp> followUps)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"LEAD DETAILS:");
            sb.AppendLine($"- Name: {SanitizePromptInput(lead.Name, 100)}");
            sb.AppendLine($"- Company: {SanitizePromptInput(lead.Company ?? "Individual", 100)}");
            sb.AppendLine($"- Source: {SanitizePromptInput(lead.Source, 50)}");
            sb.AppendLine($"- Interested Product: {SanitizePromptInput(lead.InterestedProduct, 200)}");
            sb.AppendLine($"- Budget: {(lead.Budget.HasValue ? CurrencyHelper.FormatINR(lead.Budget.Value) : "Not specified")}");
            sb.AppendLine($"- Current Status: {lead.Status}");
            sb.AppendLine($"- Priority: {lead.Priority}");
            sb.AppendLine($"- Created Date: {lead.CreatedAt:yyyy-MM-dd}");

            sb.AppendLine($"\nRECENT ACTIVITIES ({activities?.Count ?? 0} total):");
            if (activities != null && activities.Any())
            {
                foreach (var a in activities.OrderByDescending(x => x.OccurredAt).Take(5))
                {
                    sb.AppendLine($"- [{a.OccurredAt:yyyy-MM-dd}] {a.Type}: {SanitizePromptInput(a.Description, 200)}");
                }
            }
            else
            {
                sb.AppendLine("- No interactions logged yet.");
            }

            sb.AppendLine($"\nFOLLOW-UP STATUS ({followUps?.Count ?? 0} total):");
            if (followUps != null && followUps.Any())
            {
                foreach (var f in followUps.OrderBy(x => x.DueAt).Take(4))
                {
                    sb.AppendLine($"- [{f.DueAt:yyyy-MM-dd}] {f.Type} (Status: {f.Status}) - {SanitizePromptInput(f.Notes, 200)}");
                }
            }
            else
            {
                sb.AppendLine("- No follow-ups scheduled.");
            }

            return sb.ToString();
        }

        private string NormalizeClassification(string? val, int score)
        {
            if (string.Equals(val, "Hot", StringComparison.OrdinalIgnoreCase)) return "Hot";
            if (string.Equals(val, "Cold", StringComparison.OrdinalIgnoreCase)) return "Cold";
            if (string.Equals(val, "Warm", StringComparison.OrdinalIgnoreCase)) return "Warm";
            return score >= 75 ? "Hot" : (score >= 40 ? "Warm" : "Cold");
        }

        private string NormalizeRisk(string? val)
        {
            if (string.Equals(val, "High", StringComparison.OrdinalIgnoreCase)) return "High";
            if (string.Equals(val, "Medium", StringComparison.OrdinalIgnoreCase)) return "Medium";
            return "Low";
        }

        private string NormalizeUrgency(string? val)
        {
            if (string.Equals(val, "Urgent", StringComparison.OrdinalIgnoreCase)) return "Urgent";
            if (string.Equals(val, "High", StringComparison.OrdinalIgnoreCase)) return "High";
            return "Normal";
        }

        private static string SanitizePromptInput(string? input, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            var trimmed = input.Trim();
            if (trimmed.Length > maxLength) trimmed = trimmed.Substring(0, maxLength);
            // Remove prompt injection markers and control delimiters
            trimmed = trimmed
                .Replace("```", "")
                .Replace("###", "")
                .Replace("<system>", "")
                .Replace("</system>", "")
                .Replace("<|im_start|>", "")
                .Replace("<|im_end|>", "")
                .Replace("[INST]", "")
                .Replace("[/INST]", "")
                .Replace("<script>", "")
                .Replace("</script>", "");
            return trimmed;
        }
    }
}
