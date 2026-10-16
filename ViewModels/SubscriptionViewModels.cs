using System;
using System.Collections.Generic;
using AI_Powered_CRM.Models.Enums;

namespace AI_Powered_CRM.ViewModels
{
    public class SubscriptionPlanDto
    {
        public SubscriptionTier Tier { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Tagline { get; set; } = string.Empty;
        public decimal MonthlyPriceInr { get; set; }
        public decimal YearlyPriceInr { get; set; }
        public string MonthlyPriceDisplay => MonthlyPriceInr == 0 ? "\u20B90" : $"\u20B9{MonthlyPriceInr:N0}";
        public string YearlyPriceDisplay => YearlyPriceInr == 0 ? "\u20B90" : $"\u20B9{YearlyPriceInr:N0}";
        public int MaxUserSeats { get; set; }
        public int MaxLeadsQuota { get; set; }
        public int MonthlyAiRequestLimit { get; set; }
        public bool IsRecommended { get; set; }
        public List<string> Highlights { get; set; } = new List<string>();
        public List<string> IncludedFeatures { get; set; } = new List<string>();
        public bool IsCurrentPlan { get; set; }
    }

    public class PricingPageViewModel
    {
        public SubscriptionTier CurrentTier { get; set; } = SubscriptionTier.FreeTrial;
        public SubscriptionStatus CurrentStatus { get; set; } = SubscriptionStatus.Trialing;
        public List<SubscriptionPlanDto> Plans { get; set; } = new List<SubscriptionPlanDto>();
        public bool IsAnnualBilling { get; set; } = false;
        public string CurrencySymbol { get; set; } = "\u20B9";
    }

    public class SubscriptionDashboardViewModel
    {
        public Guid TenantId { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public SubscriptionTier Tier { get; set; } = SubscriptionTier.FreeTrial;
        public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Trialing;
        public DateTime? TrialEndsAt { get; set; }
        public DateTime? SubscriptionRenewsAt { get; set; }
        public int? DaysLeftInTrial { get; set; }
        public bool IsTrialActive => Status == SubscriptionStatus.Trialing && DaysLeftInTrial > 0;

        // Quotas & Usage
        public int ActiveSeats { get; set; }
        public int MaxSeats { get; set; }
        public int SeatPercent => MaxSeats > 0 ? Math.Min(100, (int)((double)ActiveSeats / MaxSeats * 100)) : 100;

        public int CurrentLeadsCount { get; set; }
        public int MaxLeadsQuota { get; set; }
        public int LeadsPercent => MaxLeadsQuota > 0 ? Math.Min(100, (int)((double)CurrentLeadsCount / MaxLeadsQuota * 100)) : 100;

        public int CurrentMonthAiUsage { get; set; }
        public int MonthlyAiRequestLimit { get; set; }
        public int AiPercent => MonthlyAiRequestLimit > 0 ? Math.Min(100, (int)((double)CurrentMonthAiUsage / MonthlyAiRequestLimit * 100)) : 100;

        public string CurrencyCode { get; set; } = "INR";
        public string CurrencySymbol { get; set; } = "\u20B9";

        public List<SubscriptionPlanDto> Plans { get; set; } = new List<SubscriptionPlanDto>();
    }

    public class BillingInvoiceItemDto
    {
        public string InvoiceNumber { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public string Description { get; set; } = string.Empty;
        public decimal AmountInr { get; set; }
        public string CurrencySymbol { get; set; } = "₹";
        public string Status { get; set; } = "Completed";
        public string PaymentMethod { get; set; } = "Complimentary / Trial";
        public string PdfUrl { get; set; } = "#";
    }

    public class BillingHistoryViewModel
    {
        public Guid TenantId { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public SubscriptionTier CurrentTier { get; set; }
        public SubscriptionStatus Status { get; set; }
        public string CurrencySymbol { get; set; } = "₹";
        public List<BillingInvoiceItemDto> Invoices { get; set; } = new List<BillingInvoiceItemDto>();
        public string Filter { get; set; } = "All";
    }
}
