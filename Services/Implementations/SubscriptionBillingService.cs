using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AI_Powered_CRM.Data;
using AI_Powered_CRM.Models.Entities;
using AI_Powered_CRM.Models.Enums;
using AI_Powered_CRM.Services.Interfaces;
using AI_Powered_CRM.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AI_Powered_CRM.Services.Implementations
{
    public class SubscriptionBillingService : ISubscriptionBillingService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<SubscriptionBillingService> _logger;

        public SubscriptionBillingService(ApplicationDbContext db, ILogger<SubscriptionBillingService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public List<SubscriptionPlanDto> GetAvailablePlans(SubscriptionTier? currentTier = null)
        {
            return _db.SubscriptionPlans.AsNoTracking()
                .Where(p => p.IsActive && p.IsPublic)
                .OrderBy(p => p.SortOrder)
                .ThenBy(p => p.Name)
                .Select(p => new SubscriptionPlanDto
                {
                    PlanId = p.PlanId,
                    Code = p.Code,
                    Name = p.Name,
                    Tagline = p.Tagline,
                    MonthlyPriceInr = p.MonthlyPriceInr,
                    YearlyPriceInr = p.YearlyPriceInr,
                    TrialDays = p.TrialDays,
                    IsFree = p.MonthlyPriceInr == 0 && p.YearlyPriceInr == 0,
                    Tier = p.Code == "starter" ? SubscriptionTier.Starter
                        : p.Code == "professional" ? SubscriptionTier.Professional
                        : p.Code == "enterprise" ? SubscriptionTier.Enterprise
                        : SubscriptionTier.FreeTrial,
                    MaxUserSeats = p.MaxUserSeats,
                    MaxLeadsQuota = p.MaxLeadsQuota,
                    MonthlyAiRequestLimit = p.MonthlyAiRequestLimit,
                    IsRecommended = p.IsRecommended,
                    IsCurrentPlan = currentTier.HasValue &&
                        ((p.Code == "starter" && currentTier == SubscriptionTier.Starter) ||
                         (p.Code == "professional" && currentTier == SubscriptionTier.Professional) ||
                         (p.Code == "enterprise" && currentTier == SubscriptionTier.Enterprise) ||
                         (p.Code == "free-trial" && currentTier == SubscriptionTier.FreeTrial)),
                    Highlights = System.Text.Json.JsonSerializer.Deserialize<List<string>>(p.HighlightsJson) ?? new List<string>(),
                    IncludedFeatures = System.Text.Json.JsonSerializer.Deserialize<List<string>>(p.IncludedFeaturesJson) ?? new List<string>()
                })
                .ToList();
        }

        public async Task<SubscriptionDashboardViewModel?> GetSubscriptionDashboardAsync(Guid tenantId)
        {
            if (tenantId == Guid.Empty) return null;

            var tenant = await _db.Tenants
                .IgnoreQueryFilters()
                .Include(t => t.Settings)
                .FirstOrDefaultAsync(t => t.TenantId == tenantId);

            if (tenant == null) return null;

            var activeSeats = await _db.TenantMemberships
                .IgnoreQueryFilters()
                .Where(m => m.TenantId == tenantId && m.IsActive)
                .CountAsync();

            var leadsCount = await _db.Leads
                .IgnoreQueryFilters()
                .Where(l => l.TenantId == tenantId)
                .CountAsync();

            int? daysLeftInTrial = null;
            if (tenant.TrialEndsAt.HasValue)
            {
                var diff = (tenant.TrialEndsAt.Value.Date - DateTime.UtcNow.Date).Days;
                daysLeftInTrial = Math.Max(0, diff);
            }

            var plans = GetAvailablePlans(tenant.SubscriptionTier);

            return new SubscriptionDashboardViewModel
            {
                TenantId = tenant.TenantId,
                CompanyName = tenant.CompanyName,
                Slug = tenant.Slug,
                Tier = tenant.SubscriptionTier,
                Status = tenant.SubscriptionStatus,
                TrialEndsAt = tenant.TrialEndsAt,
                SubscriptionRenewsAt = tenant.SubscriptionRenewsAt,
                DaysLeftInTrial = daysLeftInTrial,
                ActiveSeats = activeSeats,
                MaxSeats = tenant.MaxUserSeats,
                CurrentLeadsCount = leadsCount,
                MaxLeadsQuota = tenant.MaxLeadsQuota,
                CurrentMonthAiUsage = tenant.CurrentMonthAiUsageCount,
                MonthlyAiRequestLimit = tenant.MonthlyAiRequestLimit,
                CurrencyCode = tenant.Settings?.CurrencyCode ?? tenant.DefaultCurrency ?? "INR",
                CurrencySymbol = tenant.Settings?.CurrencySymbol ?? "₹",
                Plans = plans
            };
        }

        public async Task<BillingHistoryViewModel> GetBillingHistoryAsync(Guid tenantId)
        {
            var tenant = await _db.Tenants
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.TenantId == tenantId);

            var model = new BillingHistoryViewModel
            {
                TenantId = tenantId,
                CompanyName = tenant?.CompanyName ?? "Workspace",
                CurrentTier = tenant?.SubscriptionTier ?? SubscriptionTier.FreeTrial,
                Status = tenant?.SubscriptionStatus ?? SubscriptionStatus.Trialing,
                CurrencySymbol = "₹",
                Filter = "All",
                Invoices = new List<BillingInvoiceItemDto>()
            };

            // If tenant was created in trial, show an honest zero-charge trial onboarding record
            if (tenant != null)
            {
                model.Invoices.Add(new BillingInvoiceItemDto
                {
                    InvoiceNumber = $"INV-{tenant.CreatedAt:yyyyMM}-001",
                    Date = tenant.CreatedAt,
                    Description = $"{tenant.SubscriptionTier} Onboarding Plan (14-Day Complimentary Trial)",
                    AmountInr = 0,
                    CurrencySymbol = "₹",
                    Status = "Completed",
                    PaymentMethod = "Complimentary / Trial",
                    PdfUrl = "#"
                });
            }

            return model;
        }
    }
}
