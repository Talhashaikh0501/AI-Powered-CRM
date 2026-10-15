using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AI_Powered_CRM.ViewModels
{
    public class CompanySettingsViewModel
    {
        public Guid TenantId { get; set; }

        [Required(ErrorMessage = "Company name is required.")]
        [StringLength(150, MinimumLength = 2, ErrorMessage = "Company name must be between 2 and 150 characters.")]
        [Display(Name = "Company Name")]
        public string CompanyName { get; set; } = string.Empty;

        [Display(Name = "Workspace Slug")]
        public string Slug { get; set; } = string.Empty;

        [Required(ErrorMessage = "Contact email is required.")]
        [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
        [Display(Name = "Contact / Billing Email")]
        public string ContactEmail { get; set; } = string.Empty;

        [Phone(ErrorMessage = "Please enter a valid phone number.")]
        [Display(Name = "Phone Number (Optional)")]
        public string? Phone { get; set; }

        [Required(ErrorMessage = "Currency code is required.")]
        [StringLength(10)]
        [Display(Name = "Currency Code")]
        public string CurrencyCode { get; set; } = "INR";

        [Required(ErrorMessage = "Currency symbol is required.")]
        [StringLength(10)]
        [Display(Name = "Currency Symbol")]
        public string CurrencySymbol { get; set; } = "₹";

        [Required(ErrorMessage = "Date format is required.")]
        [StringLength(30)]
        [Display(Name = "Date Format")]
        public string DateFormat { get; set; } = "dd/MM/yyyy";

        [Required(ErrorMessage = "Time zone is required.")]
        [StringLength(60)]
        [Display(Name = "Time Zone")]
        public string TimeZoneId { get; set; } = "India Standard Time";

        [Range(1, 12, ErrorMessage = "Fiscal year start month must be between 1 and 12.")]
        [Display(Name = "Fiscal Year Start Month")]
        public int FiscalYearStartMonth { get; set; } = 4;

        [Required(ErrorMessage = "Lead assignment mode is required.")]
        [Display(Name = "Lead Assignment Mode")]
        public string LeadAssignmentMode { get; set; } = "Manual";

        [Display(Name = "Restrict Lead Export to Admins Only")]
        public bool RestrictLeadExport { get; set; } = true;
    }

    public class UserWorkspaceItemDto
    {
        public Guid TenantId { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string CompanyRole { get; set; } = string.Empty;
        public bool IsCurrent { get; set; }
        public string SubscriptionTier { get; set; } = "FreeTrial";
        public bool IsActive { get; set; } = true;
    }

    public class SwitchTenantViewModel
    {
        public Guid CurrentTenantId { get; set; }
        public string CurrentTenantName { get; set; } = string.Empty;
        public List<UserWorkspaceItemDto> Workspaces { get; set; } = new List<UserWorkspaceItemDto>();
    }
}
