using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using AI_Powered_CRM.Models.Enums;
using AI_Powered_CRM.Models.Interfaces;

namespace AI_Powered_CRM.Models.Entities
{
    public class FollowUp : ITenantEntity
    {
        [Key]
        public int FollowUpId { get; set; }

        [Required]
        public Guid TenantId { get; set; }

        [ForeignKey(nameof(TenantId))]
        public virtual Tenant? Tenant { get; set; }

        [Required]
        public int LeadId { get; set; }

        [ForeignKey(nameof(LeadId))]
        public virtual Lead Lead { get; set; } = null!;

        [Required]
        [MaxLength(450)]
        public string AssignedUserId { get; set; } = string.Empty;

        [ForeignKey(nameof(AssignedUserId))]
        public virtual ApplicationUser AssignedUser { get; set; } = null!;

        [Required]
        public DateTime DueAt { get; set; }

        [Required]
        public FollowUpType Type { get; set; } = FollowUpType.Call;

        [MaxLength(1000)]
        public string? Notes { get; set; }

        [Required]
        public FollowUpStatus Status { get; set; } = FollowUpStatus.Pending;

        public DateTime? CompletedAt { get; set; }
    }
}
