using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using AI_Powered_CRM.Models.Enums;

namespace AI_Powered_CRM.Models.Entities
{
    public class Tenant
    {
        [Key]
        public Guid TenantId { get; set; } = Guid.NewGuid();

        [Required]
        [MaxLength(150)]
        public string CompanyName { get; set; } = string.Empty;

        [Required]
        [MaxLength(60)]
        public string Slug { get; set; } = string.Empty;

        [Required]
        [MaxLength(180)]
        [EmailAddress]
        public string ContactEmail { get; set; } = string.Empty;

        [MaxLength(30)]
        public string? Phone { get; set; }

        [Required]
        [MaxLength(10)]
        public string DefaultCurrency { get; set; } = "INR";

        [Required]
        public SubscriptionTier SubscriptionTier { get; set; } = SubscriptionTier.FreeTrial;

        [Required]
        public SubscriptionStatus SubscriptionStatus { get; set; } = SubscriptionStatus.Trialing;

        public DateTime? TrialEndsAt { get; set; } = DateTime.UtcNow.AddDays(14);

        public DateTime? SubscriptionRenewsAt { get; set; }

        public int MaxUserSeats { get; set; } = 5;

        public int MaxLeadsQuota { get; set; } = 1000;

        public int MonthlyAiRequestLimit { get; set; } = 200;

        public int CurrentMonthAiUsageCount { get; set; } = 0;

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public virtual TenantSetting? Settings { get; set; }
        public virtual ICollection<TenantMembership> Memberships { get; set; } = new List<TenantMembership>();
        public virtual ICollection<Lead> Leads { get; set; } = new List<Lead>();
    }
}
