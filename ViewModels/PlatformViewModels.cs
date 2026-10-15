using System;
using System.Collections.Generic;
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

    public class SupportAccessPortalViewModel
    {
        public List<SupportAccessTicket> Tickets { get; set; } = new List<SupportAccessTicket>();
        public List<PlatformTenantSummaryDto> AvailableTenants { get; set; } = new List<PlatformTenantSummaryDto>();
    }

    public class RequestSupportAccessViewModel
    {
        public Guid TenantId { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}
