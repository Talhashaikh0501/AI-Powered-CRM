using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using AI_Powered_CRM.Models.Entities;
using AI_Powered_CRM.Models.Enums;

namespace AI_Powered_CRM.ViewModels
{
    public class PlatformDashboardViewModel
    {
        public int TotalTenantsCount { get; set; }
        public int ActiveTenantsCount { get; set; }
        public int TotalPlatformUsersCount { get; set; }
        public int TotalAiCallsThisMonth { get; set; }
        public decimal EstimatedMonthlyMrrInr { get; set; }

        public string? Search { get; set; }
        public SubscriptionTier? FilterTier { get; set; }
        public bool? FilterActive { get; set; }

        public Dictionary<SubscriptionTier, int> TierDistribution { get; set; } = new Dictionary<SubscriptionTier, int>();

        public List<PlatformTenantSummaryDto> Tenants { get; set; } = new List<PlatformTenantSummaryDto>();
    }

    public class PlatformTenantSummaryDto
    {
        public Guid TenantId { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string ContactEmail { get; set; } = string.Empty;
        public SubscriptionTier Tier { get; set; }
        public SubscriptionStatus Status { get; set; }
        public int ActiveSeats { get; set; }
        public int MaxUserSeats { get; set; }
        public int CurrentMonthAiUsageCount { get; set; }
        public int MonthlyAiRequestLimit { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class PlatformTenantDetailsViewModel
    {
        public Guid TenantId { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string ContactEmail { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string DefaultCurrency { get; set; } = "INR";
        public SubscriptionTier Tier { get; set; }
        public SubscriptionStatus Status { get; set; }
        public DateTime? TrialEndsAt { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        // Quotas & Consumption
        public int ActiveSeats { get; set; }
        public int MaxUserSeats { get; set; }
        public int TotalLeadsCount { get; set; }
        public int MaxLeadsQuota { get; set; }
        public int CurrentMonthAiUsageCount { get; set; }
        public int MonthlyAiRequestLimit { get; set; }

        // Workspace settings
        public string CurrencySymbol { get; set; } = "₹";
        public string TimeZoneId { get; set; } = "India Standard Time";
        public string DateFormat { get; set; } = "dd/MM/yyyy";
        public string LeadAssignmentMode { get; set; } = "Manual";
        public bool RestrictLeadExport { get; set; }

        public List<PlatformMemberSummaryDto> Members { get; set; } = new List<PlatformMemberSummaryDto>();
        public List<PlatformAuditSummaryDto> RecentAudits { get; set; } = new List<PlatformAuditSummaryDto>();
    }

    public class PlatformMemberSummaryDto
    {
        public string UserId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public CompanyRole Role { get; set; }
        public bool IsActive { get; set; }
        public DateTime JoinedAt { get; set; }
    }

    public class PlatformAuditSummaryDto
    {
        public string Action { get; set; } = string.Empty;
        public string EntityType { get; set; } = string.Empty;
        public string? Details { get; set; }
        public DateTime CreatedAt { get; set; }
        public string PerformedBy { get; set; } = "System";
    }

    public class UpdateTenantPlanViewModel
    {
        [Required]
        public Guid TenantId { get; set; }

        public string CompanyName { get; set; } = string.Empty;

        [Required]
        public SubscriptionTier SubscriptionTier { get; set; }

        [Required]
        public SubscriptionStatus SubscriptionStatus { get; set; }

        [Range(1, 1000)]
        public int MaxUserSeats { get; set; }

        [Range(10, 500000)]
        public int MaxLeadsQuota { get; set; }

        [Range(0, 100000)]
        public int MonthlyAiRequestLimit { get; set; }

        public string? Reason { get; set; }
    }

    public class ToggleTenantStatusViewModel
    {
        [Required]
        public Guid TenantId { get; set; }

        public string? Reason { get; set; }
    }

    public class SupportAccessPortalViewModel
    {
        public List<SupportAccessTicket> Tickets { get; set; } = new List<SupportAccessTicket>();
        public List<PlatformTenantSummaryDto> AvailableTenants { get; set; } = new List<PlatformTenantSummaryDto>();
    }

    public class RequestSupportAccessViewModel
    {
        [Required]
        public Guid TenantId { get; set; }

        [Required(ErrorMessage = "Reason is required.")]
        [StringLength(500, MinimumLength = 10, ErrorMessage = "Operational reason must be at least 10 characters.")]
        public string Reason { get; set; } = string.Empty;
    }
}
