using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AI_Powered_CRM.Data;
using AI_Powered_CRM.Models;
using AI_Powered_CRM.Models.Entities;
using AI_Powered_CRM.Models.Enums;
using AI_Powered_CRM.Security;
using AI_Powered_CRM.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AIPoweredCRM.Tests
{
    public class SupportAccessServiceTests : IClassFixture<CrmWebApplicationFactory>
    {
        private readonly CrmWebApplicationFactory _factory;

        public SupportAccessServiceTests(CrmWebApplicationFactory factory)
        {
            _factory = factory;
        }

        private static HttpContext CreateHttpContext(ApplicationUser user, string role)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Name, user.UserName ?? "testuser"),
                new Claim(ClaimTypes.Role, role)
            };
            return new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
            };
        }

        private async Task<(ApplicationUser superAdmin, ApplicationUser requester, ApplicationUser tenantAdmin, Guid tenantId)> SetupTestUsersAndTenantAsync()
        {
            using var scope = _factory.Services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            if (!await roleManager.RoleExistsAsync("SuperAdmin"))
                await roleManager.CreateAsync(new IdentityRole("SuperAdmin"));
            if (!await roleManager.RoleExistsAsync("CompanyAdmin"))
                await roleManager.CreateAsync(new IdentityRole("CompanyAdmin"));
            if (!await roleManager.RoleExistsAsync("SalesExecutive"))
                await roleManager.CreateAsync(new IdentityRole("SalesExecutive"));

            var tenantId = Guid.NewGuid();
            await _factory.EnsureTenantExistsAsync(tenantId, "Test Target Tenant", "test-target-tenant");

            var superAdmin = new ApplicationUser
            {
                UserName = $"superadmin_{Guid.NewGuid():N}@platform.local",
                Email = $"superadmin_{Guid.NewGuid():N}@platform.local",
                FullName = "Platform SuperAdmin",
                IsActive = true
            };
            await userManager.CreateAsync(superAdmin, "AdminPassword!123");
            await userManager.AddToRoleAsync(superAdmin, "SuperAdmin");

            var requester = new ApplicationUser
            {
                UserName = $"requester_{Guid.NewGuid():N}@tenant.local",
                Email = $"requester_{Guid.NewGuid():N}@tenant.local",
                FullName = "Support Requester",
                IsActive = true
            };
            await userManager.CreateAsync(requester, "UserPassword!123");
            await userManager.AddToRoleAsync(requester, "CompanyAdmin");

            var tenantAdmin = new ApplicationUser
            {
                UserName = $"tenantadmin_{Guid.NewGuid():N}@tenant.local",
                Email = $"tenantadmin_{Guid.NewGuid():N}@tenant.local",
                FullName = "Independent Tenant Admin",
                IsActive = true
            };
            await userManager.CreateAsync(tenantAdmin, "UserPassword!123");
            await userManager.AddToRoleAsync(tenantAdmin, "CompanyAdmin");

            // Seed active tenant memberships
            db.IgnoreTenantFilter = true;
            db.TenantMemberships.Add(new TenantMembership
            {
                MembershipId = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = requester.Id,
                CompanyRole = CompanyRole.CompanyAdmin,
                IsActive = true,
                JoinedAt = DateTime.UtcNow
            });
            db.TenantMemberships.Add(new TenantMembership
            {
                MembershipId = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = tenantAdmin.Id,
                CompanyRole = CompanyRole.CompanyAdmin,
                IsActive = true,
                JoinedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            return (superAdmin, requester, tenantAdmin, tenantId);
        }

        [Fact]
        public async Task TC_SUPPORT_001_CreateTicket_DefaultsToPending_WithAssignedSuperAdmin()
        {
            var (superAdmin, requester, _, tenantId) = await SetupTestUsersAndTenantAsync();

            using var scope = _factory.Services.CreateScope();
            var httpContextAccessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            httpContextAccessor.HttpContext = CreateHttpContext(requester, "CompanyAdmin");

            var service = scope.ServiceProvider.GetRequiredService<ISupportAccessService>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var ticket = await service.CreateTicketAsync(
                tenantId,
                superAdmin.Id,
                "Investigating customer report for latency on dashboard");

            Assert.NotNull(ticket);
            Assert.Equal("Pending", ticket.Status);
            Assert.False(ticket.IsActive);
            Assert.Equal(superAdmin.Id, ticket.SuperAdminUserId);
            Assert.Equal(requester.Id, ticket.CreatedByUserId);
            Assert.StartsWith("TKT-", ticket.TicketNumber);

            // Verify persisted in DB with audit
            db.IgnoreTenantFilter = true;
            var persisted = await db.SupportAccessTickets.FirstOrDefaultAsync(t => t.TicketId == ticket.TicketId);
            Assert.NotNull(persisted);
            Assert.Equal("Pending", persisted.Status);

            var audit = await db.AuditLogs.FirstOrDefaultAsync(a => a.EntityId == ticket.TicketId.ToString() && a.Action == "SupportTicketCreated");
            Assert.NotNull(audit);
        }

        [Fact]
        public async Task TC_SUPPORT_002_CreateTicket_UnassignedSuperAdmin_ThrowsArgumentException()
        {
            var (_, requester, _, tenantId) = await SetupTestUsersAndTenantAsync();

            using var scope = _factory.Services.CreateScope();
            var httpContextAccessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            httpContextAccessor.HttpContext = CreateHttpContext(requester, "CompanyAdmin");

            var service = scope.ServiceProvider.GetRequiredService<ISupportAccessService>();

            await Assert.ThrowsAsync<ArgumentException>(() =>
                service.CreateTicketAsync(tenantId, "", "Test reason"));

            await Assert.ThrowsAsync<ArgumentException>(() =>
                service.CreateTicketAsync(tenantId, "   ", "Test reason"));
        }

        [Fact]
        public async Task TC_SUPPORT_003_SelfApproval_ByDesignatedSuperAdmin_StrictlyForbidden()
        {
            var (superAdmin, requester, _, tenantId) = await SetupTestUsersAndTenantAsync();

            using var scope = _factory.Services.CreateScope();
            var httpContextAccessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            httpContextAccessor.HttpContext = CreateHttpContext(requester, "CompanyAdmin");

            var service = scope.ServiceProvider.GetRequiredService<ISupportAccessService>();

            var ticket = await service.CreateTicketAsync(
                tenantId,
                superAdmin.Id,
                "Emergency access request");

            // Switch authenticated context to the assigned SuperAdmin
            httpContextAccessor.HttpContext = CreateHttpContext(superAdmin, "SuperAdmin");

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ApproveTicketAsync(ticket.TicketId, TimeSpan.FromHours(2)));

            Assert.Contains("Self-approval is strictly forbidden", ex.Message);
        }

        [Fact]
        public async Task TC_SUPPORT_004_SelfApproval_ByRequester_StrictlyForbidden()
        {
            var (superAdmin, requester, _, tenantId) = await SetupTestUsersAndTenantAsync();

            using var scope = _factory.Services.CreateScope();
            var httpContextAccessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            httpContextAccessor.HttpContext = CreateHttpContext(requester, "CompanyAdmin");

            var service = scope.ServiceProvider.GetRequiredService<ISupportAccessService>();

            var ticket = await service.CreateTicketAsync(
                tenantId,
                superAdmin.Id,
                "Support diagnostics");

            // Requester attempts self-approval
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ApproveTicketAsync(ticket.TicketId, TimeSpan.FromHours(2)));

            Assert.Contains("Self-approval is strictly forbidden", ex.Message);
        }

        [Fact]
        public async Task TC_SUPPORT_005_IndependentApproval_Succeeds_And_ActivatesTicket()
        {
            var (superAdmin, requester, tenantAdmin, tenantId) = await SetupTestUsersAndTenantAsync();

            using var scope = _factory.Services.CreateScope();
            var httpContextAccessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            httpContextAccessor.HttpContext = CreateHttpContext(requester, "CompanyAdmin");

            var service = scope.ServiceProvider.GetRequiredService<ISupportAccessService>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var ticket = await service.CreateTicketAsync(
                tenantId,
                superAdmin.Id,
                "Authorized debugging session");

            // Switch to independent tenant admin
            httpContextAccessor.HttpContext = CreateHttpContext(tenantAdmin, "CompanyAdmin");
            var approved = await service.ApproveTicketAsync(ticket.TicketId, TimeSpan.FromHours(3));

            Assert.Equal("Approved", approved.Status);
            Assert.Equal(tenantAdmin.Id, approved.ApprovedByUserId);
            Assert.NotNull(approved.ApprovedAt);
            Assert.True(approved.IsActive);

            // Check audit log
            db.IgnoreTenantFilter = true;
            var audit = await db.AuditLogs.FirstOrDefaultAsync(a =>
                a.EntityId == ticket.TicketId.ToString() && a.Action == "SupportTicketApproved");

            Assert.NotNull(audit);
            Assert.Equal(tenantAdmin.Id, audit.UserId);
        }

        [Fact]
        public async Task TC_SUPPORT_006_Approval_AuditFailure_FailsClosed_AndRollsBackTicket()
        {
            var (superAdmin, requester, tenantAdmin, tenantId) = await SetupTestUsersAndTenantAsync();

            using var scope = _factory.Services.CreateScope();
            var httpContextAccessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            httpContextAccessor.HttpContext = CreateHttpContext(requester, "CompanyAdmin");

            var service = scope.ServiceProvider.GetRequiredService<ISupportAccessService>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var ticket = await service.CreateTicketAsync(
                tenantId,
                superAdmin.Id,
                "Session testing audit failure in approval");

            httpContextAccessor.HttpContext = CreateHttpContext(tenantAdmin, "CompanyAdmin");

            try
            {
                _factory.SimulateAuditFailure = true;

                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    service.ApproveTicketAsync(ticket.TicketId, TimeSpan.FromHours(2)));

                Assert.Contains("audit", ex.Message, StringComparison.OrdinalIgnoreCase);

                // Verify in-memory ticket entity DID NOT remain approved
                Assert.Equal("Pending", ticket.Status);
                Assert.Null(ticket.ApprovedByUserId);
                Assert.False(ticket.IsActive);

                // Verify database row is still Pending
                db.IgnoreTenantFilter = true;
                var dbTicket = await db.SupportAccessTickets.AsNoTracking().FirstOrDefaultAsync(t => t.TicketId == ticket.TicketId);
                Assert.NotNull(dbTicket);
                Assert.Equal("Pending", dbTicket.Status);
                Assert.Null(dbTicket.ApprovedByUserId);
            }
            finally
            {
                _factory.SimulateAuditFailure = false;
            }
        }

        [Fact]
        public async Task TC_SUPPORT_007_RevokeTicket_DeactivatesTicket()
        {
            var (superAdmin, requester, tenantAdmin, tenantId) = await SetupTestUsersAndTenantAsync();

            using var scope = _factory.Services.CreateScope();
            var httpContextAccessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            httpContextAccessor.HttpContext = CreateHttpContext(requester, "CompanyAdmin");

            var service = scope.ServiceProvider.GetRequiredService<ISupportAccessService>();

            var ticket = await service.CreateTicketAsync(
                tenantId,
                superAdmin.Id,
                "Temporary access");

            httpContextAccessor.HttpContext = CreateHttpContext(tenantAdmin, "CompanyAdmin");
            await service.ApproveTicketAsync(ticket.TicketId, TimeSpan.FromHours(1));

            var revoked = await service.RevokeTicketAsync(ticket.TicketId, "Maintenance completed");

            Assert.Equal("Revoked", revoked.Status);
            Assert.False(revoked.IsActive);
        }

        [Fact]
        public async Task TC_SUPPORT_008_UnauthorizedUser_SalesExecutive_CannotApproveTicket()
        {
            var (superAdmin, requester, _, tenantId) = await SetupTestUsersAndTenantAsync();

            using var scope = _factory.Services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var httpContextAccessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            var service = scope.ServiceProvider.GetRequiredService<ISupportAccessService>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var salesUser = new ApplicationUser
            {
                UserName = $"sales_{Guid.NewGuid():N}@tenant.local",
                Email = $"sales_{Guid.NewGuid():N}@tenant.local",
                FullName = "Sales Exec",
                IsActive = true
            };
            await userManager.CreateAsync(salesUser, "UserPass!123");
            await userManager.AddToRoleAsync(salesUser, "SalesExecutive");

            db.IgnoreTenantFilter = true;
            db.TenantMemberships.Add(new TenantMembership
            {
                MembershipId = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = salesUser.Id,
                CompanyRole = CompanyRole.SalesExecutive,
                IsActive = true,
                JoinedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            httpContextAccessor.HttpContext = CreateHttpContext(requester, "CompanyAdmin");
            var ticket = await service.CreateTicketAsync(
                tenantId,
                superAdmin.Id,
                "Unauthorized approval test");

            // Attempt approval by regular sales executive (lacks CompanyAdmin/SuperAdmin)
            httpContextAccessor.HttpContext = CreateHttpContext(salesUser, "SalesExecutive");
            var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                service.ApproveTicketAsync(ticket.TicketId, TimeSpan.FromHours(2)));

            Assert.Contains("not authorized to approve support tickets", ex.Message);
        }

        [Fact]
        public async Task TC_SUPPORT_009_InactiveUser_CannotApproveTicket()
        {
            var (superAdmin, requester, tenantAdmin, tenantId) = await SetupTestUsersAndTenantAsync();

            using var scope = _factory.Services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var httpContextAccessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            var service = scope.ServiceProvider.GetRequiredService<ISupportAccessService>();

            var userToDeactivate = await userManager.FindByIdAsync(tenantAdmin.Id);
            Assert.NotNull(userToDeactivate);
            userToDeactivate.IsActive = false;
            await userManager.UpdateAsync(userToDeactivate);

            httpContextAccessor.HttpContext = CreateHttpContext(requester, "CompanyAdmin");
            var ticket = await service.CreateTicketAsync(
                tenantId,
                superAdmin.Id,
                "Inactive approver test");

            httpContextAccessor.HttpContext = CreateHttpContext(userToDeactivate, "CompanyAdmin");
            var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                service.ApproveTicketAsync(ticket.TicketId, TimeSpan.FromHours(2)));

            Assert.Contains("inactive or suspended", ex.Message);
        }

        [Fact]
        public async Task TC_SUPPORT_010_CrossTenant_Admin_CannotApproveTicket()
        {
            var (superAdmin, requester, _, tenantId) = await SetupTestUsersAndTenantAsync();

            var companyBId = Guid.NewGuid();
            await _factory.EnsureTenantExistsAsync(companyBId, "Company B", "company-b");

            using var scope = _factory.Services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var httpContextAccessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            var service = scope.ServiceProvider.GetRequiredService<ISupportAccessService>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var foreignAdmin = new ApplicationUser
            {
                UserName = $"foreign_admin_{Guid.NewGuid():N}@companyb.local",
                Email = $"foreign_admin_{Guid.NewGuid():N}@companyb.local",
                FullName = "Foreign Admin",
                IsActive = true
            };
            await userManager.CreateAsync(foreignAdmin, "Pass!12345");
            await userManager.AddToRoleAsync(foreignAdmin, "CompanyAdmin");

            db.IgnoreTenantFilter = true;
            db.TenantMemberships.Add(new TenantMembership
            {
                MembershipId = Guid.NewGuid(),
                TenantId = companyBId,
                UserId = foreignAdmin.Id,
                CompanyRole = CompanyRole.CompanyAdmin,
                IsActive = true,
                JoinedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            httpContextAccessor.HttpContext = CreateHttpContext(requester, "CompanyAdmin");
            var ticket = await service.CreateTicketAsync(
                tenantId,
                superAdmin.Id,
                "Cross tenant approval prevention");

            // Attempt approval by Company B admin on Company A's ticket
            httpContextAccessor.HttpContext = CreateHttpContext(foreignAdmin, "CompanyAdmin");
            var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                service.ApproveTicketAsync(ticket.TicketId, TimeSpan.FromHours(2)));

            Assert.Contains("not authorized to approve support tickets for tenant", ex.Message);
        }

        [Fact]
        public void TC_SUPPORT_011_ExpiredTicket_CannotBeUsedForAccess()
        {
            var ticket = new SupportAccessTicket
            {
                Status = "Approved",
                SuperAdminUserId = "admin-1",
                ExpiresAt = DateTime.UtcNow.AddMinutes(-5)
            };

            Assert.False(ticket.IsActive);
        }

        [Fact]
        public async Task TC_SUPPORT_012_ConcurrentApproval_ExactlyOneSucceeds()
        {
            var (superAdmin, requester, tenantAdmin, tenantId) = await SetupTestUsersAndTenantAsync();

            using var scope = _factory.Services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var httpContextAccessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            var service = scope.ServiceProvider.GetRequiredService<ISupportAccessService>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var tenantAdmin2 = new ApplicationUser
            {
                UserName = $"tenantadmin2_{Guid.NewGuid():N}@tenant.local",
                Email = $"tenantadmin2_{Guid.NewGuid():N}@tenant.local",
                FullName = "Second Tenant Admin",
                IsActive = true
            };
            await userManager.CreateAsync(tenantAdmin2, "UserPass!123");
            await userManager.AddToRoleAsync(tenantAdmin2, "CompanyAdmin");

            db.IgnoreTenantFilter = true;
            db.TenantMemberships.Add(new TenantMembership
            {
                MembershipId = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = tenantAdmin2.Id,
                CompanyRole = CompanyRole.CompanyAdmin,
                IsActive = true,
                JoinedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            httpContextAccessor.HttpContext = CreateHttpContext(requester, "CompanyAdmin");
            var ticket = await service.CreateTicketAsync(
                tenantId,
                superAdmin.Id,
                "Concurrency race check");

            // Run concurrent approvals with distinct authenticated scopes
            var task1 = Task.Run(async () =>
            {
                using var s1 = _factory.Services.CreateScope();
                var h1 = s1.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
                h1.HttpContext = CreateHttpContext(tenantAdmin, "CompanyAdmin");
                var svc1 = s1.ServiceProvider.GetRequiredService<ISupportAccessService>();
                return await svc1.ApproveTicketAsync(ticket.TicketId, TimeSpan.FromHours(2));
            });

            var task2 = Task.Run(async () =>
            {
                using var s2 = _factory.Services.CreateScope();
                var h2 = s2.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
                h2.HttpContext = CreateHttpContext(tenantAdmin2, "CompanyAdmin");
                var svc2 = s2.ServiceProvider.GetRequiredService<ISupportAccessService>();
                return await svc2.ApproveTicketAsync(ticket.TicketId, TimeSpan.FromHours(2));
            });

            var results = await Task.WhenAll(
                task1.ContinueWith(t => t.IsCompletedSuccessfully),
                task2.ContinueWith(t => t.IsCompletedSuccessfully));

            // Assert: EXACTLY one concurrent approval succeeds and the other fails appropriately
            var successCount = (results[0] ? 1 : 0) + (results[1] ? 1 : 0);
            Assert.Equal(1, successCount);

            // Assert: The losing task encountered an InvalidOperationException or DbUpdateConcurrencyException
            Exception? failureEx = results[0] ? task2.Exception?.InnerException ?? task2.Exception : task1.Exception?.InnerException ?? task1.Exception;
            Assert.NotNull(failureEx);
            Assert.True(failureEx is InvalidOperationException || failureEx is DbUpdateConcurrencyException);

            // Assert: Database ticket is approved by whichever admin succeeded
            using var verifyScope = _factory.Services.CreateScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            verifyDb.IgnoreTenantFilter = true;
            var finalTicket = await verifyDb.SupportAccessTickets.AsNoTracking().FirstOrDefaultAsync(t => t.TicketId == ticket.TicketId);
            Assert.NotNull(finalTicket);
            Assert.Equal("Approved", finalTicket.Status);
            Assert.True(finalTicket.ApprovedByUserId == tenantAdmin.Id || finalTicket.ApprovedByUserId == tenantAdmin2.Id);
        }

        [Fact]
        public async Task TC_SUPPORT_013_ServerSideAuthentication_IdentityMismatch_ThrowsSecurityException()
        {
            var (superAdmin, requester, tenantAdmin, tenantId) = await SetupTestUsersAndTenantAsync();

            using var scope = _factory.Services.CreateScope();
            var httpContextAccessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            httpContextAccessor.HttpContext = CreateHttpContext(requester, "CompanyAdmin");

            var service = scope.ServiceProvider.GetRequiredService<ISupportAccessService>();

            var ticket = await service.CreateTicketAsync(
                tenantId,
                superAdmin.Id,
                "Identity mismatch test");

            httpContextAccessor.HttpContext = CreateHttpContext(tenantAdmin, "CompanyAdmin");

            // Client attempts to assert a different user ID than the server-authenticated principal
            var ex = await Assert.ThrowsAsync<System.Security.SecurityException>(() =>
                service.ApproveTicketAsync(ticket.TicketId, "spoofed-user-id", TimeSpan.FromHours(1)));

            Assert.Contains("Identity mismatch", ex.Message);
        }

        [Fact]
        public async Task TC_SUPPORT_014_Creation_AuditFailure_FailsClosed_AndRollsBack()
        {
            var (superAdmin, requester, _, tenantId) = await SetupTestUsersAndTenantAsync();

            using var scope = _factory.Services.CreateScope();
            var httpContextAccessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            httpContextAccessor.HttpContext = CreateHttpContext(requester, "CompanyAdmin");

            var service = scope.ServiceProvider.GetRequiredService<ISupportAccessService>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            try
            {
                _factory.SimulateAuditFailure = true;

                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    service.CreateTicketAsync(tenantId, superAdmin.Id, "Testing creation failure"));

                Assert.Contains("audit", ex.Message, StringComparison.OrdinalIgnoreCase);

                // Verify ticket was NOT persisted
                db.IgnoreTenantFilter = true;
                var count = await db.SupportAccessTickets.CountAsync(t => t.TenantId == tenantId && t.Reason == "Testing creation failure");
                Assert.Equal(0, count);
            }
            finally
            {
                _factory.SimulateAuditFailure = false;
            }
        }

        [Fact]
        public async Task TC_SUPPORT_015_Revocation_AuditFailure_FailsClosed_AndRollsBack()
        {
            var (superAdmin, requester, tenantAdmin, tenantId) = await SetupTestUsersAndTenantAsync();

            using var scope = _factory.Services.CreateScope();
            var httpContextAccessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            httpContextAccessor.HttpContext = CreateHttpContext(requester, "CompanyAdmin");

            var service = scope.ServiceProvider.GetRequiredService<ISupportAccessService>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var ticket = await service.CreateTicketAsync(
                tenantId,
                superAdmin.Id,
                "Testing revocation failure");

            httpContextAccessor.HttpContext = CreateHttpContext(tenantAdmin, "CompanyAdmin");
            await service.ApproveTicketAsync(ticket.TicketId, TimeSpan.FromHours(1));

            try
            {
                _factory.SimulateAuditFailure = true;

                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    service.RevokeTicketAsync(ticket.TicketId, "Emergency rollback"));

                Assert.Contains("audit", ex.Message, StringComparison.OrdinalIgnoreCase);

                // Verify ticket remains Approved in DB and did not remain Revoked
                db.IgnoreTenantFilter = true;
                var dbTicket = await db.SupportAccessTickets.AsNoTracking().FirstOrDefaultAsync(t => t.TicketId == ticket.TicketId);
                Assert.NotNull(dbTicket);
                Assert.Equal("Approved", dbTicket.Status);
            }
            finally
            {
                _factory.SimulateAuditFailure = false;
            }
        }

        [Fact]
        public async Task TC_SUPPORT_016_UnauthenticatedCaller_WithoutSystemScope_ThrowsUnauthorizedAccessException()
        {
            var (superAdmin, _, _, tenantId) = await SetupTestUsersAndTenantAsync();

            using var scope = _factory.Services.CreateScope();
            var httpContextAccessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            httpContextAccessor.HttpContext = new DefaultHttpContext(); // No authenticated user

            var service = scope.ServiceProvider.GetRequiredService<ISupportAccessService>();

            // Attempting operation without authenticated user or system scope is strictly rejected
            var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                service.CreateTicketAsync(tenantId, superAdmin.Id, "Unauthenticated ticket attempt"));

            Assert.Contains("Authentication required", ex.Message);
        }

        [Fact]
        public async Task TC_SUPPORT_017_SystemExecutionScope_EnablesAuthorizedAutomatedOperations()
        {
            var (superAdmin, requester, tenantAdmin, tenantId) = await SetupTestUsersAndTenantAsync();

            using var scope = _factory.Services.CreateScope();
            var httpContextAccessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            httpContextAccessor.HttpContext = CreateHttpContext(requester, "CompanyAdmin");

            var service = scope.ServiceProvider.GetRequiredService<ISupportAccessService>();
            var systemSecurity = scope.ServiceProvider.GetRequiredService<ISystemSecurityContext>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var ticket = await service.CreateTicketAsync(
                tenantId,
                superAdmin.Id,
                "Ticket for system cleanup");

            httpContextAccessor.HttpContext = CreateHttpContext(tenantAdmin, "CompanyAdmin");
            await service.ApproveTicketAsync(ticket.TicketId, TimeSpan.FromHours(1));

            // Clear HTTP context to simulate a background job
            httpContextAccessor.HttpContext = null;

            // System automated job revokes expired ticket within an authorized system scope
            using (systemSecurity.BeginSystemScope("AutomatedTicketExpiryBackgroundJob"))
            {
                var revokedTicket = await service.RevokeTicketAsync(ticket.TicketId, "Automated expiry revocation");
                Assert.Equal("Revoked", revokedTicket.Status);

                db.IgnoreTenantFilter = true;
                var audit = await db.AuditLogs.FirstOrDefaultAsync(a =>
                    a.EntityId == ticket.TicketId.ToString() && a.Action == "SupportTicketRevoked");
                Assert.NotNull(audit);
                Assert.Contains("SYSTEM:AutomatedTicketExpiryBackgroundJob", audit.DetailsJson);
            }
        }

        [Fact]
        public async Task TC_SUPPORT_018_SystemExecutionScope_CannotApproveTickets()
        {
            var (superAdmin, requester, _, tenantId) = await SetupTestUsersAndTenantAsync();

            using var scope = _factory.Services.CreateScope();
            var httpContextAccessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            httpContextAccessor.HttpContext = CreateHttpContext(requester, "CompanyAdmin");

            var service = scope.ServiceProvider.GetRequiredService<ISupportAccessService>();
            var systemSecurity = scope.ServiceProvider.GetRequiredService<ISystemSecurityContext>();

            var ticket = await service.CreateTicketAsync(
                tenantId,
                superAdmin.Id,
                "Ticket for system approval test");

            httpContextAccessor.HttpContext = null;

            // System execution context CANNOT approve delegated support access tickets
            using (systemSecurity.BeginSystemScope("MaliciousOrAutomatedApproval"))
            {
                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    service.ApproveTicketAsync(ticket.TicketId, TimeSpan.FromHours(1)));

                Assert.Contains("Automated system execution context cannot approve delegated support access tickets", ex.Message);
            }
        }
    }
}
