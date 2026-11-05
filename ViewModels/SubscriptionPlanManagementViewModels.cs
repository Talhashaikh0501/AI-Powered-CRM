using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AI_Powered_CRM.ViewModels
{
    public class ManageSubscriptionPlanViewModel
    {
        public Guid? PlanId { get; set; }

        [Required, StringLength(60, MinimumLength = 2)]
        [RegularExpression(@"^[a-zA-Z0-9-]+$", ErrorMessage = "Use letters, numbers, and hyphens only.")]
        public string Code { get; set; } = string.Empty;

        [Required, StringLength(120)]
        public string Name { get; set; } = string.Empty;

        [StringLength(300)]
        public string Tagline { get; set; } = string.Empty;

        [Range(0, 100000000)]
        public decimal MonthlyPriceInr { get; set; }

        [Range(0, 100000000)]
        public decimal YearlyPriceInr { get; set; }

        [Range(0, 365)]
        public int TrialDays { get; set; }

        [Range(1, 100000)]
        public int MaxUserSeats { get; set; }

        [Range(1, 10000000)]
        public int MaxLeadsQuota { get; set; }

        [Range(0, 10000000)]
        public int MonthlyAiRequestLimit { get; set; }

        public bool IsRecommended { get; set; }
        public bool IsActive { get; set; } = true;
        public bool IsPublic { get; set; } = true;
        public int SortOrder { get; set; }
        public string Highlights { get; set; } = string.Empty;
        public string IncludedFeatures { get; set; } = string.Empty;
    }

    public class SubscriptionPlanManagementViewModel
    {
        public List<SubscriptionPlanManagementItem> Plans { get; set; } = new();
    }

    public class SubscriptionPlanManagementItem
    {
        public Guid PlanId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public decimal MonthlyPriceInr { get; set; }
        public decimal YearlyPriceInr { get; set; }
        public int MaxUserSeats { get; set; }
        public int MaxLeadsQuota { get; set; }
        public int MonthlyAiRequestLimit { get; set; }
        public int AssignedCompanies { get; set; }
        public bool IsActive { get; set; }
        public bool IsPublic { get; set; }
        public bool IsRecommended { get; set; }
    }
}