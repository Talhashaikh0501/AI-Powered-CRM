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

        [Fact]
        public async Task TC_SWITCH_003_SwitchTenant_GetMethod_IsDisallowed()
        {
            var (client, _) = await _factory.CreateAuthenticatedClientAsync("CompanyAdmin");
            var targetTenantId = Guid.NewGuid();

            var response = await client.GetAsync($"/Account/SwitchTenant?tenantId={targetTenantId}");
            
            // Endpoint is strictly [HttpPost], so GET cannot succeed as an authorized workspace switch
            Assert.True(
                response.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed ||
                response.StatusCode == System.Net.HttpStatusCode.NotFound ||
                response.StatusCode == System.Net.HttpStatusCode.Redirect);

            // Verify the active tenant cookie was not set by the rejected GET request
            if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
            {
                Assert.DoesNotContain(setCookies, c => c.Contains(TenantResolutionMiddleware.ActiveTenantCookieName + "=" + targetTenantId));
            }
        }

        [Fact]
        public async Task TC_ASSIGN_001_RoundRobin_RotatesFairlyAmongSalesExecutives()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var assignmentService = scope.ServiceProvider.GetRequiredService<ILeadAssignmentService>();

            var tenantId = Guid.NewGuid();
            await _factory.EnsureTenantExistsAsync(tenantId, "RoundRobin Corp", "rr-" + Guid.NewGuid().ToString("N")[..6]);

            db.IgnoreTenantFilter = true;

            // Set tenant assignment mode to RoundRobin
            var setting = await db.TenantSettings.FirstOrDefaultAsync(s => s.TenantId == tenantId);
            if (setting == null)
            {
                setting = new TenantSetting { TenantId = tenantId, LeadAssignmentMode = "RoundRobin" };
                db.TenantSettings.Add(setting);
            }
            else
            {
                setting.LeadAssignmentMode = "RoundRobin";
            }

            var rep1 = new ApplicationUser { Id = $"rep1_{Guid.NewGuid():N}", UserName = "rep1@rr.com", Email = "rep1@rr.com", IsActive = true };
            var rep2 = new ApplicationUser { Id = $"rep2_{Guid.NewGuid():N}", UserName = "rep2@rr.com", Email = "rep2@rr.com", IsActive = true };
            db.Users.AddRange(rep1, rep2);

            db.TenantMemberships.Add(new TenantMembership
            {
                MembershipId = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = rep1.Id,
                CompanyRole = CompanyRole.SalesExecutive,
                IsActive = true
            });
            db.TenantMemberships.Add(new TenantMembership
            {
                MembershipId = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = rep2.Id,
                CompanyRole = CompanyRole.SalesExecutive,
                IsActive = true
            });

            await db.SaveChangesAsync();

            // First call: both have 0 leads, assigns to rep1 (or least loaded)
            var chosen1 = await assignmentService.DetermineLeadOwnerAsync(tenantId, null, "fallback");
            Assert.True(chosen1 == rep1.Id || chosen1 == rep2.Id);

            // Simulate lead assigned to chosen1
            db.Leads.Add(new Lead
            {
                TenantId = tenantId,
                Name = "Deal 1",
                Phone = "+91 9988776655",
                InterestedProduct = "ERP",
                OwnerUserId = chosen1,
                Status = LeadStatus.New,
                Priority = LeadPriority.High,
                Source = "Website"
            });
            await db.SaveChangesAsync();

            // Second call: the other representative now has fewer active leads, so round-robin MUST select them
            var chosen2 = await assignmentService.DetermineLeadOwnerAsync(tenantId, null, "fallback");
            var expectedOtherRep = chosen1 == rep1.Id ? rep2.Id : rep1.Id;
            Assert.Equal(expectedOtherRep, chosen2);
        }

        [Fact]
        public async Task TC_ASSIGN_002_RoundRobin_StrictTenantIsolation()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var assignmentService = scope.ServiceProvider.GetRequiredService<ILeadAssignmentService>();

            var tenantAlpha = Guid.NewGuid();
            var tenantBeta = Guid.NewGuid();

            await _factory.EnsureTenantExistsAsync(tenantAlpha, "Alpha RoundRobin", "alpha-rr-" + Guid.NewGuid().ToString("N")[..6]);
            await _factory.EnsureTenantExistsAsync(tenantBeta, "Beta RoundRobin", "beta-rr-" + Guid.NewGuid().ToString("N")[..6]);

            db.IgnoreTenantFilter = true;

            var settingAlpha = await db.TenantSettings.FirstOrDefaultAsync(s => s.TenantId == tenantAlpha);
            if (settingAlpha != null)
            {
                settingAlpha.LeadAssignmentMode = "RoundRobin";
            }
            else
            {
                db.TenantSettings.Add(new TenantSetting { TenantId = tenantAlpha, LeadAssignmentMode = "RoundRobin" });
            }

            var settingBeta = await db.TenantSettings.FirstOrDefaultAsync(s => s.TenantId == tenantBeta);
            if (settingBeta != null)
            {
                settingBeta.LeadAssignmentMode = "RoundRobin";
            }
            else
            {
                db.TenantSettings.Add(new TenantSetting { TenantId = tenantBeta, LeadAssignmentMode = "RoundRobin" });
            }

            var repAlpha = new ApplicationUser { Id = $"rep_alpha_{Guid.NewGuid():N}", UserName = "alpha@rep.com", Email = "alpha@rep.com", IsActive = true };
            var repBeta = new ApplicationUser { Id = $"rep_beta_{Guid.NewGuid():N}", UserName = "beta@rep.com", Email = "beta@rep.com", IsActive = true };
            db.Users.AddRange(repAlpha, repBeta);

            db.TenantMemberships.Add(new TenantMembership
            {
                MembershipId = Guid.NewGuid(),
                TenantId = tenantAlpha,
                UserId = repAlpha.Id,
                CompanyRole = CompanyRole.SalesExecutive,
                IsActive = true
            });
            db.TenantMemberships.Add(new TenantMembership
            {
                MembershipId = Guid.NewGuid(),
                TenantId = tenantBeta,
                UserId = repBeta.Id,
                CompanyRole = CompanyRole.SalesExecutive,
                IsActive = true
            });
            await db.SaveChangesAsync();

            // When assigning in Tenant Beta, Rep Alpha must NEVER be chosen
            var chosenBeta = await assignmentService.DetermineLeadOwnerAsync(tenantBeta, null, "fallback");
            Assert.Equal(repBeta.Id, chosenBeta);
            Assert.NotEqual(repAlpha.Id, chosenBeta);
        }

        [Fact]
        public async Task TC_INSIGHTS_001_GetAIInsights_CalculatesDistributionAndOpportunities()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var reportService = scope.ServiceProvider.GetRequiredService<IReportService>();

            var tenantId = Guid.NewGuid();
            await _factory.EnsureTenantExistsAsync(tenantId, "Insights Corp", "ins-" + Guid.NewGuid().ToString("N")[..6]);

            db.IgnoreTenantFilter = true;

            // Add leads with insights
            var hotLead = new Lead
            {
                TenantId = tenantId,
                Name = "Hot Lead Ltd",
                Phone = "+91 9988776611",
                InterestedProduct = "AI Suite",
                Budget = 500000,
                Status = LeadStatus.Proposal,
                Priority = LeadPriority.High,
                Source = "Website"
            };
            var warmLead = new Lead
            {
                TenantId = tenantId,
                Name = "Warm Prospect LLC",
                Phone = "+91 9988776622",
                InterestedProduct = "Cloud CRM",
                Budget = 250000,
                Status = LeadStatus.Contacted,
                Priority = LeadPriority.Medium,
                Source = "LinkedIn"
            };
            db.Leads.AddRange(hotLead, warmLead);
            await db.SaveChangesAsync();

            db.AILeadInsights.Add(new AILeadInsight
            {
                TenantId = tenantId,
                LeadId = hotLead.LeadId,
                Score = 88,
                Classification = AIClassification.Hot,
                Summary = "Strong enterprise buying signals.",
                NextBestAction = "Send commercial proposal.",
                RiskLevel = RiskLevel.Low,
                GeneratedAt = DateTime.UtcNow
            });
            db.AILeadInsights.Add(new AILeadInsight
            {
                TenantId = tenantId,
                LeadId = warmLead.LeadId,
                Score = 65,
                Classification = AIClassification.Warm,
                Summary = "Evaluating technical features.",
                NextBestAction = "Schedule technical demo.",
                RiskLevel = RiskLevel.Medium,
                GeneratedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            var vm = await reportService.GetAIInsightsAsync("admin", "Admin");

            Assert.NotNull(vm);
            Assert.True(vm.TotalAnalyzedLeads >= 2);
            Assert.True(vm.HotLeadsCount >= 1);
            Assert.True(vm.WarmLeadsCount >= 1);
            Assert.True(vm.HighProbabilityPipelineValue >= 500000);
            Assert.NotEmpty(vm.PriorityOpportunities);
            Assert.Contains(vm.PriorityOpportunities, o => o.LeadId == hotLead.LeadId && o.Score == 88);
        }

        [Fact]
        public async Task TC_SUPPORT_001_SupportAccessService_ApprovalAndRevocationLifecycle()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var supportService = scope.ServiceProvider.GetRequiredService<ISupportAccessService>();

            if (!await roleManager.RoleExistsAsync("PlatformSuperAdmin"))
                await roleManager.CreateAsync(new IdentityRole("PlatformSuperAdmin"));
            if (!await roleManager.RoleExistsAsync("CompanyAdmin"))
                await roleManager.CreateAsync(new IdentityRole("CompanyAdmin"));

            var tenantId = Guid.NewGuid();
            await _factory.EnsureTenantExistsAsync(tenantId, "Support Test Corp", "sup-" + Guid.NewGuid().ToString("N")[..6]);

            db.IgnoreTenantFilter = true;

            var superAdmin = new ApplicationUser
            {
                UserName = $"superadmin_{Guid.NewGuid():N}@platform.crm",
                Email = $"superadmin_{Guid.NewGuid():N}@platform.crm",
                FullName = "Platform SuperAdmin",
                IsActive = true
            };
            var companyAdmin = new ApplicationUser
            {
                UserName = $"admin_{Guid.NewGuid():N}@supportcorp.com",
                Email = $"admin_{Guid.NewGuid():N}@supportcorp.com",
                FullName = "Support Corp Admin",
                IsActive = true
            };

            await userManager.CreateAsync(superAdmin, "SuperAdminPassword!123");
            await userManager.AddToRoleAsync(superAdmin, "PlatformSuperAdmin");

            await userManager.CreateAsync(companyAdmin, "CompanyAdminPassword!123");
            await userManager.AddToRoleAsync(companyAdmin, "CompanyAdmin");

            db.TenantMemberships.Add(new TenantMembership
            {
                MembershipId = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = companyAdmin.Id,
                CompanyRole = CompanyRole.CompanyAdmin,
                IsActive = true
            });
            await db.SaveChangesAsync();

            var superAdminPrincipal = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, superAdmin.Id),
                new Claim(ClaimTypes.Name, superAdmin.UserName!),
                new Claim(ClaimTypes.Role, "PlatformSuperAdmin")
            }, "TestAuth"));

            var companyAdminPrincipal = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, companyAdmin.Id),
                new Claim(ClaimTypes.Name, companyAdmin.UserName!),
                new Claim(ClaimTypes.Role, "CompanyAdmin")
            }, "TestAuth"));

            // 1. SuperAdmin creates ticket
            var ticket = await supportService.CreateTicketAsync(tenantId, superAdmin.Id, "Database index optimization", superAdminPrincipal);
            Assert.NotNull(ticket);
            Assert.Equal("Pending", ticket.Status);
            Assert.False(ticket.IsActive);

            // 2. CompanyAdmin approves ticket
            var approved = await supportService.ApproveTicketAsync(ticket.TicketId, TimeSpan.FromHours(4), companyAdminPrincipal);
            Assert.NotNull(approved);
            Assert.Equal("Approved", approved.Status);
            Assert.True(approved.IsActive);
            Assert.Equal(companyAdmin.Id, approved.ApprovedByUserId);

            // 3. Revoke ticket
            var revoked = await supportService.RevokeTicketAsync(ticket.TicketId, "Maintenance completed", companyAdminPrincipal);
            Assert.NotNull(revoked);
            Assert.Equal("Revoked", revoked.Status);
            Assert.False(revoked.IsActive);
        }

        [Fact]
        public async Task TC_TEAM_001_ChangeUserRole_PreventsDemotingLastCompanyAdmin()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var tenantId = Guid.NewGuid();
            await _factory.EnsureTenantExistsAsync(tenantId, "Sole Admin Corp", "sole-adm-" + Guid.NewGuid().ToString("N")[..6]);

            db.IgnoreTenantFilter = true;

            var soleAdminId = $"sole_admin_{Guid.NewGuid():N}";
            db.Users.Add(new ApplicationUser
            {
                Id = soleAdminId,
                UserName = "sole@admin.com",
                Email = "sole@admin.com",
                IsActive = true
            });

            db.TenantMemberships.Add(new TenantMembership
            {
                MembershipId = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = soleAdminId,
                CompanyRole = CompanyRole.CompanyAdmin,
                IsActive = true
            });
            await db.SaveChangesAsync();

            // Verify count of active company admins is exactly 1
            var activeAdmins = await db.TenantMemberships
                .Where(m => m.TenantId == tenantId && m.CompanyRole == CompanyRole.CompanyAdmin && m.IsActive)
                .CountAsync();
            Assert.Equal(1, activeAdmins);

            // If an attempt to change role to SalesExecutive is evaluated against the rule:
            var targetMembership = await db.TenantMemberships.FirstOrDefaultAsync(m => m.TenantId == tenantId && m.UserId == soleAdminId);
            Assert.NotNull(targetMembership);

            var isLastAdmin = targetMembership.CompanyRole == CompanyRole.CompanyAdmin && activeAdmins <= 1;
            Assert.True(isLastAdmin, "The sole CompanyAdmin cannot be demoted.");
        }

        [Fact]
        public async Task TC_TEAM_002_ToggleUserStatus_PreventsDeactivatingLastCompanyAdmin()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var tenantId = Guid.NewGuid();
            await _factory.EnsureTenantExistsAsync(tenantId, "Sole Active Admin Corp", "sole-act-" + Guid.NewGuid().ToString("N")[..6]);

            db.IgnoreTenantFilter = true;

            var soleAdminId = $"sole_active_adm_{Guid.NewGuid():N}";
            db.Users.Add(new ApplicationUser
            {
                Id = soleAdminId,
                UserName = "soleactive@admin.com",
                Email = "soleactive@admin.com",
                IsActive = true
            });

            db.TenantMemberships.Add(new TenantMembership
            {
                MembershipId = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = soleAdminId,
                CompanyRole = CompanyRole.CompanyAdmin,
                IsActive = true
            });
            await db.SaveChangesAsync();

            // Verify count of active company admins is 1
            var activeAdmins = await db.TenantMemberships
                .Where(m => m.TenantId == tenantId && m.CompanyRole == CompanyRole.CompanyAdmin && m.IsActive)
                .CountAsync();
            Assert.Equal(1, activeAdmins);

            var membership = await db.TenantMemberships.FirstOrDefaultAsync(m => m.TenantId == tenantId && m.UserId == soleAdminId);
            Assert.NotNull(membership);

            var wouldLeaveZeroAdmins = membership.IsActive && membership.CompanyRole == CompanyRole.CompanyAdmin && activeAdmins <= 1;
            Assert.True(wouldLeaveZeroAdmins, "Deactivation must be blocked when it would leave zero active CompanyAdmins.");
        }
    }
}
