using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AI_Powered_CRM.Models.Entities
{
    public class SubscriptionPlan
    {
        [Key]
        public Guid PlanId { get; set; } = Guid.NewGuid();

        [Required, MaxLength(60)]
        public string Code { get; set; } = string.Empty;

        [Required, MaxLength(120)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(300)]
        public string Tagline { get; set; } = string.Empty;

        public decimal MonthlyPriceInr { get; set; }
        public decimal YearlyPriceInr { get; set; }
        public int TrialDays { get; set; }
        public int MaxUserSeats { get; set; }
        public int MaxLeadsQuota { get; set; }
        public int MonthlyAiRequestLimit { get; set; }
        public bool IsRecommended { get; set; }
        public bool IsActive { get; set; } = true;
        public bool IsPublic { get; set; } = true;
        public int SortOrder { get; set; }
        public string HighlightsJson { get; set; } = "[]";
        public string IncludedFeaturesJson { get; set; } = "[]";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public virtual ICollection<Tenant> Tenants { get; set; } = new List<Tenant>();
    }
}