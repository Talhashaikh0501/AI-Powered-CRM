using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AI_Powered_CRM.Data;
using AI_Powered_CRM.Models;
using AI_Powered_CRM.Models.Entities;
using AI_Powered_CRM.Models.Enums;
using AI_Powered_CRM.Services.Implementations;
using AI_Powered_CRM.Services.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AIPoweredCRM.Tests
{
    public class SprintHardeningTests : IClassFixture<CrmWebApplicationFactory>
    {
        private readonly CrmWebApplicationFactory _factory;

        public SprintHardeningTests(CrmWebApplicationFactory factory)
        {
            _factory = factory;
        }

        // =========================================================================
        // TASK 1: SECURE PLATFORM SUPERADMIN PROVISIONING TESTS
        // =========================================================================

        [Fact]
        public async Task TC_SUPERADMIN_BOOTSTRAP_001_RejectsCollisionWithExistingTenantAdmin()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

            var tenantId = Guid.NewGuid();
            await _factory.EnsureTenantExistsAsync(tenantId, "Target Company", "target-" + Guid.NewGuid().ToString("N")[..6]);

            var existingEmail = $"companyadmin_{Guid.NewGuid():N}@tenantcorp.in";
            var companyAdmin = new ApplicationUser
            {
                Id = "ca_" + Guid.NewGuid().ToString("N"),
                UserName = existingEmail,
                Email = existingEmail,
                FullName = "Ramesh Gupta",
                IsActive = true,
                EmailConfirmed = true,
                SecurityStamp = Guid.NewGuid().ToString()
            };
            var createRes = await userManager.CreateAsync(companyAdmin, "SecurePass@123");
            Assert.True(createRes.Succeeded);

            if (!await roleManager.RoleExistsAsync("CompanyAdmin"))
                await roleManager.CreateAsync(new IdentityRole("CompanyAdmin"));
            await userManager.AddToRoleAsync(companyAdmin, "CompanyAdmin");

            db.IgnoreTenantFilter = true;
            db.TenantMemberships.Add(new TenantMembership
            {
                MembershipId = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = companyAdmin.Id,
                CompanyRole = CompanyRole.CompanyAdmin,
                IsActive = true,
                JoinedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            // Configure SuperAdmin bootstrap pointing to this exact email
            var inMemorySettings = new Dictionary<string, string?>
            {
                ["Security:SuperAdminBootstrap:Email"] = existingEmail,
                ["Security:SuperAdminBootstrap:Password"] = "SuperSecretAdminPass@2026",
                ["Security:SuperAdminBootstrap:FullName"] = "Attempted Impersonator"
            };

            var testConfig = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();
            var customProvider = new ConfigurationOverrideServiceProvider(_factory.Services, testConfig);

            // Run DbInitializer SeedAsync with this configuration
            await DbInitializer.SeedAsync(customProvider, isDevelopment: false);

            // VERIFICATION: Check via fresh scope that CompanyAdmin was NOT elevated!
            using var verifyScope = _factory.Services.CreateScope();
            var verifyUserManager = verifyScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await verifyUserManager.FindByEmailAsync(existingEmail);
            Assert.NotNull(user);

            var isPlatformSuperAdmin = await verifyUserManager.IsInRoleAsync(user, "PlatformSuperAdmin");
            var isSuperAdmin = await verifyUserManager.IsInRoleAsync(user, "SuperAdmin");

            Assert.False(isPlatformSuperAdmin, "Security Failure: Tenant user was elevated to PlatformSuperAdmin!");
            Assert.False(isSuperAdmin, "Security Failure: Tenant user was elevated to SuperAdmin!");
        }

        [Fact]
        public async Task TC_SUPERADMIN_BOOTSTRAP_002_IdempotentRepeatPreservesExistingSuperAdminWithoutDuplicates()
        {
            var saEmail = $"dedicated_sa_{Guid.NewGuid():N}@platform.local";
            var inMemorySettings = new Dictionary<string, string?>
            {
                ["Security:SuperAdminBootstrap:Email"] = saEmail,
                ["Security:SuperAdminBootstrap:Password"] = "SafePassword!2026",
                ["Security:SuperAdminBootstrap:FullName"] = "Dedicated Ops Lead"
            };
            var testConfig = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();
            var customProvider = new ConfigurationOverrideServiceProvider(_factory.Services, testConfig);

            // First run: Creates account
            await DbInitializer.SeedAsync(customProvider, isDevelopment: false);

            using (var verifyScope1 = _factory.Services.CreateScope())
            {
                var verifyUserManager = verifyScope1.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var db = verifyScope1.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var saUser = await verifyUserManager.FindByEmailAsync(saEmail);
                Assert.NotNull(saUser);
                Assert.True(await verifyUserManager.IsInRoleAsync(saUser, "PlatformSuperAdmin"));

                // Verify no tenant memberships
                db.IgnoreTenantFilter = true;
                var memberships = await db.TenantMemberships.Where(m => m.UserId == saUser.Id).ToListAsync();
                Assert.Empty(memberships);
            }

            // Second run: Safe idempotent repeat
            await DbInitializer.SeedAsync(customProvider, isDevelopment: false);

            using (var verifyScope2 = _factory.Services.CreateScope())
            {
                var db = verifyScope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                db.IgnoreTenantFilter = true;
                var count = await db.Users.CountAsync(u => u.Email == saEmail);
                Assert.Equal(1, count);
            }
        }

        private class ConfigurationOverrideServiceProvider : IServiceProvider, IServiceScopeFactory
        {
            private readonly IServiceProvider _root;
            private readonly IConfiguration _config;

            public ConfigurationOverrideServiceProvider(IServiceProvider root, IConfiguration config)
            {
                _root = root;
                _config = config;
            }

            public object? GetService(Type serviceType)
            {
                if (serviceType == typeof(IConfiguration)) return _config;
                if (serviceType == typeof(IServiceScopeFactory)) return this;
                return _root.GetService(serviceType);
            }

            public IServiceScope CreateScope()
            {
                var innerScope = _root.GetRequiredService<IServiceScopeFactory>().CreateScope();
                return new ScopeWrapper(innerScope, _config);
            }

            private class ScopeWrapper : IServiceScope, IServiceProvider
            {
                private readonly IServiceScope _inner;
                private readonly IConfiguration _config;

                public ScopeWrapper(IServiceScope inner, IConfiguration config)
                {
                    _inner = inner;
                    _config = config;
                }

                public IServiceProvider ServiceProvider => this;

                public object? GetService(Type serviceType)
                {
                    if (serviceType == typeof(IConfiguration)) return _config;
                    return _inner.ServiceProvider.GetService(serviceType);
                }

                public void Dispose() => _inner.Dispose();
            }
        }

        // =========================================================================
        // TASK 3: EMAIL DELIVERY RELIABILITY TESTS
        // =========================================================================

        [Fact]
        public async Task TC_EMAIL_RELIABILITY_001_SmtpFailure_ReturnsFalse_NoMisleadingSuccess()
        {
            // Point to an invalid SMTP server host that will immediately fail
            var inMemorySettings = new Dictionary<string, string?>
            {
                ["Email:Smtp:Host"] = "127.0.0.1",
                ["Email:Smtp:Port"] = "65534", // Unreachable port
                ["Email:Smtp:EnableSsl"] = "false",
                ["Email:FromEmail"] = "noreply@crm.local"
            };
            var config = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();

            var mockEnv = new TestWebHostEnvironment { EnvironmentName = Environments.Production };
            var service = new EmailDeliveryService(config, mockEnv, NullLogger<EmailDeliveryService>.Instance);

            // Attempt email dispatch
            var result = await service.SendEmailAsync("customer@client.com", "Test Subject", "Test Body");

            // MUST be false - accurate status, never falsely reporting success!
            Assert.False(result);
        }

        [Fact]
        public async Task TC_EMAIL_RELIABILITY_002_UnconfiguredSmtp_InProduction_ReturnsFalse()
        {
            // Empty SMTP configuration
            var inMemorySettings = new Dictionary<string, string?>
            {
                ["Email:Smtp:Host"] = "",
                ["Email:Smtp:Port"] = ""
            };
            var config = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();

            var mockEnv = new TestWebHostEnvironment { EnvironmentName = Environments.Production };
            var service = new EmailDeliveryService(config, mockEnv, NullLogger<EmailDeliveryService>.Instance);

            var result = await service.SendEmailAsync("customer@client.com", "Subject", "Body");

            // Must reject in production when SMTP is not configured
            Assert.False(result);
        }

        [Fact]
        public async Task TC_EMAIL_RELIABILITY_003_UnconfiguredSmtp_InDevelopment_CapturesEmailSuccessfully()
        {
            var inMemorySettings = new Dictionary<string, string?>();
            var config = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();

            var mockEnv = new TestWebHostEnvironment { EnvironmentName = Environments.Development };
            var service = new EmailDeliveryService(config, mockEnv, NullLogger<EmailDeliveryService>.Instance);

            var targetEmail = $"dev_{Guid.NewGuid():N}@devtest.local";
            var result = await service.SendEmailAsync(targetEmail, "Dev Subject", "<h1>Welcome</h1>");

            Assert.True(result);

            // Verify captured in memory
            var captured = EmailDeliveryService.CapturedEmails.FirstOrDefault(e => e.ToEmail == targetEmail);
            Assert.NotNull(captured);
            Assert.Equal("Dev Subject", captured.Subject);
            Assert.Contains("Welcome", captured.HtmlBody);
        }

        private class TestWebHostEnvironment : IWebHostEnvironment
        {
            public string WebRootPath { get; set; } = string.Empty;
            public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = null!;
            public string ApplicationName { get; set; } = "AI_Powered_CRM";
            public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
            public string ContentRootPath { get; set; } = string.Empty;
            public string EnvironmentName { get; set; } = Environments.Development;
        }
    }
}
