using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using AI_Powered_CRM.Data;
using AI_Powered_CRM.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AIPoweredCRM.Tests
{
    public class HttpSecurityIntegrationTests : IClassFixture<CrmWebApplicationFactory>
    {
        private readonly CrmWebApplicationFactory _factory;

        public HttpSecurityIntegrationTests(CrmWebApplicationFactory factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task TC_HTTP_001_Unauthenticated_LeadsApi_Returns_401()
        {
            var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });

            var response = await client.GetAsync("/api/v1/leads");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal(401, (int)response.StatusCode);
        }

        [Fact]
        public async Task TC_HTTP_002_Unauthenticated_ReportsConversionApi_Returns_401()
        {
            var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });

            var response = await client.GetAsync("/api/v1/reports/conversion");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal(401, (int)response.StatusCode);
        }

        [Fact]
        public async Task TC_HTTP_003_Unauthenticated_MvcReports_RedirectsToLogin_302()
        {
            var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });

            var response = await client.GetAsync("/Reports");
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.NotNull(response.Headers.Location);
            Assert.Contains("/Account/Login", response.Headers.Location.ToString());
        }

        [Fact]
        public async Task TC_HTTP_004_SalesExecutive_Denied_ReportsConversionApi_Returns_403()
        {
            var (client, _) = await _factory.CreateAuthenticatedClientAsync("SalesExecutive");

            var response = await client.GetAsync("/api/v1/reports/conversion");
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(403, (int)response.StatusCode);
        }

        [Fact]
        public async Task TC_HTTP_005_SalesExecutive_Denied_ReportsStaffApi_Returns_403()
        {
            var (client, _) = await _factory.CreateAuthenticatedClientAsync("SalesExecutive");

            var response = await client.GetAsync("/api/v1/reports/staff");
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(403, (int)response.StatusCode);
        }

        [Fact]
        public async Task TC_HTTP_006_SalesExecutive_Denied_MvcReports_RedirectsToAccessDenied_302()
        {
            var (client, _) = await _factory.CreateAuthenticatedClientAsync("SalesExecutive");

            var response = await client.GetAsync("/Reports");
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.NotNull(response.Headers.Location);
            Assert.Contains("/Account/AccessDenied", response.Headers.Location.ToString());
        }

        [Fact]
        public async Task TC_HTTP_007_Admin_Authorized_ReportsConversionApi_Returns_200()
        {
            var (client, _) = await _factory.CreateAuthenticatedClientAsync("Admin");

            var response = await client.GetAsync("/api/v1/reports/conversion");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(200, (int)response.StatusCode);
        }

        [Fact]
        public async Task TC_HTTP_008_SessionInvalidation_DeactivatedUser_Rejected_Immediately()
        {
            // 1. Create authenticated client for active user
            var (client, user) = await _factory.CreateAuthenticatedClientAsync("SalesExecutive", isActive: true);

            // 2. Verified authenticated user can access leads API
            var initialResponse = await client.GetAsync("/api/v1/leads");
            Assert.Equal(HttpStatusCode.OK, initialResponse.StatusCode);

            // 3. Deactivate user in database
            using (var scope = _factory.Services.CreateScope())
            {
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var dbUser = await userManager.FindByIdAsync(user.Id);
                Assert.NotNull(dbUser);
                dbUser.IsActive = false;
                await userManager.UpdateAsync(dbUser);
                await userManager.UpdateSecurityStampAsync(dbUser);
            }

            // 4. Send request with the same session cookie -> OnValidatePrincipal rejects deactivated user
            var responseAfterDeactivation = await client.GetAsync("/api/v1/leads");
            Assert.Equal(HttpStatusCode.Unauthorized, responseAfterDeactivation.StatusCode);
            Assert.Equal(401, (int)responseAfterDeactivation.StatusCode);
        }

        [Fact]
        public async Task TC_HTTP_009_CrossTenant_Header_Spoofing_Rejected_With_403()
        {
            var companyAId = Guid.NewGuid();
            var companyBId = Guid.NewGuid();

            await _factory.EnsureTenantExistsAsync(companyAId, "Company Alpha", "company-alpha");
            await _factory.EnsureTenantExistsAsync(companyBId, "Company Beta", "company-beta");

            // User belongs to Company A
            var (clientA, _) = await _factory.CreateAuthenticatedClientAsync("SalesExecutive", tenantId: companyAId);

            // Attempt to switch / spoof Company B via X-Tenant-ID header
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/leads");
            request.Headers.Add("X-Tenant-ID", companyBId.ToString());

            var response = await clientA.SendAsync(request);

            // Assert: Middleware must intercept and block with 403 Forbidden
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            var content = await response.Content.ReadAsStringAsync();
            Assert.Contains("Forbidden: You do not have active membership in the specified company.", content);
        }

        [Fact]
        public async Task TC_HTTP_010_CompanyA_Cannot_Read_CompanyB_Lead_Via_Api()
        {
            var companyAId = Guid.NewGuid();
            var companyBId = Guid.NewGuid();

            await _factory.EnsureTenantExistsAsync(companyAId, "Company Alpha", "company-alpha");
            await _factory.EnsureTenantExistsAsync(companyBId, "Company Beta", "company-beta");

            int bLeadId = 0;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                db.IgnoreTenantFilter = true;
                var leadB = new AI_Powered_CRM.Models.Entities.Lead
                {
                    TenantId = companyBId,
                    Name = "Secret Beta Client",
                    Phone = "+91 99999 11111",
                    Email = "secret@beta.com",
                    Status = AI_Powered_CRM.Models.Enums.LeadStatus.New
                };
                db.Leads.Add(leadB);
                await db.SaveChangesAsync();
                bLeadId = leadB.LeadId;
            }

            // User belongs to Company A
            var (clientA, _) = await _factory.CreateAuthenticatedClientAsync("SalesExecutive", tenantId: companyAId);

            // Company A attempts to GET Company B's lead by ID
            var response = await clientA.GetAsync($"/api/v1/leads/{bLeadId}");

            // Assert: Must return 404 NotFound due to tenant isolation filter
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task TC_HTTP_011_Anonymous_User_Cannot_Set_Tenant_Via_Header()
        {
            var companyAId = Guid.NewGuid();
            await _factory.EnsureTenantExistsAsync(companyAId, "Company Alpha", "company-alpha");

            var anonymousClient = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });

            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/leads");
            request.Headers.Add("X-Tenant-ID", companyAId.ToString());

            var response = await anonymousClient.SendAsync(request);

            // Assert: Must return 401 Unauthorized (anonymous client cannot bypass auth using headers)
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task TC_HTTP_012_PlatformSuperAdmin_Without_Support_Ticket_Denied_With_403()
        {
            var companyBId = Guid.NewGuid();
            await _factory.EnsureTenantExistsAsync(companyBId, "Company Beta", "company-beta");

            var (adminClient, _) = await _factory.CreateAuthenticatedClientAsync("PlatformSuperAdmin");

            // SuperAdmin passes X-Tenant-ID WITHOUT X-Support-Ticket header
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/leads");
            request.Headers.Add("X-Tenant-ID", companyBId.ToString());

            var response = await adminClient.SendAsync(request);

            // Assert: Must be rejected with 403 Forbidden because support ticket is required
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            var content = await response.Content.ReadAsStringAsync();
            Assert.Contains("Platform SuperAdmin delegated tenant access requires an approved server-side support ticket", content);
        }

        [Fact]
        public async Task TC_HTTP_012B_PlatformSuperAdmin_With_Unapproved_Or_Forged_Ticket_Denied_With_403()
        {
            var companyBId = Guid.NewGuid();
            await _factory.EnsureTenantExistsAsync(companyBId, "Company Beta", "company-beta");

            var (adminClient, _) = await _factory.CreateAuthenticatedClientAsync("PlatformSuperAdmin");

            // SuperAdmin passes self-declared ticket that does not exist in server database
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/leads");
            request.Headers.Add("X-Tenant-ID", companyBId.ToString());
            request.Headers.Add("X-Support-Ticket", "FORGED-TICKET-9999");

            var response = await adminClient.SendAsync(request);

            // Assert: Rejected with 403 Forbidden because no active server-side ticket exists
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            var content = await response.Content.ReadAsStringAsync();
            Assert.Contains("No active, server-approved support ticket found for this tenant and administrator.", content);
        }

        [Fact]
        public async Task TC_HTTP_013_PlatformSuperAdmin_With_Approved_Support_Ticket_Authorized_And_Audited_200()
        {
            var companyBId = Guid.NewGuid();
            await _factory.EnsureTenantExistsAsync(companyBId, "Company Beta", "company-beta");

            var (adminClient, user) = await _factory.CreateAuthenticatedClientAsync("PlatformSuperAdmin");

            // Create active, approved server-side support ticket in database
            await _factory.CreateSupportTicketAsync(
                companyBId, 
                "TICKET-4482", 
                "Customer reported dashboard synchronization delay", 
                superAdminUserId: user.Id, 
                isApproved: true);

            // SuperAdmin passes X-Tenant-ID WITH verified X-Support-Ticket
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/leads");
            request.Headers.Add("X-Tenant-ID", companyBId.ToString());
            request.Headers.Add("X-Support-Ticket", "TICKET-4482");

            var response = await adminClient.SendAsync(request);

            // Assert: Server-verified support access is authorized
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // Verify permanent audit log entry was created
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var auditEntry = await db.AuditLogs.IgnoreQueryFilters()
                .FirstOrDefaultAsync(a => a.TenantId == companyBId && a.Action == "PlatformSuperAdminDelegatedAccess");

            Assert.NotNull(auditEntry);
            Assert.Contains("TICKET-4482", auditEntry.DetailsJson);
            Assert.Contains("Customer reported dashboard synchronization delay", auditEntry.DetailsJson);
        }

        [Fact]
        public async Task TC_HTTP_014_Inactive_Membership_Denied_With_403()
        {
            var companyAId = Guid.NewGuid();
            await _factory.EnsureTenantExistsAsync(companyAId, "Company Alpha", "company-alpha");

            // Create client whose membership in Company Alpha is deactivated
            var (inactiveClient, _) = await _factory.CreateAuthenticatedClientAsync(
                "SalesExecutive", tenantId: companyAId, isMembershipActive: false);

            var response = await inactiveClient.GetAsync("/api/v1/leads");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            var content = await response.Content.ReadAsStringAsync();
            Assert.Contains("Forbidden: Your organization membership has been deactivated.", content);
        }

        [Fact]
        public async Task TC_HTTP_015_Suspended_Tenant_Denied_With_403()
        {
            var suspendedTenantId = Guid.NewGuid();
            await _factory.EnsureTenantExistsAsync(suspendedTenantId, "Suspended Corp", "suspended-corp", isTenantActive: false);

            var (client, _) = await _factory.CreateAuthenticatedClientAsync(
                "SalesExecutive", tenantId: suspendedTenantId, isMembershipActive: true, isTenantActive: false);

            var response = await client.GetAsync("/api/v1/leads");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            var content = await response.Content.ReadAsStringAsync();
            Assert.Contains("Forbidden: Your organization account is currently inactive or suspended.", content);
        }

        [Fact]
        public async Task TC_HTTP_016_CompanyA_Cannot_Update_CompanyB_Lead_Via_Api()
        {
            var companyAId = Guid.NewGuid();
            var companyBId = Guid.NewGuid();

            await _factory.EnsureTenantExistsAsync(companyAId, "Company Alpha", "company-alpha");
            await _factory.EnsureTenantExistsAsync(companyBId, "Company Beta", "company-beta");

            int bLeadId = 0;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                db.IgnoreTenantFilter = true;
                var leadB = new AI_Powered_CRM.Models.Entities.Lead
                {
                    TenantId = companyBId,
                    Name = "Beta Enterprise Account",
                    Phone = "+91 99999 22222",
                    Email = "lead@beta.com",
                    Status = AI_Powered_CRM.Models.Enums.LeadStatus.New
                };
                db.Leads.Add(leadB);
                await db.SaveChangesAsync();
                bLeadId = leadB.LeadId;
            }

            var (clientA, _) = await _factory.CreateAuthenticatedClientAsync("CompanyAdmin", tenantId: companyAId);

            var updateDto = new AI_Powered_CRM.DTOs.UpdateLeadDto
            {
                LeadId = bLeadId,
                Name = "Tampered Account Name",
                Phone = "+91 99999 22222",
                Source = "Website",
                InterestedProduct = "Enterprise Cloud",
                Status = AI_Powered_CRM.Models.Enums.LeadStatus.Contacted
            };

            var jsonContent = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(updateDto),
                System.Text.Encoding.UTF8,
                "application/json");

            var response = await clientA.PutAsync($"/api/v1/leads/{bLeadId}", jsonContent);

            // Assert: Must return 403 Forbidden because the lead belongs to another tenant and is not accessible
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task TC_HTTP_017_CompanyA_Cannot_Access_CompanyB_Reports_Via_Header_Spoofing()
        {
            var companyAId = Guid.NewGuid();
            var companyBId = Guid.NewGuid();

            await _factory.EnsureTenantExistsAsync(companyAId, "Company Alpha", "company-alpha");
            await _factory.EnsureTenantExistsAsync(companyBId, "Company Beta", "company-beta");

            var (adminClientA, _) = await _factory.CreateAuthenticatedClientAsync("CompanyAdmin", tenantId: companyAId);

            // Attempt to read Company B conversion report via X-Tenant-ID header
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/reports/conversion");
            request.Headers.Add("X-Tenant-ID", companyBId.ToString());

            var response = await adminClientA.SendAsync(request);

            // Assert: Middleware intercepts unauthorized tenant header and returns 403 Forbidden
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            var content = await response.Content.ReadAsStringAsync();
            Assert.Contains("Forbidden: You do not have active membership in the specified company.", content);
        }

        [Fact]
        public async Task TC_HTTP_018_PlatformSuperAdmin_Audit_Failure_Fails_Closed_With_500()
        {
            var companyBId = Guid.NewGuid();
            await _factory.EnsureTenantExistsAsync(companyBId, "Company Beta", "company-beta");

            var (adminClient, user) = await _factory.CreateAuthenticatedClientAsync("PlatformSuperAdmin");

            await _factory.CreateSupportTicketAsync(
                companyBId, 
                "TICKET-FAIL-500", 
                "Support session testing audit outage handling", 
                superAdminUserId: user.Id, 
                isApproved: true);

            try
            {
                // Simulate database audit storage failure
                _factory.SimulateAuditFailure = true;

                using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/leads");
                request.Headers.Add("X-Tenant-ID", companyBId.ToString());
                request.Headers.Add("X-Support-Ticket", "TICKET-FAIL-500");

                var response = await adminClient.SendAsync(request);

                // Assert: Must fail closed with 500 InternalServerError
                Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
                var content = await response.Content.ReadAsStringAsync();
                Assert.Contains("Security Alert: Delegated support access cannot proceed because mandatory security audit logging failed.", content);
            }
            finally
            {
                _factory.SimulateAuditFailure = false;
            }
        }

        [Fact]
        public async Task TC_HTTP_019_Pending_SupportTicket_Denied_With_403()
        {
            var companyBId = Guid.NewGuid();
            await _factory.EnsureTenantExistsAsync(companyBId, "Company Beta", "company-beta");

            var (adminClient, user) = await _factory.CreateAuthenticatedClientAsync("PlatformSuperAdmin");

            // Ticket is created but still in Pending status
            await _factory.CreateSupportTicketAsync(
                companyBId, 
                "TICKET-PENDING-001", 
                "Pending support investigation", 
                superAdminUserId: user.Id, 
                isApproved: false);

            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/leads");
            request.Headers.Add("X-Tenant-ID", companyBId.ToString());
            request.Headers.Add("X-Support-Ticket", "TICKET-PENDING-001");

            var response = await adminClient.SendAsync(request);

            // Assert: Pending ticket must be rejected with 403 Forbidden
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            var content = await response.Content.ReadAsStringAsync();
            Assert.Contains("Forbidden: No active, server-approved support ticket found for this tenant and administrator.", content);
        }

        [Fact]
        public async Task TC_HTTP_020_Expired_SupportTicket_Denied_With_403()
        {
            var companyBId = Guid.NewGuid();
            await _factory.EnsureTenantExistsAsync(companyBId, "Company Beta", "company-beta");

            var (adminClient, user) = await _factory.CreateAuthenticatedClientAsync("PlatformSuperAdmin");

            // Ticket was approved but has expired
            await _factory.CreateSupportTicketAsync(
                companyBId, 
                "TICKET-EXPIRED-001", 
                "Expired support ticket", 
                superAdminUserId: user.Id, 
                isApproved: true,
                validity: TimeSpan.FromMinutes(-15));

            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/leads");
            request.Headers.Add("X-Tenant-ID", companyBId.ToString());
            request.Headers.Add("X-Support-Ticket", "TICKET-EXPIRED-001");

            var response = await adminClient.SendAsync(request);

            // Assert: Expired ticket must be rejected with 403 Forbidden
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            var content = await response.Content.ReadAsStringAsync();
            Assert.Contains("Forbidden: No active, server-approved support ticket found for this tenant and administrator.", content);
        }

        [Fact]
        public async Task TC_HTTP_021_Mismatched_Or_Unassigned_SuperAdmin_Denied_With_403()
        {
            var companyBId = Guid.NewGuid();
            await _factory.EnsureTenantExistsAsync(companyBId, "Company Beta", "company-beta");

            var (adminClient, _) = await _factory.CreateAuthenticatedClientAsync("PlatformSuperAdmin");

            // Ticket was approved for a different administrator
            await _factory.CreateSupportTicketAsync(
                companyBId, 
                "TICKET-OTHER-ADMIN", 
                "Assigned to different superadmin", 
                superAdminUserId: "different-admin-id", 
                isApproved: true);

            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/leads");
            request.Headers.Add("X-Tenant-ID", companyBId.ToString());
            request.Headers.Add("X-Support-Ticket", "TICKET-OTHER-ADMIN");

            var response = await adminClient.SendAsync(request);

            // Assert: Mismatched superadmin must be rejected with 403 Forbidden
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            var content = await response.Content.ReadAsStringAsync();
            Assert.Contains("Forbidden: No active, server-approved support ticket found for this tenant and administrator.", content);
        }
    }
}
