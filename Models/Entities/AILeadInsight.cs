using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using AI_Powered_CRM.Models.Enums;
using AI_Powered_CRM.Models.Interfaces;

namespace AI_Powered_CRM.Models.Entities
{
    public class AILeadInsight : ITenantEntity
    {
        [Key]
        public long InsightId { get; set; }

        [Required]
        public Guid TenantId { get; set; }

        [ForeignKey(nameof(TenantId))]
        public virtual Tenant? Tenant { get; set; }

        [Required]
        public int LeadId { get; set; }

        [ForeignKey(nameof(LeadId))]
        public virtual Lead Lead { get; set; } = null!;

        [Required]
        public AIClassification Classification { get; set; } = AIClassification.Warm;

        [Range(0, 100)]
        public int Score { get; set; }

        [Required]
        public string Summary { get; set; } = string.Empty;

        [MaxLength(500)]
        public string NextBestAction { get; set; } = string.Empty;

        [Required]
        public RiskLevel RiskLevel { get; set; } = RiskLevel.Low;

        [Required]
        public UrgencyLevel Urgency { get; set; } = UrgencyLevel.Normal;

        public string ReasonJson { get; set; } = "[]";

        public string? SuggestedResponse { get; set; }

        [MaxLength(50)]
        public string Provider { get; set; } = "RuleEngine";

        [MaxLength(80)]
        public string Model { get; set; } = "Deterministic";

        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    }
}
