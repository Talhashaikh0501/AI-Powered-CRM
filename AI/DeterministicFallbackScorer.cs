using System;
using System.Collections.Generic;
using System.Linq;
using AI_Powered_CRM.DTOs;
using AI_Powered_CRM.Helpers;
using AI_Powered_CRM.Models.Entities;
using AI_Powered_CRM.Models.Enums;

namespace AI_Powered_CRM.AI
{
    public class DeterministicFallbackScorer : IDeterministicFallbackScorer
    {
        public AILeadAnalysisResult CalculateDeterministicScore(Lead lead, List<LeadActivity> activities, List<FollowUp> followUps)
        {
            var reasons = new List<string>();
            int score = 30; // base score

            // 1. Budget Fit Analysis
            if (lead.Budget.HasValue)
            {
                if (lead.Budget.Value >= 100000m)
                {
                    score += 25;
                    reasons.Add("High-value enterprise budget fit (" + CurrencyHelper.FormatINRRounded(lead.Budget.Value) + ")");
                }
                else if (lead.Budget.Value >= 50000m)
                {
                    score += 15;
                    reasons.Add("Strong mid-market budget (" + CurrencyHelper.FormatINRRounded(lead.Budget.Value) + ")");
                }
                else if (lead.Budget.Value >= 20000m)
                {
                    score += 8;
                    reasons.Add("Standard budget allocation (" + CurrencyHelper.FormatINRRounded(lead.Budget.Value) + ")");
                }
                else
                {
                    score -= 5;
                    reasons.Add("Lower budget bracket (<" + CurrencyHelper.FormatINRRounded(20000m) + ")");
                }
            }
            else
            {
                score -= 5;
                reasons.Add("Budget unconfirmed; needs qualification");
            }

            // 2. Engagement Touchpoints
            int activityCount = activities?.Count ?? 0;
            if (activityCount >= 4)
            {
                score += 20;
                reasons.Add("High prospect engagement (" + activityCount + " interactions recorded)");
            }
            else if (activityCount >= 2)
            {
                score += 12;
                reasons.Add("Active relationship (" + activityCount + " touchpoints logged)");
            }
            else if (activityCount == 1)
            {
                score += 5;
                reasons.Add("Initial contact initiated");
            }
            else
            {
                score -= 15;
                reasons.Add("No touchpoints logged yet");
            }

            // 3. Pipeline Progression
            switch (lead.Status)
            {
                case LeadStatus.Negotiation:
                    score += 25;
                    reasons.Add("Advanced stage: Negotiation in progress");
                    break;
                case LeadStatus.Proposal:
                    score += 20;
                    reasons.Add("Commercial proposal under client review");
                    break;
                case LeadStatus.Qualified:
                    score += 15;
                    reasons.Add("Requirements successfully qualified");
                    break;
                case LeadStatus.Interested:
                    score += 10;
                    reasons.Add("Prospect has confirmed active interest");
                    break;
                case LeadStatus.Contacted:
                    score += 5;
                    reasons.Add("Initial conversation established");
                    break;
                case LeadStatus.Converted:
                    score = 100;
                    reasons.Add("Deal won and converted!");
                    break;
                case LeadStatus.Lost:
                    score = 10;
                    reasons.Add("Deal closed as Lost");
                    break;
                default:
                    break;
            }

            // 4. Recency & Inactivity
            var lastActivityDate = activities?.OrderByDescending(a => a.OccurredAt).FirstOrDefault()?.OccurredAt ?? lead.CreatedAt;
            var daysInactive = (DateTime.UtcNow - lastActivityDate).TotalDays;

            if (daysInactive <= 3)
            {
                score += 10;
                reasons.Add("Recent activity within last 72 hours");
            }
            else if (daysInactive > 14)
            {
                score -= 20;
                reasons.Add("Warning: Inactive for " + Math.Round(daysInactive) + " days without communication");
            }
            else if (daysInactive > 7)
            {
                score -= 10;
                reasons.Add("Caution: " + Math.Round(daysInactive) + " days since last client touchpoint");
            }

            // 5. Follow-ups
            bool hasOverdueFollowUp = followUps?.Any(f => f.Status == FollowUpStatus.Pending && f.DueAt < DateTime.UtcNow) ?? false;
            bool hasUpcomingFollowUp = followUps?.Any(f => f.Status == FollowUpStatus.Pending && f.DueAt >= DateTime.UtcNow) ?? false;

            if (hasOverdueFollowUp)
            {
                score -= 25;
                reasons.Add("Urgent: Overdue follow-up pending attention");
            }
            else if (hasUpcomingFollowUp)
            {
                score += 10;
                reasons.Add("Next follow-up already scheduled on calendar");
            }
            else if (lead.Status != LeadStatus.Converted && lead.Status != LeadStatus.Lost)
            {
                score -= 10;
                reasons.Add("No next follow-up currently scheduled");
            }

            // Clamp score
            score = Math.Clamp(score, 0, 100);

            // Classification
            string classification = score >= 75 ? "Hot" : (score >= 40 ? "Warm" : "Cold");

            // Risk Level
            string riskLevel = "Low";
            if (hasOverdueFollowUp || daysInactive > 14 || lead.Status == LeadStatus.Lost)
            {
                riskLevel = "High";
            }
            else if (daysInactive > 7 || !hasUpcomingFollowUp)
            {
                riskLevel = "Medium";
            }

            // Urgency
            string urgency = "Normal";
            if (hasOverdueFollowUp || (score >= 75 && daysInactive > 3))
            {
                urgency = "Urgent";
            }
            else if (score >= 70 || hasUpcomingFollowUp)
            {
                urgency = "High";
            }

            // Summary
            string summary = $"{classification} priority lead ({lead.Name}) interested in {lead.InterestedProduct} from {lead.Source} source. " +
                             $"Current status is {lead.Status} with a budget of {(lead.Budget.HasValue ? CurrencyHelper.FormatINRRounded(lead.Budget.Value) : "unspecified")}. " +
                             $"Last touched {Math.Round(daysInactive)} days ago with {activityCount} total interactions logged.";

            // Next Best Action
            string nextBestAction;
            if (hasOverdueFollowUp)
            {
                nextBestAction = "Immediately complete the overdue follow-up task and re-engage the customer via direct phone call.";
            }
            else if (lead.Status == LeadStatus.New)
            {
                nextBestAction = "Conduct an initial discovery call within 2 hours to qualify timeline and key pain points.";
            }
            else if (lead.Status == LeadStatus.Qualified)
            {
                nextBestAction = "Prepare and dispatch formal commercial proposal outlining deliverables and milestones.";
            }
            else if (lead.Status == LeadStatus.Proposal)
            {
                nextBestAction = "Schedule a review meeting to discuss proposal terms and answer decision-maker questions.";
            }
            else if (lead.Status == LeadStatus.Negotiation)
            {
                nextBestAction = "Finalize contract terms, offer approved closing incentives, and request signature.";
            }
            else if (!hasUpcomingFollowUp && lead.Status != LeadStatus.Converted && lead.Status != LeadStatus.Lost)
            {
                nextBestAction = "Schedule the next follow-up call to maintain sales momentum and prevent deal staleness.";
            }
            else
            {
                nextBestAction = "Maintain regular relationship cadence and track customer satisfaction.";
            }

            // Suggested response
            string suggestedResponse = $"Hi {lead.Name}, I hope you're having a productive week. Following up regarding your interest in {lead.InterestedProduct}—I'd love to share how our team can help you achieve your goals. Do you have 10 minutes for a brief call tomorrow?";

            return new AILeadAnalysisResult
            {
                Classification = classification,
                Score = score,
                Summary = summary,
                NextBestAction = nextBestAction,
                RiskLevel = riskLevel,
                Urgency = urgency,
                Reasons = reasons,
                SuggestedResponse = suggestedResponse,
                Provider = "RulesEngine",
                Model = "Deterministic-v1",
                IsFallback = true,
                GeneratedAt = DateTime.UtcNow
            };
        }
    }
}
