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
            return new List<SubscriptionPlanDto>
            {
                new SubscriptionPlanDto
                {
                    Tier = SubscriptionTier.FreeTrial,
                    Name = "Free Trial",
                    Tagline = "14-day enterprise test drive with full AI access",
                    MonthlyPriceInr = 0,
                    YearlyPriceInr = 0,
                    MaxUserSeats = 5,
                    MaxLeadsQuota = 1000,
                    MonthlyAiRequestLimit = 200,
                    IsRecommended = false,
                    IsCurrentPlan = currentTier == SubscriptionTier.FreeTrial,
                    Highlights = new List<string>
                    {
                        "14-Day Free Access",
                        "5 User Seats Included",
                        "1,000 Leads Storage",
                        "200 AI Scoring Credits"
                    },
                    IncludedFeatures = new List<string>
                    {
                        "5 Team Members",
                        "1,000 Lead Records",
                        "200 Groq AI Predictions / month",
                        "Activity Logging & Notes",
                        "Follow-up Task Reminders",
                        "INR (₹) Default Currency"
                    }
                },
                new SubscriptionPlanDto
                {
                    Tier = SubscriptionTier.Starter,
                    Name = "Starter",
                    Tagline = "Ideal for growing sales teams and agile startups",
                    MonthlyPriceInr = 1499,
                    YearlyPriceInr = 14390,
                    MaxUserSeats = 10,
                    MaxLeadsQuota = 5000,
                    MonthlyAiRequestLimit = 1000,
                    IsRecommended = false,
                    IsCurrentPlan = currentTier == SubscriptionTier.Starter,
                    Highlights = new List<string>
                    {
                        "Up to 10 Seats",
                        "5,000 Leads Quota",
                        "1,000 Monthly AI Predictions",
                        "Standard Email Support"
                    },
                    IncludedFeatures = new List<string>
                    {
                        "10 Team Members",
                        "5,000 Lead Records",
                        "1,000 Groq AI Predictions / month",
                        "Role Permissions (Admin, Manager, Rep)",
                        "Conversion Pipeline Reports",
                        "Automated Follow-up Queues",
                        "Standard Email Support (24h SLA)"
                    }
                },
                new SubscriptionPlanDto
                {
                    Tier = SubscriptionTier.Professional,
                    Name = "Professional",
                    Tagline = "High-velocity sales intelligence for scaling organizations",
                    MonthlyPriceInr = 3999,
                    YearlyPriceInr = 38390,
                    MaxUserSeats = 25,
                    MaxLeadsQuota = 25000,
                    MonthlyAiRequestLimit = 5000,
                    IsRecommended = true,
                    IsCurrentPlan = currentTier == SubscriptionTier.Professional,
                    Highlights = new List<string>
                    {
                        "Up to 25 Seats",
                        "25,000 Leads Quota",
                        "5,000 Monthly AI Predictions",
                        "Priority Audit Logging & Export Controls",
                        "Priority Response Support"
                    },
                    IncludedFeatures = new List<string>
                    {
                        "25 Team Members",
                        "25,000 Lead Records",
                        "5,000 Groq AI Predictions / month",
                        "Real-Time AI Lead Scoring & Win Probability",
                        "Advanced Conversion Analytics & Trends",
                        "Comprehensive Security Audit Logs",
                        "Export Controls & Anti-Leakage",
                        "Priority Email & Chat Support"
                    }
                },
                new SubscriptionPlanDto
                {
                    Tier = SubscriptionTier.Enterprise,
                    Name = "Enterprise",
                    Tagline = "Maximum security, custom volume, and dedicated SLA for large teams",
                    MonthlyPriceInr = 8999,
                    YearlyPriceInr = 86390,
                    MaxUserSeats = 100,
                    MaxLeadsQuota = 100000,
                    MonthlyAiRequestLimit = 25000,
                    IsRecommended = false,
                    IsCurrentPlan = currentTier == SubscriptionTier.Enterprise,
                    Highlights = new List<string>
                    {
                        "100+ Custom Seats",
                        "100,000+ Leads Quota",
                        "25,000 Monthly AI Predictions",
                        "Delegated Support Access Ticketing",
                        "24/7 Dedicated SLA"
                    },
                    IncludedFeatures = new List<string>
                    {
                        "100+ Team Members",
                        "100,000+ Lead Records",
                        "25,000 Groq AI Predictions / month",
                        "Anti-Self-Approval Delegated Support Access",
                        "Immutable Audit Log Retention",
                        "Custom Lead Assignment Rules",
                        "Dedicated Account Manager",
                        "24/7 Priority SLA & Escalation"
                    }
                }
            };
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
