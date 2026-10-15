using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using AI_Powered_CRM.Models.Enums;

namespace AI_Powered_CRM.ViewModels
{
    public class RegisterCompanyViewModel
    {
        [Required(ErrorMessage = "Company name is required.")]
        [StringLength(150, MinimumLength = 2, ErrorMessage = "Company name must be between 2 and 150 characters.")]
        [Display(Name = "Company Name")]
        public string CompanyName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Company URL slug is required.")]
        [StringLength(60, MinimumLength = 3, ErrorMessage = "Slug must be between 3 and 60 characters.")]
        [RegularExpression(@"^[a-z0-9]+(?:-[a-z0-9]+)*$", ErrorMessage = "Slug can only contain lowercase letters, numbers, and hyphens (e.g., 'acme-corp').")]
        [Display(Name = "Workspace Slug")]
        public string Slug { get; set; } = string.Empty;

        [Required(ErrorMessage = "Founder / Admin full name is required.")]
        [StringLength(150, MinimumLength = 2, ErrorMessage = "Name must be between 2 and 150 characters.")]
        [Display(Name = "Your Full Name")]
        public string AdminFullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Work email address is required.")]
        [EmailAddress(ErrorMessage = "Please enter a valid work email address.")]
        [Display(Name = "Work Email")]
        public string ContactEmail { get; set; } = string.Empty;

        [Phone(ErrorMessage = "Please enter a valid phone number.")]
        [Display(Name = "Phone Number (Optional)")]
        public string? Phone { get; set; }

        [Required(ErrorMessage = "Password is required.")]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters long.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please confirm your password.")]
        [DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
        [Display(Name = "Confirm Password")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public class InviteUserViewModel
    {
        [Required(ErrorMessage = "Employee email address is required.")]
        [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
        [Display(Name = "Employee Email")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Company role is required.")]
        [Display(Name = "Assigned Role")]
        public CompanyRole CompanyRole { get; set; } = CompanyRole.SalesExecutive;
    }

    public class AcceptInvitationViewModel
    {
        [Required]
        public string Token { get; set; } = string.Empty;

        public string CompanyName { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public CompanyRole CompanyRole { get; set; } = CompanyRole.SalesExecutive;

        [Required(ErrorMessage = "Full name is required.")]
        [StringLength(150, MinimumLength = 2, ErrorMessage = "Name must be between 2 and 150 characters.")]
        [Display(Name = "Full Name")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password is required.")]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters long.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please confirm your password.")]
        [DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
        [Display(Name = "Confirm Password")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public class TenantInvitationItemDto
    {
        public Guid InvitationId { get; set; }
        public string Email { get; set; } = string.Empty;
        public CompanyRole CompanyRole { get; set; }
        public bool IsAccepted { get; set; }
        public DateTime ExpiresAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public string InvitedByUserName { get; set; } = string.Empty;
        public bool IsExpired => !IsAccepted && DateTime.UtcNow > ExpiresAt;
    }

    public class AdminInvitationsViewModel
    {
        public List<TenantInvitationItemDto> Invitations { get; set; } = new List<TenantInvitationItemDto>();
        public InviteUserViewModel NewInvite { get; set; } = new InviteUserViewModel();
        public int ActiveUserSeats { get; set; }
        public int MaxUserSeats { get; set; }
        public bool CanInviteMore => ActiveUserSeats < MaxUserSeats;
    }
}
