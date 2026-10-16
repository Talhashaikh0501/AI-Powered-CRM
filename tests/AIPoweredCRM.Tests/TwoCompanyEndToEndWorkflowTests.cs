using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using AI_Powered_CRM.Controllers;
using AI_Powered_CRM.Data;
using AI_Powered_CRM.DTOs;
using AI_Powered_CRM.Models;
using AI_Powered_CRM.Models.Entities;
using AI_Powered_CRM.Models.Enums;
using AI_Powered_CRM.Security;
using AI_Powered_CRM.Services.Implementations;
using AI_Powered_CRM.Services.Interfaces;
using AI_Powered_CRM.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AIPoweredCRM.Tests
{
    public class TwoCompanyEndToEndWorkflowTests : IClassFixture<CrmWebApplicationFactory>
    {
        private readonly CrmWebApplicationFactory _factory;

        public TwoCompanyEndToEndWorkflowTests(CrmWebApplicationFactory factory)
        {
            _factory = factory;
        }

        private class TestTempDataProvider : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
            public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
        }

        private static TempDataDictionary CreateTestTempData(HttpContext context)
        {
            return new TempDataDictionary(context, new TestTempDataProvider());
        }

        [Fact]
        public async Task TC_TWO_COMPANY_001_IndependentOnboarding_AndEmailVerification()
        {
            using var scope = _factory.Services.CreateScope();
            var onboardingService = scope.ServiceProvider.GetRequiredService<ICompanyOnboardingService>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var alphaSlug = "alpha-tech-" + Guid.NewGuid().ToString("N")[..8];
            var betaSlug = "beta-logistics-" + Guid.NewGuid().ToString("N")[..8];

            var alphaEmail = $"founder_alpha_{Guid.NewGuid():N}@alpha-tech.in";
            var betaEmail = $"founder_beta_{Guid.NewGuid():N}@beta-logistics.in";

            // 1. Register Company Alpha
            var alphaResult = await onboardingService.RegisterCompanyAsync(new RegisterCompanyViewModel
            {
                CompanyName = "Alpha Technologies Pvt Ltd",
                Slug = alphaSlug,
                AdminFullName = "Rohan Mehta",
                ContactEmail = alphaEmail,
                Phone = "+91 9820011223",
                Password = "AlphaPass@2026",
                ConfirmPassword = "AlphaPass@2026"
            });
            Assert.True(alphaResult.Succeeded, string.Join("; ", alphaResult.Errors));
            Assert.NotNull(alphaResult.TenantId);
            Assert.NotNull(alphaResult.UserId);

            // 2. Register Company Beta
            var betaResult = await onboardingService.RegisterCompanyAsync(new RegisterCompanyViewModel
            {
                CompanyName = "Beta Logistics Solutions",
                Slug = betaSlug,
                AdminFullName = "Priya Sharma",
                ContactEmail = betaEmail,
                Phone = "+91 9840033445",
                Password = "BetaPass@2026",
                ConfirmPassword = "BetaPass@2026"
            });
            Assert.True(betaResult.Succeeded, string.Join("; ", betaResult.Errors));
            Assert.NotNull(betaResult.TenantId);
            Assert.NotNull(betaResult.UserId);

            // 3. Verify Both Tenants Created with Defaults
            db.IgnoreTenantFilter = true;
            var alphaTenant = await db.Tenants.FirstOrDefaultAsync(t => t.TenantId == alphaResult.TenantId.Value);
            var betaTenant = await db.Tenants.FirstOrDefaultAsync(t => t.TenantId == betaResult.TenantId.Value);

            Assert.NotNull(alphaTenant);
            Assert.NotNull(betaTenant);
            Assert.NotEqual(alphaTenant.TenantId, betaTenant.TenantId);
            Assert.Equal("INR", alphaTenant.DefaultCurrency);
            Assert.Equal("INR", betaTenant.DefaultCurrency);
            Assert.Equal(SubscriptionTier.FreeTrial, alphaTenant.SubscriptionTier);
            Assert.Equal(SubscriptionTier.FreeTrial, betaTenant.SubscriptionTier);

            // 4. Verify Email Confirmation Required
            var alphaUser = await userManager.FindByIdAsync(alphaResult.UserId);
            var betaUser = await userManager.FindByIdAsync(betaResult.UserId);

            Assert.NotNull(alphaUser);
            Assert.NotNull(betaUser);
            Assert.False(alphaUser.EmailConfirmed);
            Assert.False(betaUser.EmailConfirmed);

            // Confirm Emails with Tokens
            var alphaToken = await userManager.GenerateEmailConfirmationTokenAsync(alphaUser);
            var alphaConfirm = await userManager.ConfirmEmailAsync(alphaUser, alphaToken);
            Assert.True(alphaConfirm.Succeeded);

            var betaToken = await userManager.GenerateEmailConfirmationTokenAsync(betaUser);
            var betaConfirm = await userManager.ConfirmEmailAsync(betaUser, betaToken);
            Assert.True(betaConfirm.Succeeded);

            var reloadedAlphaUser = await userManager.FindByIdAsync(alphaResult.UserId);
            var reloadedBetaUser = await userManager.FindByIdAsync(betaResult.UserId);
            Assert.True(reloadedAlphaUser!.EmailConfirmed);
            Assert.True(reloadedBetaUser!.EmailConfirmed);
        }

        [Fact]
        public async Task TC_TWO_COMPANY_002_CrossTenantInvitation_AndAcceptanceIsolation()
        {
            using var scope = _factory.Services.CreateScope();
            var onboardingService = scope.ServiceProvider.GetRequiredService<ICompanyOnboardingService>();
            var invitationService = scope.ServiceProvider.GetRequiredService<ITenantInvitationService>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // Register Alpha & Beta
            var alphaRes = await onboardingService.RegisterCompanyAsync(new RegisterCompanyViewModel
            {
                CompanyName = "Alpha Corp " + Guid.NewGuid().ToString("N")[..6],
                Slug = "alpha-inv-" + Guid.NewGuid().ToString("N")[..8],
                AdminFullName = "Alpha Admin",
                ContactEmail = $"admin_alpha_{Guid.NewGuid():N}@alphacorp.in",
                Password = "Password@123",
                ConfirmPassword = "Password@123"
            });
            var betaRes = await onboardingService.RegisterCompanyAsync(new RegisterCompanyViewModel
            {
                CompanyName = "Beta Corp " + Guid.NewGuid().ToString("N")[..6],
                Slug = "beta-inv-" + Guid.NewGuid().ToString("N")[..8],
                AdminFullName = "Beta Admin",
                ContactEmail = $"admin_beta_{Guid.NewGuid():N}@betacorp.in",
                Password = "Password@123",
                ConfirmPassword = "Password@123"
            });

            var alphaTenantId = alphaRes.TenantId!.Value;
            var betaTenantId = betaRes.TenantId!.Value;

            var alphaEmployeeEmail = $"emp_alpha_{Guid.NewGuid():N}@alphacorp.in";
            var betaEmployeeEmail = $"emp_beta_{Guid.NewGuid():N}@betacorp.in";

            // 1. Alpha invites employee as SalesExecutive
            var alphaInvite = await invitationService.CreateInvitationAsync(
                alphaTenantId,
                alphaEmployeeEmail,
                CompanyRole.SalesExecutive,
                alphaRes.UserId!
            );
            Assert.True(alphaInvite.Succeeded, string.Join("; ", alphaInvite.Errors));
            Assert.NotNull(alphaInvite.RawToken);

            // 2. Beta invites employee as Manager
            var betaInvite = await invitationService.CreateInvitationAsync(
                betaTenantId,
                betaEmployeeEmail,
                CompanyRole.Manager,
                betaRes.UserId!
            );
            Assert.True(betaInvite.Succeeded, string.Join("; ", betaInvite.Errors));
            Assert.NotNull(betaInvite.RawToken);

            // 3. Verify Invitation List Isolation
            var alphaInvitations = await invitationService.GetTenantInvitationsAsync(alphaTenantId);
            var betaInvitations = await invitationService.GetTenantInvitationsAsync(betaTenantId);

            Assert.Contains(alphaInvitations, i => i.Email == alphaEmployeeEmail);
            Assert.DoesNotContain(alphaInvitations, i => i.Email == betaEmployeeEmail);

            Assert.Contains(betaInvitations, i => i.Email == betaEmployeeEmail);
            Assert.DoesNotContain(betaInvitations, i => i.Email == alphaEmployeeEmail);

            // 4. Accept Alpha Invitation
            var alphaAccept = await invitationService.AcceptInvitationAsync(
                alphaInvite.RawToken!,
                "Sameer Verma",
                "EmpPass@2026"
            );
            Assert.True(alphaAccept.Succeeded, string.Join("; ", alphaAccept.Errors));

            // 5. Accept Beta Invitation
            var betaAccept = await invitationService.AcceptInvitationAsync(
                betaInvite.RawToken!,
                "Neha Kapoor",
                "EmpPass@2026"
            );
            Assert.True(betaAccept.Succeeded, string.Join("; ", betaAccept.Errors));

            // 6. Verify Memberships are isolated
            db.IgnoreTenantFilter = true;
            var sameerMembership = await db.TenantMemberships
                .FirstOrDefaultAsync(m => m.UserId == alphaAccept.UserId);
            var nehaMembership = await db.TenantMemberships
                .FirstOrDefaultAsync(m => m.UserId == betaAccept.UserId);

            Assert.NotNull(sameerMembership);
            Assert.Equal(alphaTenantId, sameerMembership.TenantId);
            Assert.Equal(CompanyRole.SalesExecutive, sameerMembership.CompanyRole);

            Assert.NotNull(nehaMembership);
            Assert.Equal(betaTenantId, nehaMembership.TenantId);
            Assert.Equal(CompanyRole.Manager, nehaMembership.CompanyRole);
        }

        [Fact]
        public async Task TC_TWO_COMPANY_003_EmployeeRoleUpdate_AndDeactivationProtection()
        {
            using var scope = _factory.Services.CreateScope();
            var onboardingService = scope.ServiceProvider.GetRequiredService<ICompanyOnboardingService>();
            var invitationService = scope.ServiceProvider.GetRequiredService<ITenantInvitationService>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var auditService = scope.ServiceProvider.GetRequiredService<IAuditService>();
            var quotaService = scope.ServiceProvider.GetRequiredService<ISubscriptionQuotaService>();
            var supportAccessService = scope.ServiceProvider.GetRequiredService<ISupportAccessService>();

            var regResult = await onboardingService.RegisterCompanyAsync(new RegisterCompanyViewModel
            {
                CompanyName = "Alpha Dynamics",
                Slug = "alpha-dyn-" + Guid.NewGuid().ToString("N")[..8],
                AdminFullName = "Admin Lead",
                ContactEmail = $"admin_dyn_{Guid.NewGuid():N}@alphadyn.in",
                Password = "Password@123",
                ConfirmPassword = "Password@123"
            });
            var tenantId = regResult.TenantId!.Value;
            var adminUserId = regResult.UserId!;

            // Invite employee
            var empEmail = $"emp_dyn_{Guid.NewGuid():N}@alphadyn.in";
            var inviteResult = await invitationService.CreateInvitationAsync(
                tenantId,
                empEmail,
                CompanyRole.SalesExecutive,
                adminUserId
            );
            var acceptResult = await invitationService.AcceptInvitationAsync(
                inviteResult.RawToken!,
                "Dev Anand",
                "EmpPass@123"
            );
            var employeeUserId = acceptResult.UserId!;

            // Set TenantContext to Alpha
            var tenantContext = new TenantContext();
            tenantContext.SetTenant(tenantId, "alpha-dyn");

            // Setup AdminController
            var adminController = new AdminController(
                userManager,
                roleManager,
                auditService,
                db,
                tenantContext,
                invitationService,
                quotaService,
                supportAccessService
            );

            // Simulate ClaimsPrincipal for Admin
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, adminUserId),
                new Claim(ClaimTypes.Name, "Admin Lead"),
                new Claim(ClaimTypes.Role, "Admin"),
                new Claim(ClaimTypes.Role, "CompanyAdmin")
            };
            var httpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
            };
            adminController.ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            };
            adminController.TempData = CreateTestTempData(httpContext);

            // 1. Promote employee from SalesExecutive to Manager
            var changeRoleResult = await adminController.ChangeUserRole(employeeUserId, CompanyRole.Manager);
            var redirectResult = Assert.IsType<RedirectToActionResult>(changeRoleResult);
            Assert.Equal(nameof(AdminController.Users), redirectResult.ActionName);

            db.IgnoreTenantFilter = true;
            var updatedMembership = await db.TenantMemberships
                .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.UserId == employeeUserId);
            Assert.NotNull(updatedMembership);
            Assert.Equal(CompanyRole.Manager, updatedMembership.CompanyRole);

            // 2. Deactivate employee
            var toggleResult = await adminController.ToggleUserStatus(employeeUserId);
            Assert.IsType<RedirectToActionResult>(toggleResult);

            var deactivatedMembership = await db.TenantMemberships
                .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.UserId == employeeUserId);
            Assert.NotNull(deactivatedMembership);
            Assert.False(deactivatedMembership.IsActive);

            // 3. Attempt to deactivate the sole remaining CompanyAdmin (should be blocked)
            var adminToggleResult = await adminController.ToggleUserStatus(adminUserId);
            Assert.IsType<RedirectToActionResult>(adminToggleResult);
            Assert.Equal("Cannot deactivate the only active Company Administrator in this workspace.", adminController.TempData["ErrorMessage"]);

            var adminMembership = await db.TenantMemberships
                .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.UserId == adminUserId);
            Assert.NotNull(adminMembership);
            Assert.True(adminMembership.IsActive, "Sole remaining CompanyAdmin must remain active.");
        }

        [Fact]
        public async Task TC_TWO_COMPANY_004_LeadLifecycle_KanbanPipeline_AndCrossTenantIsolation()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var leadService = scope.ServiceProvider.GetRequiredService<ILeadService>();
            var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();

            var tenantAId = Guid.NewGuid();
            var tenantBId = Guid.NewGuid();

            await _factory.EnsureTenantExistsAsync(tenantAId, "Alpha Corp", "alpha-corp");
            await _factory.EnsureTenantExistsAsync(tenantBId, "Beta Corp", "beta-corp");

            var userAId = "user_alpha_" + Guid.NewGuid().ToString("N")[..8];
            var userBId = "user_beta_" + Guid.NewGuid().ToString("N")[..8];

            db.IgnoreTenantFilter = true;
            db.Users.AddRange(
                new ApplicationUser { Id = userAId, UserName = "usera@alpha.in", Email = "usera@alpha.in", IsActive = true, SecurityStamp = Guid.NewGuid().ToString() },
                new ApplicationUser { Id = userBId, UserName = "userb@beta.in", Email = "userb@beta.in", IsActive = true, SecurityStamp = Guid.NewGuid().ToString() }
            );
            db.TenantMemberships.AddRange(
                new TenantMembership { MembershipId = Guid.NewGuid(), TenantId = tenantAId, UserId = userAId, CompanyRole = CompanyRole.CompanyAdmin, IsActive = true, JoinedAt = DateTime.UtcNow },
                new TenantMembership { MembershipId = Guid.NewGuid(), TenantId = tenantBId, UserId = userBId, CompanyRole = CompanyRole.CompanyAdmin, IsActive = true, JoinedAt = DateTime.UtcNow }
            );
            await db.SaveChangesAsync();

            // 1. Create 2 leads under Tenant A
            tenantContext.SetTenant(tenantAId, "alpha-corp");
            db.IgnoreTenantFilter = false;

            var leadA1 = await leadService.CreateLeadAsync(new CreateLeadDto
            {
                Name = "Alpha Cloud Migration Deal",
                Phone = "+91 9988001122",
                Email = "cio@clientalpha.com",
                Company = "Client Alpha",
                Source = "Website",
                InterestedProduct = "Cloud ERP",
                Budget = 500000m,
                Priority = LeadPriority.High
            }, userAId, "Admin");

            var leadA2 = await leadService.CreateLeadAsync(new CreateLeadDto
            {
                Name = "Alpha Security Audit Deal",
                Phone = "+91 9988001133",
                Email = "ciso@clientalpha.com",
                Company = "Client Alpha Security",
                Source = "Referral",
                InterestedProduct = "Security Suite",
                Budget = 250000m,
                Priority = LeadPriority.Medium
            }, userAId, "Admin");

            // 2. Create 1 lead under Tenant B
            tenantContext.SetTenant(tenantBId, "beta-corp");

            var leadB1 = await leadService.CreateLeadAsync(new CreateLeadDto
            {
                Name = "Beta Fleet Tracking Deal",
                Phone = "+91 9977001144",
                Email = "fleet@clientbeta.com",
                Company = "Client Beta Logistics",
                Source = "Cold Call",
                InterestedProduct = "GPS Telematics",
                Budget = 750000m,
                Priority = LeadPriority.High
            }, userBId, "Admin");

            // 3. Verify Isolation from Tenant A's context
            tenantContext.SetTenant(tenantAId, "alpha-corp");
            var (alphaLeads, alphaCount) = await leadService.GetPagedLeadsAsync(null, null, null, null, userAId, "Admin");
            Assert.Equal(2, alphaCount);
            Assert.Contains(alphaLeads, l => l.LeadId == leadA1.LeadId);
            Assert.Contains(alphaLeads, l => l.LeadId == leadA2.LeadId);
            Assert.DoesNotContain(alphaLeads, l => l.LeadId == leadB1.LeadId);

            // Attempt direct lookup of Beta's lead under Tenant A context -> must return null
            var crossLookup = await leadService.GetLeadDetailsAsync(leadB1.LeadId, userAId, "Admin");
            Assert.Null(crossLookup);

            // 4. Verify Isolation from Tenant B's context
            tenantContext.SetTenant(tenantBId, "beta-corp");
            var (betaLeads, betaCount) = await leadService.GetPagedLeadsAsync(null, null, null, null, userBId, "Admin");
            Assert.Equal(1, betaCount);
            Assert.Equal(leadB1.LeadId, betaLeads.First().LeadId);
            Assert.DoesNotContain(betaLeads, l => l.LeadId == leadA1.LeadId);

            // 5. Move Lead A1 through Kanban Pipeline
            tenantContext.SetTenant(tenantAId, "alpha-corp");
            var moveSuccess = await leadService.ChangeStatusAsync(leadA1.LeadId, LeadStatus.Qualified, "Passed technical scoping", userAId, "Admin");
            Assert.True(moveSuccess);

            var kanban = await leadService.GetPipelineKanbanAsync(userAId, "Admin");
            Assert.NotNull(kanban);
            Assert.True(kanban.ContainsKey(LeadStatus.Qualified));
            Assert.Contains(kanban[LeadStatus.Qualified], l => l.LeadId == leadA1.LeadId);
            Assert.Contains(kanban[LeadStatus.New], l => l.LeadId == leadA2.LeadId);
        }

        [Fact]
        public async Task TC_TWO_COMPANY_005_AiLeadScoring_DeterministicFallback_Isolation()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var leadService = scope.ServiceProvider.GetRequiredService<ILeadService>();
            var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();

            var tenantAId = Guid.NewGuid();
            var tenantBId = Guid.NewGuid();
            await _factory.EnsureTenantExistsAsync(tenantAId, "Alpha Analytics", "alpha-analytics");
            await _factory.EnsureTenantExistsAsync(tenantBId, "Beta Analytics", "beta-analytics");

            var userAId = "user_ai_a_" + Guid.NewGuid().ToString("N")[..8];
            db.IgnoreTenantFilter = true;
            db.Users.Add(new ApplicationUser { Id = userAId, UserName = "ai_a@test.in", Email = "ai_a@test.in", IsActive = true, SecurityStamp = Guid.NewGuid().ToString() });
            db.TenantMemberships.Add(new TenantMembership { MembershipId = Guid.NewGuid(), TenantId = tenantAId, UserId = userAId, CompanyRole = CompanyRole.CompanyAdmin, IsActive = true, JoinedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();

            tenantContext.SetTenant(tenantAId, "alpha-analytics");
            db.IgnoreTenantFilter = false;

            var lead = await leadService.CreateLeadAsync(new CreateLeadDto
            {
                Name = "AI Scored Deal",
                Phone = "+91 9988776655",
                Email = "decision_maker@enterprise.in",
                Company = "Enterprise Corp",
                Source = "Inbound",
                InterestedProduct = "Full Enterprise Platform",
                Budget = 1200000m,
                Priority = LeadPriority.High
            }, userAId, "Admin");

            // Run AI analysis (executes deterministic fallback when Groq key is absent/test mode)
            var insight = await leadService.RunAIAnalysisAsync(lead.LeadId, userAId, "Admin");

            Assert.NotNull(insight);
            Assert.True(insight.Score >= 0 && insight.Score <= 100);
            Assert.False(string.IsNullOrWhiteSpace(insight.Summary));
            Assert.False(string.IsNullOrWhiteSpace(insight.NextBestAction));
            Assert.False(string.IsNullOrWhiteSpace(insight.ReasonJson));

            // Switch to Tenant B context -> AILeadInsights for Lead A must not be visible
            tenantContext.SetTenant(tenantBId, "beta-analytics");
            var visibleInsights = await db.AILeadInsights.Where(i => i.LeadId == lead.LeadId).ToListAsync();
            Assert.Empty(visibleInsights);
        }

        [Fact]
        public async Task TC_TWO_COMPANY_006_AnalyticsReporting_AndCsvExport_Isolation()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var leadService = scope.ServiceProvider.GetRequiredService<ILeadService>();
            var reportService = scope.ServiceProvider.GetRequiredService<IReportService>();
            var auditService = scope.ServiceProvider.GetRequiredService<IAuditService>();
            var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();

            var tenantAId = Guid.NewGuid();
            var tenantBId = Guid.NewGuid();
            await _factory.EnsureTenantExistsAsync(tenantAId, "Alpha Reports", "alpha-rep");
            await _factory.EnsureTenantExistsAsync(tenantBId, "Beta Reports", "beta-rep");

            var userAId = "user_rep_a_" + Guid.NewGuid().ToString("N")[..8];
            var userBId = "user_rep_b_" + Guid.NewGuid().ToString("N")[..8];

            db.IgnoreTenantFilter = true;
            db.Users.AddRange(
                new ApplicationUser { Id = userAId, UserName = "repa@alpha.in", Email = "repa@alpha.in", IsActive = true, SecurityStamp = Guid.NewGuid().ToString() },
                new ApplicationUser { Id = userBId, UserName = "repb@beta.in", Email = "repb@beta.in", IsActive = true, SecurityStamp = Guid.NewGuid().ToString() }
            );
            db.TenantMemberships.AddRange(
                new TenantMembership { MembershipId = Guid.NewGuid(), TenantId = tenantAId, UserId = userAId, CompanyRole = CompanyRole.CompanyAdmin, IsActive = true, JoinedAt = DateTime.UtcNow },
                new TenantMembership { MembershipId = Guid.NewGuid(), TenantId = tenantBId, UserId = userBId, CompanyRole = CompanyRole.CompanyAdmin, IsActive = true, JoinedAt = DateTime.UtcNow }
            );
            await db.SaveChangesAsync();

            // 1. Create & convert lead under Tenant A
            tenantContext.SetTenant(tenantAId, "alpha-rep");
            db.IgnoreTenantFilter = false;

            var leadA = await leadService.CreateLeadAsync(new CreateLeadDto
            {
                Name = "Alpha Conversion Deal",
                Phone = "+91 9111222333",
                Company = "Big Client A",
                Source = "Organic Search",
                InterestedProduct = "Subscription A",
                Budget = 300000m,
                Priority = LeadPriority.High
            }, userAId, "Admin");

            await leadService.ChangeStatusAsync(leadA.LeadId, LeadStatus.Converted, "Signed 1 year contract", userAId, "Admin");

            // 2. Create unconverted lead under Tenant B
            tenantContext.SetTenant(tenantBId, "beta-rep");
            var leadB = await leadService.CreateLeadAsync(new CreateLeadDto
            {
                Name = "Beta Pipeline Deal",
                Phone = "+91 9444555666",
                Company = "Client B",
                Source = "Social Media",
                InterestedProduct = "Package B",
                Budget = 100000m,
                Priority = LeadPriority.Low
            }, userBId, "Admin");

            // 3. Test Report Service under Tenant A
            tenantContext.SetTenant(tenantAId, "alpha-rep");
            var alphaSummary = await reportService.GetDashboardSummaryAsync(userAId, "Admin");
            var alphaConversion = await reportService.GetConversionReportAsync();

            Assert.Equal(0, alphaSummary.TotalActiveLeads); // converted lead is no longer in active pipeline
            Assert.Equal(1, alphaConversion.TotalLeads);
            Assert.Equal(1, alphaConversion.ConvertedLeads);
            Assert.Equal(300000m, alphaConversion.TotalWonDealValue);

            // 4. Test Report Service under Tenant B
            tenantContext.SetTenant(tenantBId, "beta-rep");
            var betaSummary = await reportService.GetDashboardSummaryAsync(userBId, "Admin");
            var betaConversion = await reportService.GetConversionReportAsync();

            Assert.Equal(1, betaSummary.TotalActiveLeads);
            Assert.Equal(1, betaConversion.TotalLeads);
            Assert.Equal(0, betaConversion.ConvertedLeads);
            Assert.Equal(0m, betaConversion.TotalWonDealValue);

            // 5. Test CSV Export through ReportsController under Tenant A
            tenantContext.SetTenant(tenantAId, "alpha-rep");
            var reportsController = new ReportsController(reportService, db, auditService, tenantContext);
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, userAId),
                new Claim(ClaimTypes.Role, "Admin"),
                new Claim(ClaimTypes.Role, "CompanyAdmin")
            };
            var httpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
            };
            reportsController.ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            };
            reportsController.TempData = CreateTestTempData(httpContext);

            var csvResult = await reportsController.ExportLeadsCsv();
            var fileResult = Assert.IsType<FileContentResult>(csvResult);
            var csvText = Encoding.UTF8.GetString(fileResult.FileContents);

            Assert.Contains("Alpha Conversion Deal", csvText);
            Assert.DoesNotContain("Beta Pipeline Deal", csvText);
        }

        [Fact]
        public async Task TC_TWO_COMPANY_007_CompanyPreferences_AndSettings_Isolation()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var auditService = scope.ServiceProvider.GetRequiredService<IAuditService>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var invitationService = scope.ServiceProvider.GetRequiredService<ITenantInvitationService>();
            var quotaService = scope.ServiceProvider.GetRequiredService<ISubscriptionQuotaService>();
            var supportAccessService = scope.ServiceProvider.GetRequiredService<ISupportAccessService>();

            var tenantAId = Guid.NewGuid();
            var tenantBId = Guid.NewGuid();
            await _factory.EnsureTenantExistsAsync(tenantAId, "Alpha Settings Co", "alpha-sett");
            await _factory.EnsureTenantExistsAsync(tenantBId, "Beta Settings Co", "beta-sett");

            db.IgnoreTenantFilter = true;
            var settingA = new TenantSetting
            {
                TenantId = tenantAId,
                CurrencyCode = "INR",
                CurrencySymbol = "₹",
                DateFormat = "dd/MM/yyyy",
                TimeZoneId = "India Standard Time",
                LeadAssignmentMode = "Manual",
                RestrictLeadExport = false
            };
            var settingB = new TenantSetting
            {
                TenantId = tenantBId,
                CurrencyCode = "INR",
                CurrencySymbol = "₹",
                DateFormat = "dd/MM/yyyy",
                TimeZoneId = "India Standard Time",
                LeadAssignmentMode = "Manual",
                RestrictLeadExport = false
            };
            db.TenantSettings.AddRange(settingA, settingB);
            await db.SaveChangesAsync();

            var tenantContext = new TenantContext();
            tenantContext.SetTenant(tenantAId, "alpha-sett");

            var adminController = new AdminController(
                userManager,
                roleManager,
                auditService,
                db,
                tenantContext,
                invitationService,
                quotaService,
                supportAccessService
            );
            var httpContext = new DefaultHttpContext();
            adminController.ControllerContext = new ControllerContext { HttpContext = httpContext };
            adminController.TempData = CreateTestTempData(httpContext);

            // Update Alpha settings: RoundRobin, RestrictLeadExport = true
            var updatedModel = new CompanySettingsViewModel
            {
                TenantId = tenantAId,
                CompanyName = "Alpha Settings Co Updated",
                ContactEmail = "contact@alphasett.in",
                CurrencyCode = "INR",
                CurrencySymbol = "₹",
                DateFormat = "yyyy-MM-dd",
                TimeZoneId = "India Standard Time",
                LeadAssignmentMode = "RoundRobin",
                RestrictLeadExport = true
            };

            var result = await adminController.Settings(updatedModel);
            Assert.IsType<RedirectToActionResult>(result);

            // Verify Alpha updated
            db.IgnoreTenantFilter = true;
            var reloadedA = await db.TenantSettings.FirstOrDefaultAsync(s => s.TenantId == tenantAId);
            Assert.NotNull(reloadedA);
            Assert.Equal("RoundRobin", reloadedA.LeadAssignmentMode);
            Assert.True(reloadedA.RestrictLeadExport);
            Assert.Equal("yyyy-MM-dd", reloadedA.DateFormat);

            // Verify Beta remains untouched
            var reloadedB = await db.TenantSettings.FirstOrDefaultAsync(s => s.TenantId == tenantBId);
            Assert.NotNull(reloadedB);
            Assert.Equal("Manual", reloadedB.LeadAssignmentMode);
            Assert.False(reloadedB.RestrictLeadExport);
            Assert.Equal("dd/MM/yyyy", reloadedB.DateFormat);
        }

        [Fact]
        public async Task TC_TWO_COMPANY_008_PlatformSuperAdmin_TenantSuspension_AndTierUpgrade()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var auditService = scope.ServiceProvider.GetRequiredService<IAuditService>();
            var supportAccessService = scope.ServiceProvider.GetRequiredService<ISupportAccessService>();

            var tenantAId = Guid.NewGuid();
            var tenantBId = Guid.NewGuid();
            await _factory.EnsureTenantExistsAsync(tenantAId, "Alpha Operations", "alpha-ops");
            await _factory.EnsureTenantExistsAsync(tenantBId, "Beta Operations", "beta-ops");

            // Setup PlatformController
            var platformController = new PlatformController(db, supportAccessService, auditService, NullLogger<PlatformController>.Instance);
            var superAdminUser = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "platform_superadmin_user"),
                new Claim(ClaimTypes.Role, "PlatformSuperAdmin")
            }, "TestPlatformAuth"));

            var httpContext = new DefaultHttpContext { User = superAdminUser };
            platformController.ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            };
            platformController.TempData = CreateTestTempData(httpContext);

            // 1. Upgrade Alpha to Professional tier with 15 seats and 10000 leads
            var upgradeModel = new UpdateTenantPlanViewModel
            {
                TenantId = tenantAId,
                SubscriptionTier = SubscriptionTier.Professional,
                SubscriptionStatus = SubscriptionStatus.Active,
                MaxUserSeats = 15,
                MaxLeadsQuota = 10000,
                MonthlyAiRequestLimit = 2000
            };
            var upgradeResult = await platformController.UpdateTenantPlan(upgradeModel);
            Assert.IsType<RedirectToActionResult>(upgradeResult);

            db.IgnoreTenantFilter = true;
            var upgradedAlpha = await db.Tenants.FirstOrDefaultAsync(t => t.TenantId == tenantAId);
            Assert.NotNull(upgradedAlpha);
            Assert.Equal(SubscriptionTier.Professional, upgradedAlpha.SubscriptionTier);
            Assert.Equal(15, upgradedAlpha.MaxUserSeats);
            Assert.Equal(10000, upgradedAlpha.MaxLeadsQuota);

            // 2. Suspend Beta Tenant
            var toggleModel = new ToggleTenantStatusViewModel
            {
                TenantId = tenantBId,
                Reason = "Non-payment investigation"
            };
            var toggleBetaResult = await platformController.ToggleTenantStatus(toggleModel);
            Assert.IsType<RedirectToActionResult>(toggleBetaResult);

            var suspendedBeta = await db.Tenants.FirstOrDefaultAsync(t => t.TenantId == tenantBId);
            Assert.NotNull(suspendedBeta);
            Assert.False(suspendedBeta.IsActive, "Beta tenant must be deactivated/suspended.");

            // Alpha remains active
            var stillActiveAlpha = await db.Tenants.FirstOrDefaultAsync(t => t.TenantId == tenantAId);
            Assert.NotNull(stillActiveAlpha);
            Assert.True(stillActiveAlpha.IsActive, "Alpha tenant must remain active.");
        }
    }
}
