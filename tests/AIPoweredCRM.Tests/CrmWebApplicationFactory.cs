using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AI_Powered_CRM.Data;
using AI_Powered_CRM.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AIPoweredCRM.Tests
{
    public class CrmWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly string _dbName;
        public bool SimulateAuditFailure { get; set; } = false;

        public CrmWebApplicationFactory() : this(null) { }

        internal CrmWebApplicationFactory(string? dbName)
        {
            _dbName = dbName ?? "IntegrationDb_" + Guid.NewGuid().ToString("N");
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RateLimiting:LoginPermitLimit"] = "3",
                    ["RateLimiting:ApiPermitLimit"] = "100",
                    ["RateLimiting:GlobalPermitLimit"] = "200"
                });
            });

            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));
                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }

                services.AddDbContext<ApplicationDbContext>(options =>
                {
                    options.UseInMemoryDatabase(_dbName);
                    options.AddInterceptors(new TestAuditFailureInterceptor(this));
                });
            });
        }

        private class TestAuditFailureInterceptor : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
        {
            private readonly CrmWebApplicationFactory _factory;
            public TestAuditFailureInterceptor(CrmWebApplicationFactory factory) => _factory = factory;

            public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
                Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData, 
                Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result, 
                System.Threading.CancellationToken cancellationToken = default)
            {
                if (_factory.SimulateAuditFailure && eventData.Context != null)
                {
                    var auditEntries = eventData.Context.ChangeTracker.Entries<AI_Powered_CRM.Models.Entities.AuditLog>();
                    if (auditEntries.Any())
                    {
                        throw new InvalidOperationException("Simulated Audit Storage Outage / Disk Failure.");
                    }
                }
                return base.SavingChangesAsync(eventData, result, cancellationToken);
            }
        }

        public async Task EnsureTenantExistsAsync(Guid tenantId, string companyName, string slug, bool isTenantActive = true)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.IgnoreTenantFilter = true;
            var existing = await db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.TenantId == tenantId);
            if (existing == null)
            {
                db.Tenants.Add(new AI_Powered_CRM.Models.Entities.Tenant
                {
                    TenantId = tenantId,
                    CompanyName = companyName,
                    Slug = slug,
                    ContactEmail = $"{slug}@example.com",
                    DefaultCurrency = "INR",
                    SubscriptionTier = AI_Powered_CRM.Models.Enums.SubscriptionTier.Enterprise,
                    SubscriptionStatus = AI_Powered_CRM.Models.Enums.SubscriptionStatus.Active,
                    IsActive = isTenantActive
                });
                await db.SaveChangesAsync();
            }
            else if (existing.IsActive != isTenantActive)
            {
                existing.IsActive = isTenantActive;
                await db.SaveChangesAsync();
            }
        }

        public async Task<AI_Powered_CRM.Models.Entities.SupportAccessTicket> CreateSupportTicketAsync(
            Guid tenantId, 
            string ticketNumber, 
            string reason, 
            string? superAdminUserId = null, 
            string? createdByUserId = null,
            bool isApproved = true, 
            TimeSpan? validity = null)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.IgnoreTenantFilter = true;

            var ticket = new AI_Powered_CRM.Models.Entities.SupportAccessTicket
            {
                TicketId = Guid.NewGuid(),
                TicketNumber = ticketNumber,
                TenantId = tenantId,
                SuperAdminUserId = superAdminUserId ?? string.Empty,
                CreatedByUserId = createdByUserId ?? "test-creator-id",
                Reason = reason,
                Status = isApproved ? "Approved" : "Pending",
                ApprovedAt = isApproved ? DateTime.UtcNow : null,
                ApprovedByUserId = isApproved ? "test-approver-id" : null,
                ExpiresAt = DateTime.UtcNow.Add(validity ?? TimeSpan.FromHours(4)),
                CreatedAt = DateTime.UtcNow
            };

            db.SupportAccessTickets.Add(ticket);
            await db.SaveChangesAsync();
            return ticket;
        }

        public async Task<(System.Net.Http.HttpClient client, ApplicationUser user)> CreateAuthenticatedClientAsync(
            string role, 
            bool isActive = true,
            Guid? tenantId = null,
            bool isMembershipActive = true,
            bool isTenantActive = true)
        {
            var client = CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });

            using var scope = Services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }

            var uniqueId = Guid.NewGuid().ToString("N");
            var user = new ApplicationUser
            {
                Id = "user_" + uniqueId,
                UserName = $"{role.ToLower()}_{uniqueId}@crm.local",
                Email = $"{role.ToLower()}_{uniqueId}@crm.local",
                IsActive = isActive,
                EmailConfirmed = true,
                SecurityStamp = Guid.NewGuid().ToString()
            };

            var createResult = await userManager.CreateAsync(user, "TestPass@123");
            if (!createResult.Succeeded)
            {
                throw new InvalidOperationException($"Failed to create test user: {string.Join(", ", createResult.Errors.Select(e => e.Description))}");
            }

            await userManager.AddToRoleAsync(user, role);

            // Assign tenant membership for non-PlatformSuperAdmin roles
            if (role != "PlatformSuperAdmin")
            {
                var targetTenantId = tenantId ?? Guid.Parse("11111111-1111-1111-1111-111111111111");
                await EnsureTenantExistsAsync(targetTenantId, "Test Company " + targetTenantId.ToString().Substring(0, 8), "test-" + targetTenantId.ToString().Substring(0, 8), isTenantActive: isTenantActive);

                var companyRole = role switch
                {
                    "Admin" or "CompanyAdmin" => AI_Powered_CRM.Models.Enums.CompanyRole.CompanyAdmin,
                    "Manager" or "SalesManager" => AI_Powered_CRM.Models.Enums.CompanyRole.Manager,
                    _ => AI_Powered_CRM.Models.Enums.CompanyRole.SalesExecutive
                };

                db.IgnoreTenantFilter = true;
                db.TenantMemberships.Add(new AI_Powered_CRM.Models.Entities.TenantMembership
                {
                    MembershipId = Guid.NewGuid(),
                    TenantId = targetTenantId,
                    UserId = user.Id,
                    CompanyRole = companyRole,
                    IsActive = isMembershipActive,
                    JoinedAt = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
            }

            var signInManager = scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();
            var principal = await signInManager.CreateUserPrincipalAsync(user);

            var cookieOptions = scope.ServiceProvider
                .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
                .Get(IdentityConstants.ApplicationScheme);

            var ticket = new AuthenticationTicket(principal, IdentityConstants.ApplicationScheme);
            var protectedCookie = cookieOptions.TicketDataFormat.Protect(ticket);

            var cookieName = cookieOptions.Cookie.Name ?? ".AspNetCore.Identity.Application";
            client.DefaultRequestHeaders.Add("Cookie", $"{cookieName}={protectedCookie}");

            return (client, user);
        }
    }
}
