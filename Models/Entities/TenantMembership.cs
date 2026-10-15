using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using AI_Powered_CRM.Models.Enums;

namespace AI_Powered_CRM.Models.Entities
{
    public class TenantMembership
    {
        [Key]
        public Guid MembershipId { get; set; } = Guid.NewGuid();

        [Required]
        public Guid TenantId { get; set; }

        [ForeignKey(nameof(TenantId))]
        public virtual Tenant? Tenant { get; set; }

        [Required]
        [MaxLength(450)]
        public string UserId { get; set; } = string.Empty;

        [ForeignKey(nameof(UserId))]
        public virtual ApplicationUser? User { get; set; }

        [Required]
        public CompanyRole CompanyRole { get; set; } = CompanyRole.SalesExecutive;

        public bool IsActive { get; set; } = true;

        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

        [MaxLength(450)]
        public string? InvitedByUserId { get; set; }

        [ForeignKey(nameof(InvitedByUserId))]
        public virtual ApplicationUser? InvitedByUser { get; set; }
    }
}
