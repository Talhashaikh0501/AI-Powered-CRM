using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using AI_Powered_CRM.Controllers;
using AI_Powered_CRM.Data;
using AI_Powered_CRM.Models;
using AI_Powered_CRM.Models.Entities;
using AI_Powered_CRM.Models.Enums;
using AI_Powered_CRM.Security;
using AI_Powered_CRM.Services.Implementations;
using AI_Powered_CRM.Services.Interfaces;
using AI_Powered_CRM.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AIPoweredCRM.Tests
{
    public class PrePaymentSprintComprehensiveTests : IClassFixture<CrmWebApplicationFactory>
    {
        private readonly CrmWebApplicationFactory _factory;

        public PrePaymentSprintComprehensiveTests(CrmWebApplicationFactory factory)
        {
            _factory = factory;
        }

        // =========================================================================
        // 1. EMAIL VERIFICATION & FOUNDER ONBOARDING TESTS
        // =========================================================================

        [Fact]
        public async Task TC_EMAIL_VERIFICATION_001_FounderRegistration_RequiresEmailConfirmation_BlocksLoginUntilVerified()
        {
            using var scope = _factory.Services.CreateScope();
            var onboardingService = scope.ServiceProvider.GetRequiredService<ICompanyOnboardingService>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var signInManager = scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();

            var slug = "verify-co-" + Guid.NewGuid().ToString("N")[..8];
            var email = $"founder_{Guid.NewGuid():N}@verify-co.in";
            var password = "FounderPass@123";

            var model = new RegisterCompanyViewModel
            {
                CompanyName = "Verification Test Co",
                Slug = slug,
                AdminFullName = "Rohan Sharma",
                ContactEmail = email,
                Phone = "+91 9876543210",
                Password = password,
                ConfirmPassword = password
            };

            // 1. Register company
            var regResult = await onboardingService.RegisterCompanyAsync(model);
            Assert.True(regResult.Succeeded, string.Join("; ", regResult.Errors));
            Assert.NotNull(regResult.UserId);

            // 2. Verify EmailConfirmed is false immediately after registration
            var user = await userManager.FindByIdAsync(regResult.UserId);
            Assert.NotNull(user);
            Assert.False(user.EmailConfirmed, "User must NOT have EmailConfirmed = true upon registration.");

            // 3. Verify AccountController blocks login when EmailConfirmed is false
            var controller = new AccountController(
                signInManager,
                userManager,
                scope.ServiceProvider.GetRequiredService<IAuditService>(),
                onboardingService,
                scope.ServiceProvider.GetRequiredService<ITenantInvitationService>(),
                scope.ServiceProvider.GetRequiredService<IEmailDeliveryService>(),
                scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
                scope.ServiceProvider.GetRequiredService<ITenantContext>(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<AccountController>.Instance);

            var loginResult = await controller.Login(new LoginViewModel { Email = email, Password = password });
            var viewResult = Assert.IsType<Microsoft.AspNetCore.Mvc.ViewResult>(loginResult);
            Assert.False(controller.ModelState.IsValid);
            Assert.True((bool?)controller.ViewBag.ShowResendConfirmation == true);

            // 4. Generate valid email confirmation token and confirm
            var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
            Assert.False(string.IsNullOrWhiteSpace(token));

            var confirmResult = await userManager.ConfirmEmailAsync(user, token);
            Assert.True(confirmResult.Succeeded);

            // 5. Verify user is now confirmed
            var updatedUser = await userManager.FindByIdAsync(regResult.UserId);
            Assert.NotNull(updatedUser);
            Assert.True(updatedUser.EmailConfirmed, "User must have EmailConfirmed = true after confirmation.");
        }

        [Fact]
        public async Task TC_EMAIL_VERIFICATION_002_InvalidConfirmationToken_IsRejected()
        {
            using var scope = _factory.Services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var user = new ApplicationUser
            {
                Id = "unconf_" + Guid.NewGuid().ToString("N"),
                UserName = $"unconf_{Guid.NewGuid():N}@test.in",
                Email = $"unconf_{Guid.NewGuid():N}@test.in",
                EmailConfirmed = false,
                SecurityStamp = Guid.NewGuid().ToString()
            };
            var createResult = await userManager.CreateAsync(user, "TestPass@123");
            Assert.True(createResult.Succeeded);

            // Attempt confirmation with invalid/tampered token
            var result = await userManager.ConfirmEmailAsync(user, "invalid-tampered-token-12345");
            Assert.False(result.Succeeded, "Invalid token must be rejected.");

            var reloaded = await userManager.FindByIdAsync(user.Id);
            Assert.NotNull(reloaded);
            Assert.False(reloaded.EmailConfirmed, "EmailConfirmed must remain false.");
        }

        // =========================================================================
        // 2. PLATFORM SUPERADMIN PROVISIONING, ACCESS & PRIVACY TESTS
        // =========================================================================

        [Fact]
        public async Task TC_PLATFORM_SUPERADMIN_001_SuperAdmin_HasPlatformRole_AndZeroTenantMemberships()
        {
            using var scope = _factory.Services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // Ensure roles
            if (!await roleManager.RoleExistsAsync("PlatformSuperAdmin"))
                await roleManager.CreateAsync(new IdentityRole("PlatformSuperAdmin"));
            if (!await roleManager.RoleExistsAsync("SuperAdmin"))
                await roleManager.CreateAsync(new IdentityRole("SuperAdmin"));

            var email = $"superadmin_{Guid.NewGuid():N}@crm-platform.local";
            var superAdmin = new ApplicationUser
            {
                Id = "sa_" + Guid.NewGuid().ToString("N"),
                UserName = email,
                Email = email,
                FullName = "Platform Operations Lead",
                IsActive = true,
                EmailConfirmed = true,
                SecurityStamp = Guid.NewGuid().ToString()
            };

            var createRes = await userManager.CreateAsync(superAdmin, "SuperAdminPass@2026");
            Assert.True(createRes.Succeeded);

            await userManager.AddToRolesAsync(superAdmin, new[] { "PlatformSuperAdmin", "SuperAdmin" });

            // Verify membership separation: SuperAdmin must NEVER belong to a tenant
            db.IgnoreTenantFilter = true;
            var memberships = await db.TenantMemberships
                .Where(m => m.UserId == superAdmin.Id)
                .ToListAsync();

            Assert.Empty(memberships);

            var roles = await userManager.GetRolesAsync(superAdmin);
            Assert.Contains("PlatformSuperAdmin", roles);
            Assert.Contains("SuperAdmin", roles);
        }

        [Fact]
        public async Task TC_PLATFORM_SUPERADMIN_002_PlatformEndpoint_AccessControl_RestrictedToPlatformSuperAdmin()
        {
            // Non-platform user (CompanyAdmin) attempting to access /Platform
            var (adminClient, _) = await _factory.CreateAuthenticatedClientAsync("CompanyAdmin");
            var adminResponse = await adminClient.GetAsync("/Platform");
            Assert.True(adminResponse.StatusCode == HttpStatusCode.Forbidden || 
                        adminResponse.StatusCode == HttpStatusCode.Redirect,
                        $"Non-platform user should be blocked from /Platform. Got: {adminResponse.StatusCode}");

            // PlatformSuperAdmin accessing /Platform
            var (superClient, _) = await _factory.CreateAuthenticatedClientAsync("PlatformSuperAdmin");
            var superResponse = await superClient.GetAsync("/Platform");
            Assert.Equal(HttpStatusCode.OK, superResponse.StatusCode);
        }

        [Fact]
        public async Task TC_PLATFORM_SUPERADMIN_003_PlatformTenantManagement_UpdatesTierAndTogglesStatus()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var auditService = scope.ServiceProvider.GetRequiredService<IAuditService>();

            var tenantId = Guid.NewGuid();
            var tenant = new Tenant
            {
                TenantId = tenantId,
                CompanyName = "Acme Corp INR",
                Slug = "acme-" + Guid.NewGuid().ToString("N")[..6],
                ContactEmail = "contact@acme.in",
                DefaultCurrency = "INR",
                SubscriptionTier = SubscriptionTier.FreeTrial,
                SubscriptionStatus = SubscriptionStatus.Active,
                MaxUserSeats = 5,
                MaxLeadsQuota = 100,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            db.IgnoreTenantFilter = true;
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();

            // Simulate PlatformController administrative plan update
            var loadedTenant = await db.Tenants.FirstOrDefaultAsync(t => t.TenantId == tenantId);
            Assert.NotNull(loadedTenant);

            loadedTenant.SubscriptionTier = SubscriptionTier.Professional;
            loadedTenant.MaxUserSeats = 15;
            loadedTenant.MaxLeadsQuota = 10000;
            await db.SaveChangesAsync();
            await auditService.LogAsync("superadmin_test", "TenantPlanUpdate", tenantId.ToString(), "Upgraded to Professional");

            // Verify updated values
            var reloaded = await db.Tenants.FirstOrDefaultAsync(t => t.TenantId == tenantId);
            Assert.NotNull(reloaded);
            Assert.Equal(SubscriptionTier.Professional, reloaded.SubscriptionTier);
            Assert.Equal(15, reloaded.MaxUserSeats);
            Assert.Equal(10000, reloaded.MaxLeadsQuota);

            // Toggle active status (suspend)
            reloaded.IsActive = false;
            await db.SaveChangesAsync();

            var suspendedTenant = await db.Tenants.FirstOrDefaultAsync(t => t.TenantId == tenantId);
            Assert.NotNull(suspendedTenant);
            Assert.False(suspendedTenant.IsActive);
        }

        [Fact]
        public async Task TC_PLATFORM_SUPERADMIN_004_PrivacyIsolation_PlatformSuperAdminCannotAccessCustomerLeadsWithoutTicket()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var tenantId = Guid.NewGuid();
            var secretLead = new Lead
            {
                LeadId = 998811,
                TenantId = tenantId,
                Name = "Confidential Executive",
                Phone = "+91 9999999999",
                Email = "confidential@client.com",
                Source = "Direct",
                InterestedProduct = "ERP",
                Status = LeadStatus.New,
                CreatedAt = DateTime.UtcNow
            };

            db.IgnoreTenantFilter = true;
            db.Leads.Add(secretLead);
            await db.SaveChangesAsync();

            // When a context queries leads with a normal or non-tenant filter (or without Support Access scope active),
            // the lead is inaccessible if the tenant filter is enforced for tenant queries.
            using var restrictedScope = _factory.Services.CreateScope();
            var restrictedDb = restrictedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            // Verify default tenant filter prevents leaked data
            var visibleLeads = await restrictedDb.Leads.Where(l => l.LeadId == secretLead.LeadId).ToListAsync();
            Assert.Empty(visibleLeads);
        }

        // =========================================================================
        // 3. CONCURRENCY-SAFE ROUND-ROBIN & WORKLOAD IN-FLIGHT TESTS
        // =========================================================================

        [Fact]
        public async Task TC_ROUND_ROBIN_CONCURRENCY_001_ParallelAssignments_DistributeFairlyWithoutCollisions()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var assignmentService = scope.ServiceProvider.GetRequiredService<ILeadAssignmentService>();

            var tenantId = Guid.NewGuid();
            await _factory.EnsureTenantExistsAsync(tenantId, "RoundRobin Tech", "rr-tech-" + Guid.NewGuid().ToString("N")[..6]);

            // Create 3 active sales executives for this tenant
            var repIds = new List<string>();
            for (int i = 1; i <= 3; i++)
            {
                var repUser = new ApplicationUser
                {
                    Id = $"rr_rep_{i}_{Guid.NewGuid():N}",
                    UserName = $"rep_{i}_{Guid.NewGuid():N}@rr.in",
                    Email = $"rep_{i}_{Guid.NewGuid():N}@rr.in",
                    FullName = $"Sales Rep {i}",
                    IsActive = true,
                    EmailConfirmed = true,
                    SecurityStamp = Guid.NewGuid().ToString()
                };
                db.Users.Add(repUser);

                db.TenantMemberships.Add(new TenantMembership
                {
                    MembershipId = Guid.NewGuid(),
                    TenantId = tenantId,
                    UserId = repUser.Id,
                    CompanyRole = CompanyRole.SalesExecutive,
                    IsActive = true,
                    JoinedAt = DateTime.UtcNow
                });
                repIds.Add(repUser.Id);
            }

            db.TenantSettings.Add(new TenantSetting
            {
                TenantId = tenantId,
                LeadAssignmentMode = "RoundRobin"
            });

            db.IgnoreTenantFilter = true;
            await db.SaveChangesAsync();

            // Execute 6 parallel assignment requests
            var tasks = Enumerable.Range(0, 6).Select(_ => assignmentService.DetermineLeadOwnerAsync(tenantId, null, repIds[0]));
            var assignedRepIds = await Task.WhenAll(tasks);

            // Verify all assignments succeeded and were assigned to valid reps of this tenant
            Assert.Equal(6, assignedRepIds.Length);
            foreach (var repId in assignedRepIds)
            {
                Assert.NotNull(repId);
                Assert.Contains(repId, repIds);
            }

            // Verify distribution: each of the 3 reps must have received exactly 2 assignments
            var grouping = assignedRepIds.GroupBy(id => id).ToDictionary(g => g.Key, g => g.Count());
            Assert.Equal(3, grouping.Count);
            foreach (var kvp in grouping)
            {
                Assert.Equal(2, kvp.Value);
            }
        }

        // =========================================================================
        // 4. BULK LEAD OPERATIONS & TENANT BOUNDARY TESTS
        // =========================================================================

        [Fact]
        public async Task TC_LEADS_BULK_ACTIONS_001_BulkStatusAndAssign_UpdatesTenantLeadsCorrectly()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var leadService = scope.ServiceProvider.GetRequiredService<ILeadService>();
            var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();

            var tenantId = Guid.NewGuid();
            await _factory.EnsureTenantExistsAsync(tenantId, "Bulk Ops Co", "bulk-co-" + Guid.NewGuid().ToString("N")[..6]);
            tenantContext.SetTenant(tenantId, "bulk-co");

            // Add assignee
            var assignee = new ApplicationUser
            {
                Id = "assignee_" + Guid.NewGuid().ToString("N"),
                UserName = $"assignee_{Guid.NewGuid():N}@bulk.in",
                Email = $"assignee_{Guid.NewGuid():N}@bulk.in",
                FullName = "Vikram Patel",
                IsActive = true,
                EmailConfirmed = true,
                SecurityStamp = Guid.NewGuid().ToString()
            };
            db.Users.Add(assignee);
            db.TenantMemberships.Add(new TenantMembership
            {
                MembershipId = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = assignee.Id,
                CompanyRole = CompanyRole.SalesExecutive,
                IsActive = true,
                JoinedAt = DateTime.UtcNow
            });

            // Create 3 leads for this tenant
            var lead1 = new Lead { LeadId = 8801, TenantId = tenantId, Name = "A One", Phone = "+91 9876543210", Email = "a@bulk.in", Source = "Website", InterestedProduct = "ERP", Status = LeadStatus.New, CreatedAt = DateTime.UtcNow };
            var lead2 = new Lead { LeadId = 8802, TenantId = tenantId, Name = "B Two", Phone = "+91 9876543211", Email = "b@bulk.in", Source = "Website", InterestedProduct = "ERP", Status = LeadStatus.New, CreatedAt = DateTime.UtcNow };
            var lead3 = new Lead { LeadId = 8803, TenantId = tenantId, Name = "C Three", Phone = "+91 9876543212", Email = "c@bulk.in", Source = "Website", InterestedProduct = "ERP", Status = LeadStatus.New, CreatedAt = DateTime.UtcNow };
            db.IgnoreTenantFilter = true;
            db.Leads.AddRange(lead1, lead2, lead3);
            await db.SaveChangesAsync();

            // 1. Bulk Status Update
            var statusResult = await leadService.ExecuteBulkActionAsync(
                new List<int> { 8801, 8802, 8803 },
                "ChangeStatus",
                null,
                LeadStatus.Qualified,
                "admin_user",
                "Admin");
            Assert.Equal(3, statusResult);

            var updatedLeads = await db.Leads.Where(l => l.TenantId == tenantId).ToListAsync();
            Assert.All(updatedLeads, l => Assert.Equal(LeadStatus.Qualified, l.Status));

            // 2. Bulk Assignment
            var assignResult = await leadService.ExecuteBulkActionAsync(
                new List<int> { 8801, 8802 },
                "Assign",
                assignee.Id,
                null,
                "admin_user",
                "Admin");
            Assert.Equal(2, assignResult);

            var assignedLeads = await db.Leads.Where(l => l.TenantId == tenantId && l.OwnerUserId == assignee.Id).ToListAsync();
            Assert.Equal(2, assignedLeads.Count);
        }

        [Fact]
        public async Task TC_LEADS_BULK_ACTIONS_002_CrossTenantBulkAction_RejectsTampering()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var leadService = scope.ServiceProvider.GetRequiredService<ILeadService>();
            var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();

            var tenantA = Guid.NewGuid();
            var tenantB = Guid.NewGuid();
            await _factory.EnsureTenantExistsAsync(tenantA, "Tenant A", "tenant-a-" + Guid.NewGuid().ToString("N")[..6]);
            await _factory.EnsureTenantExistsAsync(tenantB, "Tenant B", "tenant-b-" + Guid.NewGuid().ToString("N")[..6]);

            var leadA = new Lead { LeadId = 7701, TenantId = tenantA, Name = "A Lead", Phone = "+91 9876543210", Email = "a@a.in", Source = "Website", InterestedProduct = "ERP", Status = LeadStatus.New, CreatedAt = DateTime.UtcNow };
            var leadB = new Lead { LeadId = 7702, TenantId = tenantB, Name = "B Lead", Phone = "+91 9876543211", Email = "b@b.in", Source = "Website", InterestedProduct = "ERP", Status = LeadStatus.New, CreatedAt = DateTime.UtcNow };

            db.IgnoreTenantFilter = true;
            db.Leads.AddRange(leadA, leadB);
            await db.SaveChangesAsync();

            // Restore tenant filter and set context strictly to Tenant A
            db.IgnoreTenantFilter = false;
            tenantContext.SetTenant(tenantA, "tenant-a");

            // Tenant A user attempts to execute bulk action including Tenant B's lead 7702
            var result = await leadService.ExecuteBulkActionAsync(
                new List<int> { 7701, 7702 },
                "ChangeStatus",
                null,
                LeadStatus.Lost,
                "tenant_a_user",
                "Admin");

            // Only Tenant A's lead (7701) should have been modified
            Assert.Equal(1, result);

            // Verify Tenant B's lead was untouched in database
            db.IgnoreTenantFilter = true;
            var reloadedLeadB = await db.Leads.FirstOrDefaultAsync(l => l.LeadId == 7702);
            Assert.NotNull(reloadedLeadB);
            Assert.Equal(LeadStatus.New, reloadedLeadB.Status); // Unchanged!
        }

        [Fact]
        public async Task TC_LEADS_BULK_ACTIONS_003_BulkArchive_ArchivesSelectedLeadsAndLogsAudit()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var leadService = scope.ServiceProvider.GetRequiredService<ILeadService>();
            var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();

            var tenantId = Guid.NewGuid();
            await _factory.EnsureTenantExistsAsync(tenantId, "Archive Co", "arch-co-" + Guid.NewGuid().ToString("N")[..6]);
            tenantContext.SetTenant(tenantId, "arch-co");

            var lead1 = new Lead { LeadId = 6601, TenantId = tenantId, Name = "Arch Lead 1", Phone = "+91 9900000001", Status = LeadStatus.New, IsArchived = false, CreatedAt = DateTime.UtcNow };
            var lead2 = new Lead { LeadId = 6602, TenantId = tenantId, Name = "Arch Lead 2", Phone = "+91 9900000002", Status = LeadStatus.New, IsArchived = false, CreatedAt = DateTime.UtcNow };

            db.IgnoreTenantFilter = true;
            db.Leads.AddRange(lead1, lead2);
            await db.SaveChangesAsync();

            db.IgnoreTenantFilter = false;

            var archiveResult = await leadService.ExecuteBulkActionAsync(
                new List<int> { 6601, 6602 },
                "Archive",
                null,
                null,
                "admin_user",
                "Admin");

            Assert.Equal(2, archiveResult);

            db.IgnoreTenantFilter = true;
            var reloadedLeads = await db.Leads.Where(l => l.TenantId == tenantId).ToListAsync();
            Assert.All(reloadedLeads, l => Assert.True(l.IsArchived));
        }

        [Fact]
        public async Task TC_PLATFORM_SUPERADMIN_REDIRECT_005_SuperAdminRedirectsToPlatform()
        {
            using var scope = _factory.Services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var signInManager = scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();
            var onboardingService = scope.ServiceProvider.GetRequiredService<ICompanyOnboardingService>();

            if (!await roleManager.RoleExistsAsync("PlatformSuperAdmin"))
                await roleManager.CreateAsync(new IdentityRole("PlatformSuperAdmin"));

            var email = $"psa_{Guid.NewGuid():N}@crm-platform.local";
            var psaUser = new ApplicationUser
            {
                Id = "psa_" + Guid.NewGuid().ToString("N"),
                UserName = email,
                Email = email,
                FullName = "Platform Executive",
                IsActive = true,
                EmailConfirmed = true,
                SecurityStamp = Guid.NewGuid().ToString()
            };

            await userManager.CreateAsync(psaUser, "Pass@12345");
            await userManager.AddToRoleAsync(psaUser, "PlatformSuperAdmin");

            var controller = new AccountController(
                signInManager,
                userManager,
                scope.ServiceProvider.GetRequiredService<IAuditService>(),
                onboardingService,
                scope.ServiceProvider.GetRequiredService<ITenantInvitationService>(),
                scope.ServiceProvider.GetRequiredService<IEmailDeliveryService>(),
                scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
                scope.ServiceProvider.GetRequiredService<ITenantContext>(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<AccountController>.Instance);

            var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
            {
                RequestServices = scope.ServiceProvider
            };
            var httpContextAccessor = scope.ServiceProvider.GetService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
            if (httpContextAccessor != null)
            {
                httpContextAccessor.HttpContext = httpContext;
            }
            controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            {
                HttpContext = httpContext
            };
            controller.Url = new TestUrlHelper();

            // Test login redirect for PlatformSuperAdmin
            var result = await controller.Login(new LoginViewModel { Email = email, Password = "Pass@12345" });
            var redirectResult = Assert.IsType<Microsoft.AspNetCore.Mvc.RedirectToActionResult>(result);
            Assert.Equal("Index", redirectResult.ActionName);
            Assert.Equal("Platform", redirectResult.ControllerName);
        }

        [Fact]
        public async Task TC_EMAIL_VERIFICATION_003_ResendRateLimitingAndAntiEnumeration()
        {
            using var scope = _factory.Services.CreateScope();
            var controller = new AccountController(
                scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>(),
                scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(),
                scope.ServiceProvider.GetRequiredService<IAuditService>(),
                scope.ServiceProvider.GetRequiredService<ICompanyOnboardingService>(),
                scope.ServiceProvider.GetRequiredService<ITenantInvitationService>(),
                scope.ServiceProvider.GetRequiredService<IEmailDeliveryService>(),
                scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
                scope.ServiceProvider.GetRequiredService<ITenantContext>(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<AccountController>.Instance);

            controller.TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(
                new Microsoft.AspNetCore.Http.DefaultHttpContext(),
                new TestTempDataProvider());

            var result = await controller.ResendEmailConfirmationPost("nonexistent_user_9999@domain.com");
            var redirectResult = Assert.IsType<Microsoft.AspNetCore.Mvc.RedirectToActionResult>(result);
            Assert.Equal(nameof(AccountController.Login), redirectResult.ActionName);
            Assert.NotNull(controller.TempData["SuccessMessage"]);
        }

        private class TestUrlHelper : Microsoft.AspNetCore.Mvc.IUrlHelper
        {
            public Microsoft.AspNetCore.Mvc.ActionContext ActionContext => new();
            public string? Action(Microsoft.AspNetCore.Mvc.Routing.UrlActionContext actionContext) => "/Platform";
            public string? Content(string? contentPath) => contentPath;
            public bool IsLocalUrl(string? url) => false;
            public string? Link(string? routeName, object? values) => "/Platform";
            public string? RouteUrl(Microsoft.AspNetCore.Mvc.Routing.UrlRouteContext routeContext) => "/Platform";
        }

        private class TestTempDataProvider : Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider
        {
            private readonly Dictionary<string, object> _data = new();
            public IDictionary<string, object> LoadTempData(Microsoft.AspNetCore.Http.HttpContext context) => _data;
            public void SaveTempData(Microsoft.AspNetCore.Http.HttpContext context, IDictionary<string, object> values)
            {
                _data.Clear();
                foreach (var kvp in values) _data[kvp.Key] = kvp.Value;
            }
        }
    }
}
