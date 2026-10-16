using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AI_Powered_CRM.Data;
using AI_Powered_CRM.Models;
using AI_Powered_CRM.Models.Entities;
using AI_Powered_CRM.Models.Enums;
using AI_Powered_CRM.Services.Interfaces;
using AI_Powered_CRM.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AI_Powered_CRM.Services.Implementations
{
    public class CompanyOnboardingService : ICompanyOnboardingService
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly IAuditService _auditService;
        private readonly ILogger<CompanyOnboardingService> _logger;

        public CompanyOnboardingService(
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            IAuditService auditService,
            ILogger<CompanyOnboardingService> logger)
        {
            _db = db;
            _userManager = userManager;
            _roleManager = roleManager;
            _auditService = auditService;
            _logger = logger;
        }

        public async Task<bool> IsSlugAvailableAsync(string slug)
        {
            if (string.IsNullOrWhiteSpace(slug)) return false;
            var normalizedSlug = slug.Trim().ToLowerInvariant();
            return !await _db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Slug == normalizedSlug);
        }

        public async Task<CompanyRegistrationResult> RegisterCompanyAsync(RegisterCompanyViewModel model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));

            var normalizedSlug = model.Slug.Trim().ToLowerInvariant();
            var normalizedEmail = model.ContactEmail.Trim().ToLowerInvariant();

            // Validate slug format
            if (!Regex.IsMatch(normalizedSlug, @"^[a-z0-9]+(?:-[a-z0-9]+)*$"))
            {
                return CompanyRegistrationResult.Failure("Invalid workspace slug format. Only lowercase alphanumeric characters and hyphens are allowed.");
            }

            // Check slug availability
            if (!await IsSlugAvailableAsync(normalizedSlug))
            {
                return CompanyRegistrationResult.Failure($"The workspace slug '{normalizedSlug}' is already taken. Please choose another.");
            }

            // Check email uniqueness
            var existingUser = await _userManager.FindByEmailAsync(normalizedEmail);
            if (existingUser != null)
            {
                return CompanyRegistrationResult.Failure("An account with this email address already exists. Please log in instead.");
            }

            // Use database transaction for atomic tenant + user creation when provider supports it
            if (_db.Database.IsRelational())
            {
                var strategy = _db.Database.CreateExecutionStrategy();
                return await strategy.ExecuteAsync(async () =>
                {
                    using var tx = await _db.Database.BeginTransactionAsync();
                    try
                    {
                        var result = await ExecuteRegistrationStepsAsync(model, normalizedSlug, normalizedEmail);
                        if (result.Succeeded)
                        {
                            await tx.CommitAsync();
                        }
                        else
                        {
                            await tx.RollbackAsync();
                        }
                        return result;
                    }
                    catch (Exception ex)
                    {
                        await tx.RollbackAsync();
                        _logger.LogError(ex, "Failed to register company '{CompanyName}'", model.CompanyName);
                        return CompanyRegistrationResult.Failure("A system error occurred while registering your company. Please try again.");
                    }
                });
            }
            else
            {
                try
                {
                    return await ExecuteRegistrationStepsAsync(model, normalizedSlug, normalizedEmail);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to register company '{CompanyName}'", model.CompanyName);
                    return CompanyRegistrationResult.Failure("A system error occurred while registering your company. Please try again.");
                }
            }
        }

        private async Task<CompanyRegistrationResult> ExecuteRegistrationStepsAsync(RegisterCompanyViewModel model, string normalizedSlug, string normalizedEmail)
        {
            // 1. Create Tenant
            var tenant = new Tenant
            {
                TenantId = Guid.NewGuid(),
                CompanyName = model.CompanyName.Trim(),
                Slug = normalizedSlug,
                ContactEmail = normalizedEmail,
                Phone = model.Phone?.Trim(),
                DefaultCurrency = "INR",
                SubscriptionTier = SubscriptionTier.FreeTrial,
                SubscriptionStatus = SubscriptionStatus.Trialing,
                TrialEndsAt = DateTime.UtcNow.AddDays(14),
                MaxUserSeats = 5,
                MaxLeadsQuota = 1000,
                MonthlyAiRequestLimit = 200,
                CurrentMonthAiUsageCount = 0,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _db.Tenants.Add(tenant);

            // 2. Create TenantSettings
            var settings = new TenantSetting
            {
                TenantId = tenant.TenantId,
                CurrencyCode = "INR",
                CurrencySymbol = "₹",
                DateFormat = "dd/MM/yyyy",
                TimeZoneId = "India Standard Time",
                FiscalYearStartMonth = 4,
                LeadAssignmentMode = "Manual",
                RestrictLeadExport = true,
                UpdatedAt = DateTime.UtcNow
            };
            _db.TenantSettings.Add(settings);

            await _db.SaveChangesAsync();

            // 3. Create Founder ApplicationUser
            var user = new ApplicationUser
            {
                UserName = normalizedEmail,
                Email = normalizedEmail,
                FullName = model.AdminFullName.Trim(),
                IsActive = true,
                EmailConfirmed = false,
                CreatedAt = DateTime.UtcNow
            };

            var userResult = await _userManager.CreateAsync(user, model.Password);
            if (!userResult.Succeeded)
            {
                var errors = new List<string>();
                foreach (var err in userResult.Errors)
                {
                    errors.Add(err.Description);
                }
                return CompanyRegistrationResult.Failure(errors.ToArray());
            }

            // 4. Ensure Roles exist and assign CompanyAdmin & Admin
            if (!await _roleManager.RoleExistsAsync("CompanyAdmin"))
            {
                await _roleManager.CreateAsync(new IdentityRole("CompanyAdmin"));
            }
            if (!await _roleManager.RoleExistsAsync("Admin"))
            {
                await _roleManager.CreateAsync(new IdentityRole("Admin"));
            }

            await _userManager.AddToRoleAsync(user, "CompanyAdmin");
            await _userManager.AddToRoleAsync(user, "Admin");

            // 5. Create TenantMembership
            var membership = new TenantMembership
            {
                MembershipId = Guid.NewGuid(),
                TenantId = tenant.TenantId,
                UserId = user.Id,
                CompanyRole = CompanyRole.CompanyAdmin,
                IsActive = true,
                JoinedAt = DateTime.UtcNow
            };
            _db.TenantMemberships.Add(membership);
            await _db.SaveChangesAsync();

            // 6. Audit Logging
            await _auditService.LogAsync(user.Id, "Tenant", tenant.TenantId.ToString(), "CompanyRegistered",
                new { tenant.CompanyName, tenant.Slug, user.Email });

            _logger.LogInformation("Company registered successfully: TenantId {TenantId}, Slug {Slug}, AdminUserId {UserId}",
                tenant.TenantId, tenant.Slug, user.Id);

            return CompanyRegistrationResult.Success(tenant.TenantId, user.Id);
        }
    }
}
