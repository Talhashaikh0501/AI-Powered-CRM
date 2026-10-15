using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using AI_Powered_CRM.Models.Enums;
using AI_Powered_CRM.Models.Interfaces;

namespace AI_Powered_CRM.Models.Entities
{
    public class LeadActivity : ITenantEntity
    {
        [Key]
        public int ActivityId { get; set; }

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
        public string UserId { get; set; } = string.Empty;

        [ForeignKey(nameof(UserId))]
        public virtual ApplicationUser User { get; set; } = null!;

        [Required]
        public ActivityType Type { get; set; } = ActivityType.Note;

        [Required]
        public string Description { get; set; } = string.Empty;

        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    }
}
