using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AI_Powered_CRM.Models.Entities
{
    public class SupportAccessTicket
    {
        [Key]
        public Guid TicketId { get; set; } = Guid.NewGuid();

        [Required]
        [MaxLength(50)]
        public string TicketNumber { get; set; } = string.Empty;

        [Required]
        public Guid TenantId { get; set; }

        [ForeignKey(nameof(TenantId))]
        public virtual Tenant? Tenant { get; set; }

        [Required]
        [MaxLength(450)]
        public string SuperAdminUserId { get; set; } = string.Empty;

        [ForeignKey(nameof(SuperAdminUserId))]
        public virtual ApplicationUser? SuperAdminUser { get; set; }

        [Required]
        [MaxLength(450)]
        public string CreatedByUserId { get; set; } = string.Empty;

        [ForeignKey(nameof(CreatedByUserId))]
        public virtual ApplicationUser? CreatedByUser { get; set; }

        [Required]
        [MaxLength(500)]
        public string Reason { get; set; } = string.Empty;

        [Required]
        [MaxLength(30)]
        [ConcurrencyCheck]
        public string Status { get; set; } = "Pending";

        public DateTime? ApprovedAt { get; set; }

        [MaxLength(450)]
        public string? ApprovedByUserId { get; set; }

        [ForeignKey(nameof(ApprovedByUserId))]
        public virtual ApplicationUser? ApprovedByUser { get; set; }

        public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddHours(4);

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [NotMapped]
        public bool IsActive => Status == "Approved" 
                             && !string.IsNullOrWhiteSpace(SuperAdminUserId) 
                             && DateTime.UtcNow <= ExpiresAt;
    }
}
