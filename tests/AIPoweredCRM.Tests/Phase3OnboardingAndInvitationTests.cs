using System;
using System.Linq;
using System.Threading.Tasks;
using AI_Powered_CRM.Data;
using AI_Powered_CRM.Models;
using AI_Powered_CRM.Models.Entities;
using AI_Powered_CRM.Models.Enums;
using AI_Powered_CRM.Services.Implementations;
using AI_Powered_CRM.Services.Interfaces;
using AI_Powered_CRM.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AIPoweredCRM.Tests
{
    public class Phase3OnboardingAndInvitationTests : IClassFixture<CrmWebApplicationFactory>
    {
        private readonly CrmWebApplicationFactory _factory;

        public Phase3OnboardingAndInvitationTests(CrmWebApplicationFactory factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task TC_ONBOARD_001_RegisterCompany_CreatesTenantSettingsUserMembership_Atomically()
        {
            using var scope = _factory.Services.CreateScope();
            var onboardingService = scope.ServiceProvider.GetRequiredService<ICompanyOnboardingService>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var slug = "innovate-tech-" + Guid.NewGuid().ToString("N")[..8];
            var email = $"founder_{Guid.NewGuid():N}@innovate.com";

            var model = new RegisterCompanyViewModel
            {
                CompanyName = "Innovate Tech Ltd",
                Slug = slug,
                AdminFullName = "Ada Lovelace",
                ContactEmail = email,
                Phone = "+91 9988776655",
                Password = "Password@123",
                ConfirmPassword = "Password@123"
            };

            var result = await onboardingService.RegisterCompanyAsync(model);

            Assert.True(result.Succeeded, string.Join("; ", result.Errors));
            Assert.NotNull(result.TenantId);
            Assert.NotNull(result.UserId);

            // Verify Tenant created with FreeTrial defaults
            var tenant = await db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.TenantId == result.TenantId.Value);
            Assert.NotNull(tenant);
            Assert.Equal("Innovate Tech Ltd", tenant.CompanyName);
            Assert.Equal(slug, tenant.Slug);
            Assert.Equal("INR", tenant.DefaultCurrency);
            Assert.Equal(5, tenant.MaxUserSeats);
            Assert.True(tenant.IsActive);

            // Verify TenantSetting
            var settings = await db.TenantSettings.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.TenantId == result.TenantId.Value);
            Assert.NotNull(settings);
            Assert.Equal("INR", settings.CurrencyCode);
            Assert.Equal("₹", settings.CurrencySymbol);
            Assert.Equal("India Standard Time", settings.TimeZoneId);

            // Verify User
            var user = await userManager.FindByIdAsync(result.UserId);
            Assert.NotNull(user);
            Assert.Equal("Ada Lovelace", user.FullName);
            Assert.True(await userManager.IsInRoleAsync(user, "CompanyAdmin"));
            Assert.True(await userManager.IsInRoleAsync(user, "Admin"));

            // Verify Membership
            var membership = await db.TenantMemberships.IgnoreQueryFilters()
                .FirstOrDefaultAsync(m => m.TenantId == result.TenantId.Value && m.UserId == user.Id);
            Assert.NotNull(membership);
            Assert.Equal(CompanyRole.CompanyAdmin, membership.CompanyRole);
            Assert.True(membership.IsActive);
        }

        [Fact]
        public async Task TC_ONBOARD_002_RegisterCompany_RejectsDuplicateSlug()
        {
            using var scope = _factory.Services.CreateScope();
            var onboardingService = scope.ServiceProvider.GetRequiredService<ICompanyOnboardingService>();

            var duplicateSlug = "unique-corp-" + Guid.NewGuid().ToString("N")[..8];

            var model1 = new RegisterCompanyViewModel
            {
                CompanyName = "First Corp",
                Slug = duplicateSlug,
                AdminFullName = "First Founder",
                ContactEmail = $"first_{Guid.NewGuid():N}@corp.com",
                Password = "Password@123",
                ConfirmPassword = "Password@123"
            };

            var res1 = await onboardingService.RegisterCompanyAsync(model1);
            Assert.True(res1.Succeeded);

            var model2 = new RegisterCompanyViewModel
            {
                CompanyName = "Second Corp",
                Slug = duplicateSlug,
                AdminFullName = "Second Founder",
                ContactEmail = $"second_{Guid.NewGuid():N}@corp.com",
                Password = "Password@123",
                ConfirmPassword = "Password@123"
            };

            var res2 = await onboardingService.RegisterCompanyAsync(model2);
            Assert.False(res2.Succeeded);
            Assert.Contains(res2.Errors, e => e.Contains("already taken", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task TC_ONBOARD_003_RegisterCompany_RejectsDuplicateUserEmail()
        {
            using var scope = _factory.Services.CreateScope();
            var onboardingService = scope.ServiceProvider.GetRequiredService<ICompanyOnboardingService>();

            var sharedEmail = $"shared_{Guid.NewGuid():N}@company.com";

            var model1 = new RegisterCompanyViewModel
            {
                CompanyName = "Company One",
                Slug = "corp-one-" + Guid.NewGuid().ToString("N")[..8],
                AdminFullName = "Founder One",
                ContactEmail = sharedEmail,
                Password = "Password@123",
                ConfirmPassword = "Password@123"
            };

            var res1 = await onboardingService.RegisterCompanyAsync(model1);
            Assert.True(res1.Succeeded);

            var model2 = new RegisterCompanyViewModel
            {
                CompanyName = "Company Two",
                Slug = "corp-two-" + Guid.NewGuid().ToString("N")[..8],
                AdminFullName = "Founder Two",
                ContactEmail = sharedEmail,
                Password = "Password@123",
                ConfirmPassword = "Password@123"
            };

            var res2 = await onboardingService.RegisterCompanyAsync(model2);
            Assert.False(res2.Succeeded);
            Assert.Contains(res2.Errors, e => e.Contains("already exists", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task TC_ONBOARD_004_IsSlugAvailable_ReturnsExpectedStatus()
        {
            using var scope = _factory.Services.CreateScope();
            var onboardingService = scope.ServiceProvider.GetRequiredService<ICompanyOnboardingService>();

            var availableSlug = "brand-new-slug-" + Guid.NewGuid().ToString("N")[..8];
            Assert.True(await onboardingService.IsSlugAvailableAsync(availableSlug));

            var model = new RegisterCompanyViewModel
            {
                CompanyName = "Slug Check Co",
                Slug = availableSlug,
                AdminFullName = "Slug Tester",
                ContactEmail = $"slug_{Guid.NewGuid():N}@tester.com",
                Password = "Password@123",
                ConfirmPassword = "Password@123"
            };

            await onboardingService.RegisterCompanyAsync(model);
            Assert.False(await onboardingService.IsSlugAvailableAsync(availableSlug));
        }

        [Fact]
        public async Task TC_INVITE_001_CreateInvitation_GeneratesTokenAndStoresSha256Hash()
        {
            using var scope = _factory.Services.CreateScope();
            var invitationService = scope.ServiceProvider.GetRequiredService<ITenantInvitationService>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var tenantId = Guid.NewGuid();
            await _factory.EnsureTenantExistsAsync(tenantId, "Invite Test Co", "invite-test-co-" + Guid.NewGuid().ToString("N")[..6]);

            var employeeEmail = $"employee_{Guid.NewGuid():N}@test.com";
            var result = await invitationService.CreateInvitationAsync(tenantId, employeeEmail, CompanyRole.SalesExecutive, "admin_user");

            Assert.True(result.Succeeded, string.Join("; ", result.Errors));
            Assert.NotNull(result.InvitationId);
            Assert.NotNull(result.RawToken);
            Assert.Equal(64, result.RawToken.Length); // 32 bytes in hex = 64 hex characters

            // Verify stored record
            var invite = await db.TenantInvitations.IgnoreQueryFilters()
                .FirstOrDefaultAsync(i => i.InvitationId == result.InvitationId!.Value);

            Assert.NotNull(invite);
            Assert.Equal(employeeEmail, invite.Email);
            Assert.Equal(CompanyRole.SalesExecutive, invite.CompanyRole);
            Assert.False(invite.IsAccepted);
            Assert.True(invite.ExpiresAt > DateTime.UtcNow.AddDays(6));

            // Verify raw token was hashed (the raw token itself must not be stored in DB plaintext)
            Assert.NotEqual(result.RawToken, invite.TokenHash);
        }

        [Fact]
        public async Task TC_INVITE_002_CreateInvitation_EnforcesSeatQuota()
        {
            using var scope = _factory.Services.CreateScope();
            var invitationService = scope.ServiceProvider.GetRequiredService<ITenantInvitationService>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var tenantId = Guid.NewGuid();
            // Create tenant with MaxUserSeats = 1
            var tenant = new Tenant
            {
                TenantId = tenantId,
                CompanyName = "Quota Limited Co",
                Slug = "quota-limited-" + Guid.NewGuid().ToString("N")[..6],
                ContactEmail = "quota@test.com",
                DefaultCurrency = "INR",
                SubscriptionTier = SubscriptionTier.FreeTrial,
                SubscriptionStatus = SubscriptionStatus.Active,
                MaxUserSeats = 1,
                IsActive = true
            };
            db.Tenants.Add(tenant);

            // Add 1 active member so seat count = 1/1 (full)
            db.TenantMemberships.Add(new TenantMembership
            {
                MembershipId = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = "existing_user_" + Guid.NewGuid().ToString("N"),
                CompanyRole = CompanyRole.CompanyAdmin,
                IsActive = true
            });
            await db.SaveChangesAsync();

            // Attempt to invite another user
            var result = await invitationService.CreateInvitationAsync(tenantId, "overflow@test.com", CompanyRole.SalesExecutive, "admin");

            Assert.False(result.Succeeded);
            Assert.Contains(result.Errors, e => e.Contains("limit reached", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task TC_INVITE_003_ValidateToken_RejectsInvalidAndExpiredTokens()
        {
            using var scope = _factory.Services.CreateScope();
            var invitationService = scope.ServiceProvider.GetRequiredService<ITenantInvitationService>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // 1. Non-existent token
            var nullInvite = await invitationService.ValidateInvitationTokenAsync("non_existent_raw_token_xyz");
            Assert.Null(nullInvite);

            // 2. Expired token
            var tenantId = Guid.NewGuid();
            await _factory.EnsureTenantExistsAsync(tenantId, "Expiry Test Co", "expiry-test-co-" + Guid.NewGuid().ToString("N")[..6]);

            var rawToken = "expired_raw_token_test_1234567890abcdef";
            var tokenHash = TenantInvitationService.ComputeTokenHash(rawToken);

            var expiredInvite = new TenantInvitation
            {
                InvitationId = Guid.NewGuid(),
                TenantId = tenantId,
                Email = "expired@test.com",
                CompanyRole = CompanyRole.SalesExecutive,
                TokenHash = tokenHash,
                ExpiresAt = DateTime.UtcNow.AddMinutes(-5), // Expired
                IsAccepted = false,
                InvitedByUserId = "admin"
            };
            db.TenantInvitations.Add(expiredInvite);
            await db.SaveChangesAsync();

            var validatedExpired = await invitationService.ValidateInvitationTokenAsync(rawToken);
            Assert.Null(validatedExpired);
        }

        [Fact]
        public async Task TC_INVITE_004_AcceptInvitation_CreatesUserMembershipAndMarksAccepted()
        {
            using var scope = _factory.Services.CreateScope();
            var invitationService = scope.ServiceProvider.GetRequiredService<ITenantInvitationService>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var tenantId = Guid.NewGuid();
            await _factory.EnsureTenantExistsAsync(tenantId, "Join Co", "join-co-" + Guid.NewGuid().ToString("N")[..6]);

            var employeeEmail = $"new_rep_{Guid.NewGuid():N}@joinco.com";
            var createRes = await invitationService.CreateInvitationAsync(tenantId, employeeEmail, CompanyRole.SalesExecutive, "founder_user");
            Assert.True(createRes.Succeeded);

            // Accept invitation
            var acceptRes = await invitationService.AcceptInvitationAsync(createRes.RawToken!, "Bob Smith", "SecurePass@123");
            Assert.True(acceptRes.Succeeded, string.Join("; ", acceptRes.Errors));
            Assert.NotNull(acceptRes.UserId);
            Assert.Equal(tenantId, acceptRes.TenantId);

            // Verify ApplicationUser created
            var user = await userManager.FindByEmailAsync(employeeEmail);
            Assert.NotNull(user);
            Assert.Equal("Bob Smith", user.FullName);
            Assert.True(await userManager.IsInRoleAsync(user, "SalesExecutive"));

            // Verify TenantMembership
            var membership = await db.TenantMemberships.IgnoreQueryFilters()
                .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.UserId == user.Id);
            Assert.NotNull(membership);
            Assert.Equal(CompanyRole.SalesExecutive, membership.CompanyRole);
            Assert.True(membership.IsActive);

            // Verify invitation marked accepted
            Assert.NotNull(createRes.InvitationId);
            var invite = await db.TenantInvitations.IgnoreQueryFilters()
                .FirstOrDefaultAsync(i => i.InvitationId == createRes.InvitationId!.Value);
            Assert.NotNull(invite);
            Assert.True(invite.IsAccepted);
        }

        [Fact]
        public async Task TC_INVITE_005_AcceptInvitation_PreventsDoubleAcceptance()
        {
            using var scope = _factory.Services.CreateScope();
            var invitationService = scope.ServiceProvider.GetRequiredService<ITenantInvitationService>();

            var tenantId = Guid.NewGuid();
            await _factory.EnsureTenantExistsAsync(tenantId, "Double Accept Co", "double-accept-" + Guid.NewGuid().ToString("N")[..6]);

            var email = $"double_{Guid.NewGuid():N}@double.com";
            var createRes = await invitationService.CreateInvitationAsync(tenantId, email, CompanyRole.Manager, "admin");
            Assert.True(createRes.Succeeded);

            // First acceptance
            var firstAccept = await invitationService.AcceptInvitationAsync(createRes.RawToken!, "First Try", "Password@123");
            Assert.True(firstAccept.Succeeded);

            // Second acceptance must fail
            var secondAccept = await invitationService.AcceptInvitationAsync(createRes.RawToken!, "Second Try", "Password@123");
            Assert.False(secondAccept.Succeeded);
            Assert.Contains(secondAccept.Errors, e => e.Contains("invalid", StringComparison.OrdinalIgnoreCase) || e.Contains("already been accepted", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task TC_INVITE_006_RevokeInvitation_RemovesPendingAndRejectsAccepted()
        {
            using var scope = _factory.Services.CreateScope();
            var invitationService = scope.ServiceProvider.GetRequiredService<ITenantInvitationService>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var tenantId = Guid.NewGuid();
            await _factory.EnsureTenantExistsAsync(tenantId, "Revoke Co", "revoke-co-" + Guid.NewGuid().ToString("N")[..6]);

            // 1. Revoke pending invitation
            var email1 = $"revoke_me_{Guid.NewGuid():N}@revoke.com";
            var invite1 = await invitationService.CreateInvitationAsync(tenantId, email1, CompanyRole.SalesExecutive, "admin");
            Assert.True(invite1.Succeeded);

            var revokeSuccess = await invitationService.RevokeInvitationAsync(tenantId, invite1.InvitationId!.Value, "admin");
            Assert.True(revokeSuccess);

            // Ensure removed
            var dbInvite1 = await db.TenantInvitations.IgnoreQueryFilters().FirstOrDefaultAsync(i => i.InvitationId == invite1.InvitationId.Value);
            Assert.Null(dbInvite1);

            // 2. Cannot revoke already accepted invitation
            var email2 = $"accepted_me_{Guid.NewGuid():N}@revoke.com";
            var invite2 = await invitationService.CreateInvitationAsync(tenantId, email2, CompanyRole.SalesExecutive, "admin");
            await invitationService.AcceptInvitationAsync(invite2.RawToken!, "Accepted Rep", "Password@123");

            var revokeAccepted = await invitationService.RevokeInvitationAsync(tenantId, invite2.InvitationId!.Value, "admin");
            Assert.False(revokeAccepted);
        }

        [Fact]
        public async Task TC_INVITE_007_GetTenantInvitations_EnforcesTenantIsolation()
        {
            using var scope = _factory.Services.CreateScope();
            var invitationService = scope.ServiceProvider.GetRequiredService<ITenantInvitationService>();

            var tenantA = Guid.NewGuid();
            var tenantB = Guid.NewGuid();

            await _factory.EnsureTenantExistsAsync(tenantA, "Tenant A", "tenant-a-" + Guid.NewGuid().ToString("N")[..6]);
            await _factory.EnsureTenantExistsAsync(tenantB, "Tenant B", "tenant-b-" + Guid.NewGuid().ToString("N")[..6]);

            await invitationService.CreateInvitationAsync(tenantA, $"userA_{Guid.NewGuid():N}@a.com", CompanyRole.SalesExecutive, "adminA");
            await invitationService.CreateInvitationAsync(tenantB, $"userB_{Guid.NewGuid():N}@b.com", CompanyRole.Manager, "adminB");

            var listA = await invitationService.GetTenantInvitationsAsync(tenantA);
            var listB = await invitationService.GetTenantInvitationsAsync(tenantB);

            Assert.All(listA, i => Assert.Contains("@a.com", i.Email));
            Assert.All(listB, i => Assert.Contains("@b.com", i.Email));
            Assert.DoesNotContain(listA, i => listB.Any(b => b.InvitationId == i.InvitationId));
        }
    }
}
