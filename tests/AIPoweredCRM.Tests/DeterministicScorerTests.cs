using System;
using System.Collections.Generic;
using AI_Powered_CRM.AI;
using AI_Powered_CRM.Models.Entities;
using AI_Powered_CRM.Models.Enums;
using Xunit;

namespace AIPoweredCRM.Tests
{
    public class DeterministicScorerTests
    {
        private readonly DeterministicFallbackScorer _scorer = new DeterministicFallbackScorer();

        [Fact]
        public void TC018_HighScore_WhenHighBudget_AndHighEngagement()
        {
            // Arrange
            var lead = new Lead
            {
                LeadId = 1,
                Name = "Enterprise Prospect",
                Budget = 120000m,
                Status = LeadStatus.Proposal,
                CreatedAt = DateTime.UtcNow.AddDays(-5)
            };

            var activities = new List<LeadActivity>
            {
                new LeadActivity { Type = ActivityType.Call, OccurredAt = DateTime.UtcNow.AddDays(-4), Description = "Call 1" },
                new LeadActivity { Type = ActivityType.Meeting, OccurredAt = DateTime.UtcNow.AddDays(-2), Description = "Demo" },
                new LeadActivity { Type = ActivityType.Email, OccurredAt = DateTime.UtcNow.AddDays(-1), Description = "Proposal sent" },
                new LeadActivity { Type = ActivityType.Note, OccurredAt = DateTime.UtcNow.AddHours(-5), Description = "Note" }
            };

            var followUps = new List<FollowUp>
            {
                new FollowUp { Status = FollowUpStatus.Pending, DueAt = DateTime.UtcNow.AddDays(1), Type = FollowUpType.Call }
            };

            // Act
            var result = _scorer.CalculateDeterministicScore(lead, activities, followUps);

            // Assert
            Assert.True(result.Score >= 75, $"Expected score >= 75 for hot enterprise deal, got {result.Score}");
            Assert.Equal("Hot", result.Classification);
            Assert.NotEmpty(result.Reasons);
            Assert.Contains(result.Reasons, r => r.Contains("budget", StringComparison.OrdinalIgnoreCase));
            Assert.False(string.IsNullOrWhiteSpace(result.NextBestAction));
        }

        [Fact]
        public void TC019_HighRisk_WhenOverdueFollowUp_AndInactivity()
        {
            // Arrange
            var lead = new Lead
            {
                LeadId = 2,
                Name = "Stale Lead",
                Budget = 15000m,
                Status = LeadStatus.Contacted,
                CreatedAt = DateTime.UtcNow.AddDays(-20)
            };

            var activities = new List<LeadActivity>(); // 0 activities

            var followUps = new List<FollowUp>
            {
                new FollowUp { Status = FollowUpStatus.Pending, DueAt = DateTime.UtcNow.AddDays(-3), Type = FollowUpType.Call } // Overdue!
            };

            // Act
            var result = _scorer.CalculateDeterministicScore(lead, activities, followUps);

            // Assert
            Assert.True(result.Score < 40, $"Expected score < 40 for cold stale lead, got {result.Score}");
            Assert.Equal("Cold", result.Classification);
            Assert.Equal("High", result.RiskLevel);
            Assert.Equal("Urgent", result.Urgency);
            Assert.Contains(result.Reasons, r => r.Contains("Overdue", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void TC020_ScoreIsAlwaysClampedBetweenZeroAndHundred()
        {
            // Arrange extreme negative
            var deadLead = new Lead
            {
                LeadId = 3,
                Name = "Dormant",
                Budget = 0m,
                Status = LeadStatus.Lost,
                CreatedAt = DateTime.UtcNow.AddDays(-100)
            };

            var overdues = new List<FollowUp>
            {
                new FollowUp { Status = FollowUpStatus.Pending, DueAt = DateTime.UtcNow.AddDays(-30) }
            };

            // Act
            var result = _scorer.CalculateDeterministicScore(deadLead, new List<LeadActivity>(), overdues);

            // Assert
            Assert.InRange(result.Score, 0, 100);
        }
    }
}
