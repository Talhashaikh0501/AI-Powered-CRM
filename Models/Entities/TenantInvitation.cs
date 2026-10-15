using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using AI_Powered_CRM.Models.Enums;

namespace AI_Powered_CRM.Models.Entities
{
    public class TenantInvitation
    {
        [Key]
        public Guid InvitationId { get; set; } = Guid.NewGuid();

        [Required]
        public Guid TenantId { get; set; }

        [ForeignKey(nameof(TenantId))]
        public virtual Tenant? Tenant { get; set; }

        [Required]
        [MaxLength(180)]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public CompanyRole CompanyRole { get; set; } = CompanyRole.SalesExecutive;

        [Required]
        [MaxLength(256)]
        public string TokenHash { get; set; } = string.Empty;

        public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddDays(7);

        public bool IsAccepted { get; set; } = false;

        [Required]
        [MaxLength(450)]
        public string InvitedByUserId { get; set; } = string.Empty;

        [ForeignKey(nameof(InvitedByUserId))]
        public virtual ApplicationUser? InvitedByUser { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
