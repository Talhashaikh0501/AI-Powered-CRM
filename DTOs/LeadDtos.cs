using System;
using System.ComponentModel.DataAnnotations;
using AI_Powered_CRM.Models.Enums;

namespace AI_Powered_CRM.DTOs
{
    public class CreateLeadDto
    {
        [Required(ErrorMessage = "Full Name is required.")]
        [StringLength(120, MinimumLength = 2)]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Phone number is required.")]
        [Phone(ErrorMessage = "Please enter a valid phone number.")]
        [StringLength(30)]
        public string Phone { get; set; } = string.Empty;

        [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
        [StringLength(180)]
        public string? Email { get; set; }

        [StringLength(150)]
        public string? Company { get; set; }

        [Required(ErrorMessage = "Lead source is required.")]
        [StringLength(60)]
        public string Source { get; set; } = "Website";

        [Required(ErrorMessage = "Interested product/service is required.")]
        [StringLength(150)]
        public string InterestedProduct { get; set; } = string.Empty;

        [Range(0, 100000000, ErrorMessage = "Budget must be a non-negative number.")]
        public decimal? Budget { get; set; }

        public LeadStatus Status { get; set; } = LeadStatus.New;

        public LeadPriority Priority { get; set; } = LeadPriority.Medium;

        public string? OwnerUserId { get; set; }
    }

    public class UpdateLeadDto
    {
        [Required]
        public int LeadId { get; set; }

        [Required(ErrorMessage = "Full Name is required.")]
        [StringLength(120, MinimumLength = 2)]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Phone number is required.")]
        [Phone]
        [StringLength(30)]
        public string Phone { get; set; } = string.Empty;

        [EmailAddress]
        [StringLength(180)]
        public string? Email { get; set; }

        [StringLength(150)]
        public string? Company { get; set; }

        [Required]
        [StringLength(60)]
        public string Source { get; set; } = "Website";

        [Required]
        [StringLength(150)]
        public string InterestedProduct { get; set; } = string.Empty;

        [Range(0, 100000000)]
        public decimal? Budget { get; set; }

        public LeadStatus Status { get; set; }

        public LeadPriority Priority { get; set; }

        public string? OwnerUserId { get; set; }
    }

    public class AssignLeadDto
    {
        [Required]
        public int LeadId { get; set; }

        [Required(ErrorMessage = "Please select a user to assign.")]
        public string NewOwnerUserId { get; set; } = string.Empty;
    }

    public class ChangeLeadStatusDto
    {
        [Required]
        public int LeadId { get; set; }

        [Required]
        public LeadStatus NewStatus { get; set; }

        [MaxLength(500)]
        public string? Notes { get; set; }
    }
}
