using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AI_Powered_CRM.Data;
using AI_Powered_CRM.Models;
using AI_Powered_CRM.Models.Entities;
using AI_Powered_CRM.Models.Enums;
using AI_Powered_CRM.Security;
using AI_Powered_CRM.Services.Interfaces;
using AI_Powered_CRM.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AIPoweredCRM.Tests
{
    public class Phase3TenantSwitchingAndSubscriptionTests : IClassFixture<CrmWebApplicationFactory>
    {
        private readonly CrmWebApplicationFactory _factory;

        public Phase3TenantSwitchingAndSubscriptionTests(CrmWebApplicationFactory factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task TC_SWITCH_001_Middleware_ResolvesActiveTenantFromCookie()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tenantContext = new TenantContext();

            var tenantA = Guid.NewGuid();
            var tenantB = Guid.NewGuid();

            await _factory.EnsureTenantExistsAsync(tenantA, "Alpha Corp", "alpha-" + Guid.NewGuid().ToString("N")[..6]);
            await _factory.EnsureTenantExistsAsync(tenantB, "Beta Corp", "beta-" + Guid.NewGuid().ToString("N")[..6]);

            var userId = "user_multi_" + Guid.NewGuid().ToString("N");

            // Grant active membership in both tenants
            db.TenantMemberships.Add(new TenantMembership
            {
                MembershipId = Guid.NewGuid(),
                TenantId = tenantA,
                UserId = userId,
                CompanyRole = CompanyRole.CompanyAdmin,
                IsActive = true
            });
            db.TenantMemberships.Add(new TenantMembership
            {
                MembershipId = Guid.NewGuid(),
                TenantId = tenantB,
                UserId = userId,
                CompanyRole = CompanyRole.Manager,
                IsActive = true
            });
            await db.SaveChangesAsync();

            // Set cookie for Tenant B
            var httpContext = new DefaultHttpContext();
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Name, "multi@test.com")
            };
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
            httpContext.Request.Headers["Cookie"] = $"{TenantResolutionMiddleware.ActiveTenantCookieName}={tenantB}";

            var middleware = new TenantResolutionMiddleware(
                next: (ctx) => Task.CompletedTask,
                logger: NullLogger<TenantResolutionMiddleware>.Instance
            );

            await middleware.InvokeAsync(httpContext, tenantContext, db);

            Assert.True(tenantContext.IsResolved);
            Assert.Equal(tenantB, tenantContext.CurrentTenantId);
        }

        [Fact]
        public async Task TC_SWITCH_002_Middleware_RejectsTamperedCookieAndFallbacksToValidMembership()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tenantContext = new TenantContext();

            var tenantOwned = Guid.NewGuid();
            var tenantAlien = Guid.NewGuid();

            await _factory.EnsureTenantExistsAsync(tenantOwned, "Legit Corp", "legit-" + Guid.NewGuid().ToString("N")[..6]);
            await _factory.EnsureTenantExistsAsync(tenantAlien, "Alien Corp", "alien-" + Guid.NewGuid().ToString("N")[..6]);

            var userId = "user_tamper_" + Guid.NewGuid().ToString("N");

            // Only member of tenantOwned
            db.TenantMemberships.Add(new TenantMembership
            {
                MembershipId = Guid.NewGuid(),
                TenantId = tenantOwned,
                UserId = userId,
                CompanyRole = CompanyRole.SalesExecutive,
                IsActive = true
            });
            await db.SaveChangesAsync();

            // Cookie points to tenantAlien (tampering)
            var httpContext = new DefaultHttpContext();
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Name, "tamper@test.com")
            };
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
            httpContext.Request.Headers["Cookie"] = $"{TenantResolutionMiddleware.ActiveTenantCookieName}={tenantAlien}";

            var middleware = new TenantResolutionMiddleware(
                next: (ctx) => Task.CompletedTask,
                logger: NullLogger<TenantResolutionMiddleware>.Instance
            );

            await middleware.InvokeAsync(httpContext, tenantContext, db);

            // Must NOT resolve to alien tenant, must fallback to owned tenant
            Assert.True(tenantContext.IsResolved);
            Assert.Equal(tenantOwned, tenantContext.CurrentTenantId);
        }

        [Fact]
        public async Task TC_SETTINGS_001_UpdateCompanySettings_PersistsIdentityAndPreferences()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var tenantId = Guid.NewGuid();
            var tenant = new Tenant
            {
                TenantId = tenantId,
                CompanyName = "Initial Name",
                Slug = "initial-slug-" + Guid.NewGuid().ToString("N")[..6],
                ContactEmail = "old@company.com",
                Phone = "+91 1111111111",
                DefaultCurrency = "INR",
                SubscriptionTier = SubscriptionTier.FreeTrial,
                SubscriptionStatus = SubscriptionStatus.Active,
                MaxUserSeats = 5,
                IsActive = true
            };
            db.Tenants.Add(tenant);

            var setting = new TenantSetting
            {
                TenantId = tenantId,
                CurrencyCode = "INR",
                CurrencySymbol = "₹",
                DateFormat = "dd/MM/yyyy",
                TimeZoneId = "India Standard Time",
                FiscalYearStartMonth = 4,
                LeadAssignmentMode = "Manual",
                RestrictLeadExport = true
            };
            db.TenantSettings.Add(setting);
            await db.SaveChangesAsync();

            // Perform settings update
            tenant.CompanyName = "Updated Tech Ltd";
            tenant.ContactEmail = "contact@updatedtech.com";
            tenant.Phone = "+91 9988776655";
            setting.LeadAssignmentMode = "RoundRobin";
            setting.RestrictLeadExport = false;
            setting.DateFormat = "yyyy-MM-dd";
            await db.SaveChangesAsync();

            // Verify persisted
            var reloadedTenant = await db.Tenants.IgnoreQueryFilters().Include(t => t.Settings).FirstOrDefaultAsync(t => t.TenantId == tenantId);
            Assert.NotNull(reloadedTenant);
            Assert.Equal("Updated Tech Ltd", reloadedTenant.CompanyName);
            Assert.Equal("contact@updatedtech.com", reloadedTenant.ContactEmail);
            Assert.Equal("+91 9988776655", reloadedTenant.Phone);

            Assert.NotNull(reloadedTenant.Settings);
            Assert.Equal("RoundRobin", reloadedTenant.Settings.LeadAssignmentMode);
            Assert.False(reloadedTenant.Settings.RestrictLeadExport);
            Assert.Equal("yyyy-MM-dd", reloadedTenant.Settings.DateFormat);
            Assert.Equal("₹", reloadedTenant.Settings.CurrencySymbol);
            Assert.Equal("INR", reloadedTenant.Settings.CurrencyCode);
        }

        [Fact]
        public void TC_BILLING_001_GetAvailablePlans_ReturnsFourPlansWithInrPricing()
        {
            using var scope = _factory.Services.CreateScope();
            var billingService = scope.ServiceProvider.GetRequiredService<ISubscriptionBillingService>();

            var plans = billingService.GetAvailablePlans(SubscriptionTier.Starter);

            Assert.NotNull(plans);
            Assert.Equal(4, plans.Count);

            // 1. Free Trial
            var free = plans.First(p => p.Tier == SubscriptionTier.FreeTrial);
            Assert.Equal(0, free.MonthlyPriceInr);
            Assert.Equal("₹0", free.MonthlyPriceDisplay);
            Assert.Equal(5, free.MaxUserSeats);
            Assert.False(free.IsCurrentPlan);

            // 2. Starter
            var starter = plans.First(p => p.Tier == SubscriptionTier.Starter);
            Assert.Equal(1499, starter.MonthlyPriceInr);
            Assert.Equal("₹1,499", starter.MonthlyPriceDisplay);
            Assert.Equal(10, starter.MaxUserSeats);
            Assert.True(starter.IsCurrentPlan);

            // 3. Professional (Recommended)
            var pro = plans.First(p => p.Tier == SubscriptionTier.Professional);
            Assert.Equal(3999, pro.MonthlyPriceInr);
            Assert.True(pro.IsRecommended);
            Assert.Equal(25, pro.MaxUserSeats);
            Assert.Equal(25000, pro.MaxLeadsQuota);

            // 4. Enterprise
            var ent = plans.First(p => p.Tier == SubscriptionTier.Enterprise);
            Assert.Equal(8999, ent.MonthlyPriceInr);
            Assert.Equal(100, ent.MaxUserSeats);
            Assert.Equal(100000, ent.MaxLeadsQuota);
        }

        [Fact]
        public async Task TC_BILLING_002_GetSubscriptionDashboard_CalculatesUsagePercentagesCorrectly()
        {
            using var scope = _factory.Services.CreateScope();
            var billingService = scope.ServiceProvider.GetRequiredService<ISubscriptionBillingService>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var tenantId = Guid.NewGuid();
            db.IgnoreTenantFilter = true;
            var tenant = new Tenant
            {
                TenantId = tenantId,
                CompanyName = "Dashboard Test Corp",
                Slug = "dash-test-" + Guid.NewGuid().ToString("N")[..6],
                ContactEmail = "dash@test.com",
                DefaultCurrency = "INR",
                SubscriptionTier = SubscriptionTier.Professional,
                SubscriptionStatus = SubscriptionStatus.Trialing,
                TrialEndsAt = DateTime.UtcNow.AddDays(10),
                MaxUserSeats = 20,
                MaxLeadsQuota = 10000,
                MonthlyAiRequestLimit = 5000,
                CurrentMonthAiUsageCount = 2500, // 50%
                IsActive = true
            };
            db.Tenants.Add(tenant);

            // Add 10 active members (50% seat quota)
            for (int i = 0; i < 10; i++)
            {
                db.TenantMemberships.Add(new TenantMembership
                {
                    MembershipId = Guid.NewGuid(),
                    TenantId = tenantId,
                    UserId = $"seat_user_{i}_" + Guid.NewGuid().ToString("N"),
                    CompanyRole = CompanyRole.SalesExecutive,
                    IsActive = true
                });
            }

            // Add 5 leads
            for (int i = 0; i < 5; i++)
            {
                db.Leads.Add(new Lead
                {
                    TenantId = tenantId,
                    Name = "Lead " + i,
                    Phone = "+91 9876543210",
                    Email = $"lead{i}@test.com",
                    InterestedProduct = "Cloud CRM",
                    Status = LeadStatus.New,
                    Priority = LeadPriority.Medium,
                    Source = "Website"
                });
            }
            await db.SaveChangesAsync();

            var dashboard = await billingService.GetSubscriptionDashboardAsync(tenantId);

            Assert.NotNull(dashboard);
            Assert.Equal("Dashboard Test Corp", dashboard.CompanyName);
            Assert.Equal(SubscriptionTier.Professional, dashboard.Tier);
            Assert.True(dashboard.IsTrialActive);
            Assert.NotNull(dashboard.DaysLeftInTrial);
            Assert.True(dashboard.DaysLeftInTrial >= 9 && dashboard.DaysLeftInTrial <= 10);

            // Quotas
            Assert.Equal(10, dashboard.ActiveSeats);
            Assert.Equal(20, dashboard.MaxSeats);
            Assert.Equal(50, dashboard.SeatPercent);

            Assert.Equal(5, dashboard.CurrentLeadsCount);
            Assert.Equal(10000, dashboard.MaxLeadsQuota);

            Assert.Equal(2500, dashboard.CurrentMonthAiUsage);
            Assert.Equal(5000, dashboard.MonthlyAiRequestLimit);
            Assert.Equal(50, dashboard.AiPercent);
        }

        [Fact]
        public async Task TC_BILLING_003_GetBillingHistory_ReturnsTrialOnboardingReceipt()
        {
            using var scope = _factory.Services.CreateScope();
            var billingService = scope.ServiceProvider.GetRequiredService<ISubscriptionBillingService>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var tenantId = Guid.NewGuid();
            var tenant = new Tenant
            {
                TenantId = tenantId,
                CompanyName = "Invoice Test Corp",
                Slug = "inv-test-" + Guid.NewGuid().ToString("N")[..6],
                ContactEmail = "invoice@test.com",
                DefaultCurrency = "INR",
                SubscriptionTier = SubscriptionTier.FreeTrial,
                SubscriptionStatus = SubscriptionStatus.Trialing,
                MaxUserSeats = 5,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();

            var history = await billingService.GetBillingHistoryAsync(tenantId);

            Assert.NotNull(history);
            Assert.Equal("Invoice Test Corp", history.CompanyName);
            Assert.Single(history.Invoices);

            var invoice = history.Invoices.First();
            Assert.Contains("INV-", invoice.InvoiceNumber);
            Assert.Equal(0, invoice.AmountInr);
            Assert.Equal("Completed", invoice.Status);
            Assert.Equal("Complimentary / Trial", invoice.PaymentMethod);
        }
    }
}
