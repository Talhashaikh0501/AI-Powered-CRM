using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using AI_Powered_CRM.Models.Interfaces;

namespace AI_Powered_CRM.Models.Entities
{
    public class Notification : ITenantEntity
    {
        [Key]
        public int NotificationId { get; set; }

        [Required]
        public Guid TenantId { get; set; }

        [ForeignKey(nameof(TenantId))]
        public virtual Tenant? Tenant { get; set; }

        [Required]
        [MaxLength(450)]
        public string UserId { get; set; } = string.Empty;

        [ForeignKey(nameof(UserId))]
        public virtual ApplicationUser User { get; set; } = null!;

        public int? LeadId { get; set; }

        [ForeignKey(nameof(LeadId))]
        public virtual Lead? Lead { get; set; }

        [Required]
        [MaxLength(120)]
        public string Title { get; set; } = string.Empty;

        [Required]
        [MaxLength(500)]
        public string Message { get; set; } = string.Empty;

        public bool IsRead { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
