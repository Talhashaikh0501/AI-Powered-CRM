using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using AI_Powered_CRM.Models.Enums;
using AI_Powered_CRM.Models.Interfaces;

namespace AI_Powered_CRM.Models.Entities
{
    public class LeadStatusHistory : ITenantEntity
    {
        [Key]
        public int HistoryId { get; set; }

        [Required]
        public Guid TenantId { get; set; }

        [ForeignKey(nameof(TenantId))]
        public virtual Tenant? Tenant { get; set; }

        [Required]
        public int LeadId { get; set; }

        [ForeignKey(nameof(LeadId))]
        public virtual Lead Lead { get; set; } = null!;

        [Required]
        public LeadStatus OldStatus { get; set; }

        [Required]
        public LeadStatus NewStatus { get; set; }

        [Required]
        [MaxLength(450)]
        public string ChangedByUserId { get; set; } = string.Empty;

        [ForeignKey(nameof(ChangedByUserId))]
        public virtual ApplicationUser ChangedByUser { get; set; } = null!;

        [MaxLength(500)]
        public string? Notes { get; set; }

        public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
    }
}
