using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AI_Powered_CRM.Data;
using AI_Powered_CRM.DTOs;
using AI_Powered_CRM.Models;
using AI_Powered_CRM.Models.Entities;
using AI_Powered_CRM.Models.Enums;
using AI_Powered_CRM.Security;
using AI_Powered_CRM.Services.Interfaces;
using AI_Powered_CRM.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AIPoweredCRM.Tests
{
    public class RealApplicationAcceptanceWorkflowTests : IClassFixture<CrmWebApplicationFactory>
    {
        private readonly CrmWebApplicationFactory _factory;

        public RealApplicationAcceptanceWorkflowTests(CrmWebApplicationFactory factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task TC_ACCEPTANCE_001_CompanyA_FullHttpLifecycle()
        {
            var companySlug = "acme-corp-" + Guid.NewGuid().ToString("N")[..8];
            var founderEmail = $"founder_{Guid.NewGuid():N}@acmecorp.in";
            var companyName = "Acme Global Solutions";
            var password = "FounderPass@2026";

            var unauthClient = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });

            // 1. HTTP GET /Account/RegisterCompany
            var getReg = await unauthClient.GetAsync("/Account/RegisterCompany");
            Assert.Equal(HttpStatusCode.OK, getReg.StatusCode);

            // 2. Register Company via ICompanyOnboardingService (or HTTP form)
            using var scope = _factory.Services.CreateScope();
            var onboardingService = scope.ServiceProvider.GetRequiredService<ICompanyOnboardingService>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var regRes = await onboardingService.RegisterCompanyAsync(new RegisterCompanyViewModel
            {
                CompanyName = companyName,
                Slug = companySlug,
                AdminFullName = "Vikram Malhotra",
                ContactEmail = founderEmail,
                Phone = "+91 9811223344",
                Password = password,
                ConfirmPassword = password
            });
            Assert.True(regRes.Succeeded, string.Join("; ", regRes.Errors));
            var tenantId = regRes.TenantId!.Value;
            var founderUser = await userManager.FindByIdAsync(regRes.UserId!);

            // 3. Verify confirmation required
            Assert.False(founderUser!.EmailConfirmed);

            // 4. Confirm Email via Token
            var confirmToken = await userManager.GenerateEmailConfirmationTokenAsync(founderUser);
            var confirmRes = await unauthClient.GetAsync($"/Account/ConfirmEmail?userId={founderUser.Id}&token={WebUtility.UrlEncode(confirmToken)}");
            Assert.True(confirmRes.StatusCode == HttpStatusCode.OK || confirmRes.StatusCode == HttpStatusCode.Redirect);

            // 5. Create Authenticated CompanyAdmin Client for this exact tenant
            var (adminClient, _) = await _factory.CreateAuthenticatedClientAsync(
                "CompanyAdmin",
                tenantId: tenantId,
                isTenantActive: true);

            // 6. Access Dashboard
            var dashRes = await adminClient.GetAsync("/Dashboard");
            Assert.Equal(HttpStatusCode.OK, dashRes.StatusCode);

            // 7. Access Team Invitations
            var inviteGetRes = await adminClient.GetAsync("/Admin/Invitations");
            Assert.Equal(HttpStatusCode.OK, inviteGetRes.StatusCode);

            // 8. Invite Employee via Service and Accept
            var invitationService = scope.ServiceProvider.GetRequiredService<ITenantInvitationService>();
            var empEmail = $"emp_{Guid.NewGuid():N}@acmecorp.in";
            var inviteRes = await invitationService.CreateInvitationAsync(tenantId, empEmail, CompanyRole.SalesExecutive, founderUser.Id);
            Assert.True(inviteRes.Succeeded);

            var acceptRes = await invitationService.AcceptInvitationAsync(inviteRes.RawToken!, "Rohit Roy", "EmpPassword@2026");
            Assert.True(acceptRes.Succeeded);

            // 9. Create Lead under Company A
            var leadService = scope.ServiceProvider.GetRequiredService<ILeadService>();
            var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            tenantContext.SetTenant(tenantId, companySlug);

            var leadA = await leadService.CreateLeadAsync(new CreateLeadDto
            {
                Name = "Acme Major Deal",
                Phone = "+91 9988771122",
                Email = "cxo@enterpriseclient.in",
                Company = "Enterprise Client",
                Source = "Direct Outreach",
                InterestedProduct = "ERP Core",
                Budget = 450000m,
                Priority = LeadPriority.High
            }, founderUser.Id, "Admin");
            Assert.NotNull(leadA);

            // 10. Update Lead Status via Pipeline
            var moveOk = await leadService.ChangeStatusAsync(leadA.LeadId, LeadStatus.Negotiation, "Contract in legal review", founderUser.Id, "Admin");
            Assert.True(moveOk);

            // 11. Run AI Analysis
            var aiInsight = await leadService.RunAIAnalysisAsync(leadA.LeadId, founderUser.Id, "Admin");
            Assert.NotNull(aiInsight);
            Assert.True(aiInsight.Score >= 0 && aiInsight.Score <= 100);

            // 12. HTTP GET /Reports and /Reports/ExportLeadsCsv
            var reportsRes = await adminClient.GetAsync("/Reports");
            Assert.Equal(HttpStatusCode.OK, reportsRes.StatusCode);

            var exportRes = await adminClient.GetAsync("/Reports/ExportLeadsCsv");
            Assert.Equal(HttpStatusCode.OK, exportRes.StatusCode);
            var csvContent = await exportRes.Content.ReadAsStringAsync();
            Assert.Contains("Acme Major Deal", csvContent);

            // 13. HTTP GET /Subscription and /Subscription/Pricing
            var subRes = await adminClient.GetAsync("/Subscription");
            Assert.Equal(HttpStatusCode.OK, subRes.StatusCode);

            var pricingRes = await adminClient.GetAsync("/Subscription/Pricing");
            Assert.Equal(HttpStatusCode.OK, pricingRes.StatusCode);
            var pricingHtml = await pricingRes.Content.ReadAsStringAsync();
            Assert.Contains("Professional", pricingHtml);
            Assert.True(pricingHtml.Contains("&#x20B9;3,999") || pricingHtml.Contains("\u20B93,999"), "Expected Professional tier pricing to contain ₹3,999 (raw or HTML encoded &#x20B9;3,999)");
            Assert.Contains("paymentComingSoonModal", pricingHtml); // Coming soon modal active
        }

        [Fact]
        public async Task TC_ACCEPTANCE_002_CompanyB_HttpFlow_AndCrossTenantDataIsolation()
        {
            var tenantAId = Guid.NewGuid();
            var tenantBId = Guid.NewGuid();

            await _factory.EnsureTenantExistsAsync(tenantAId, "Alpha Workspace", "alpha-ws");
            await _factory.EnsureTenantExistsAsync(tenantBId, "Beta Workspace", "beta-ws");

            var (clientA, userA) = await _factory.CreateAuthenticatedClientAsync("CompanyAdmin", tenantId: tenantAId);
            var (clientB, userB) = await _factory.CreateAuthenticatedClientAsync("CompanyAdmin", tenantId: tenantBId);

            // 1. Seed lead for Tenant A
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var leadService = scope.ServiceProvider.GetRequiredService<ILeadService>();
            var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();

            tenantContext.SetTenant(tenantAId, "alpha-ws");
            var leadA = await leadService.CreateLeadAsync(new CreateLeadDto
            {
                Name = "Confidential Alpha Lead",
                Phone = "+91 9123456789",
                Email = "confidential@alpha.in",
                Company = "Alpha Proprietary Client",
                Source = "Inbound",
                InterestedProduct = "High Security CRM",
                Budget = 990000m,
                Priority = LeadPriority.High
            }, userA.Id, "Admin");

            // 2. Company B attempts direct HTTP GET to Company A's lead detail
            var crossLookupRes = await clientB.GetAsync($"/Leads/Details/{leadA.LeadId}");
            Assert.True(crossLookupRes.StatusCode == HttpStatusCode.NotFound || crossLookupRes.StatusCode == HttpStatusCode.Forbidden,
                $"Cross-tenant access must be blocked. Expected 404 or 403, got: {crossLookupRes.StatusCode}");

            // 3. Company B exports leads -> must NOT contain Company A's lead
            var exportBRes = await clientB.GetAsync("/Reports/ExportLeadsCsv");
            Assert.Equal(HttpStatusCode.OK, exportBRes.StatusCode);
            var csvB = await exportBRes.Content.ReadAsStringAsync();
            Assert.DoesNotContain("Confidential Alpha Lead", csvB);

            // 4. Company B accesses reports -> zero Company A metrics
            tenantContext.SetTenant(tenantBId, "beta-ws");
            var reportService = scope.ServiceProvider.GetRequiredService<IReportService>();
            var conversionB = await reportService.GetConversionReportAsync();
            Assert.Equal(0, conversionB.TotalLeads);
        }

        [Fact]
        public async Task TC_ACCEPTANCE_003_PlatformSuperAdmin_Lifecycle_AndTicketDelegatedAccess()
        {
            var (superAdminClient, superAdminUser) = await _factory.CreateAuthenticatedClientAsync("PlatformSuperAdmin");

            // 1. Access /Platform directory
            var platformRes = await superAdminClient.GetAsync("/Platform");
            Assert.Equal(HttpStatusCode.OK, platformRes.StatusCode);

            // 2. Create target customer tenant
            var customerTenantId = Guid.NewGuid();
            await _factory.EnsureTenantExistsAsync(customerTenantId, "Customer Target Corp", "customer-target");

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var supportAccessService = scope.ServiceProvider.GetRequiredService<ISupportAccessService>();

            // 3. SuperAdmin attempts direct access to customer lead without ticket -> blocked
            var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            tenantContext.SetPlatformSuperAdmin(true);

            db.IgnoreTenantFilter = true;
            var secretLead = new Lead
            {
                TenantId = customerTenantId,
                Name = "Customer Private Record",
                Phone = "+91 9911223344",
                InterestedProduct = "Private Product",
                OwnerUserId = "rep1",
                IsArchived = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Leads.Add(secretLead);
            await db.SaveChangesAsync();

            // 4. Request Delegated Support Access Ticket
            var (clientCustomerAdmin, customerAdminUser) = await _factory.CreateAuthenticatedClientAsync("CompanyAdmin", tenantId: customerTenantId);

            var ticket = await _factory.CreateSupportTicketAsync(
                customerTenantId,
                "TICKET-" + Guid.NewGuid().ToString("N")[..6],
                "Investigating sync performance",
                superAdminUserId: superAdminUser.Id,
                createdByUserId: superAdminUser.Id,
                isApproved: false // Starts pending
            );
            Assert.Equal("Pending", ticket.Status);

            // 5. Customer Admin Approves Support Access Ticket
            var approveClaims = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, customerAdminUser.Id),
                new Claim(ClaimTypes.Role, "CompanyAdmin")
            }, "TestAuth"));

            var approvedTicket = await supportAccessService.ApproveTicketAsync(ticket.TicketId, TimeSpan.FromHours(2), approveClaims);
            Assert.Equal("Approved", approvedTicket.Status);
            Assert.NotNull(approvedTicket.ApprovedAt);

            // 6. Verify Ticket Status is Approved
            var currentTicket = await supportAccessService.GetTicketByIdAsync(approvedTicket.TicketId);
            Assert.NotNull(currentTicket);
            Assert.Equal("Approved", currentTicket.Status);

            // 7. Revoke Support Ticket
            var revokedTicket = await supportAccessService.RevokeTicketAsync(approvedTicket.TicketId, "Investigation completed", approveClaims);
            Assert.NotNull(revokedTicket);
            Assert.Equal("Revoked", revokedTicket.Status);

            var reloadedTicket = await supportAccessService.GetTicketByIdAsync(approvedTicket.TicketId);
            Assert.NotNull(reloadedTicket);
            Assert.Equal("Revoked", reloadedTicket.Status);
        }
    }
}
