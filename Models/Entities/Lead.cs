using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using AI_Powered_CRM.Models.Enums;
using AI_Powered_CRM.Models.Interfaces;

namespace AI_Powered_CRM.Models.Entities
{
    public class Lead : ITenantEntity
    {
        [Key]
        public int LeadId { get; set; }

        [Required]
        public Guid TenantId { get; set; }

        [ForeignKey(nameof(TenantId))]
        public virtual Tenant? Tenant { get; set; }

        [Required]
        [MaxLength(120)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [MaxLength(30)]
        public string Phone { get; set; } = string.Empty;

        [MaxLength(180)]
        [EmailAddress]
        public string? Email { get; set; }

        [MaxLength(150)]
        public string? Company { get; set; }

        [Required]
        [MaxLength(60)]
        public string Source { get; set; } = "Website";

        [Required]
        [MaxLength(150)]
        public string InterestedProduct { get; set; } = string.Empty;

        [Column(TypeName = "decimal(18,2)")]
        [Range(0, 100000000)]
        public decimal? Budget { get; set; }

        [Required]
        public LeadStatus Status { get; set; } = LeadStatus.New;

        [Required]
        public LeadPriority Priority { get; set; } = LeadPriority.Medium;

        [MaxLength(450)]
        public string? OwnerUserId { get; set; }

        [ForeignKey(nameof(OwnerUserId))]
        public virtual ApplicationUser? OwnerUser { get; set; }

        public bool IsArchived { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public virtual ICollection<LeadActivity> Activities { get; set; } = new List<LeadActivity>();
        public virtual ICollection<FollowUp> FollowUps { get; set; } = new List<FollowUp>();
        public virtual ICollection<LeadStatusHistory> StatusHistories { get; set; } = new List<LeadStatusHistory>();
        public virtual ICollection<AILeadInsight> AIInsights { get; set; } = new List<AILeadInsight>();
    }
}
