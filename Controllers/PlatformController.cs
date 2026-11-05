using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AI_Powered_CRM.Data;
using AI_Powered_CRM.Models;
using AI_Powered_CRM.Models.Entities;
using AI_Powered_CRM.Models.Enums;
using AI_Powered_CRM.Services.Interfaces;
using AI_Powered_CRM.Security;
using AI_Powered_CRM.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AI_Powered_CRM.Controllers
{
    [Authorize(Policy = "PlatformSuperAdminOnly")]
    public class PlatformController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ISupportAccessService _supportAccessService;
        private readonly IAuditService _auditService;
        private readonly ILogger<PlatformController> _logger;
        private readonly IDataProtectionProvider _dataProtectionProvider;
        private readonly ICompanyOnboardingService? _companyOnboardingService;
        private readonly UserManager<ApplicationUser>? _userManager;
        private readonly IEmailDeliveryService? _emailDeliveryService;

        public PlatformController(
            ApplicationDbContext context,
            ISupportAccessService supportAccessService,
            IAuditService auditService,
            ILogger<PlatformController> logger,
            IDataProtectionProvider? dataProtectionProvider = null,
            ICompanyOnboardingService? companyOnboardingService = null,
            UserManager<ApplicationUser>? userManager = null,
            IEmailDeliveryService? emailDeliveryService = null)
        {
            _context = context;
            _supportAccessService = supportAccessService;
            _auditService = auditService;
            _logger = logger;
            _dataProtectionProvider = dataProtectionProvider!;
            _companyOnboardingService = companyOnboardingService;
            _userManager = userManager;
            _emailDeliveryService = emailDeliveryService;
        }

        private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "SuperAdmin";

        // GET: /Platform/CreateCompany
        [HttpGet]
        public IActionResult CreateCompany()
        {
            return View(new RegisterCompanyViewModel());
        }

        // POST: /Platform/CreateCompany
        // Uses the same transactional onboarding flow as public registration, but is
        // available only to authenticated Platform SuperAdmins.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateCompany(RegisterCompanyViewModel model)
        {
            if (_companyOnboardingService == null || _userManager == null || _emailDeliveryService == null)
                return StatusCode(500, "Company provisioning services are not available.");

            if (!ModelState.IsValid)
                return View(model);

            var result = await _companyOnboardingService.RegisterCompanyAsync(model);
            if (!result.Succeeded)
            {
                foreach (var error in result.Errors)
                    ModelState.AddModelError(string.Empty, error);
                return View(model);
            }

            var admin = result.UserId == null ? null : await _userManager.FindByIdAsync(result.UserId);
            var emailSent = false;
            if (admin != null)
            {
                var token = await _userManager.GenerateEmailConfirmationTokenAsync(admin);
                var confirmationLink = Url.Action(
                    "ConfirmEmail",
                    "Account",
                    new { userId = admin.Id, token },
                    Request.Scheme);

                if (!string.IsNullOrWhiteSpace(confirmationLink))
                {
                    emailSent = await _emailDeliveryService.SendEmailConfirmationAsync(
                        admin.Email!,
                        confirmationLink,
                        admin.FullName ?? admin.Email!);
                }
            }

            await _auditService.LogAsync(
                GetUserId(),
                "Tenant",
                result.TenantId!.Value.ToString(),
                "CompanyProvisionedByPlatformSuperAdmin",
                new
                {
                    model.CompanyName,
                    model.Slug,
                    AdminEmail = model.ContactEmail,
                    AdminUserId = result.UserId,
                    EmailConfirmationSent = emailSent
                });

            _logger.LogInformation(
                "Platform SuperAdmin {UserId} provisioned company {TenantId} ({CompanyName}); confirmation email sent: {EmailSent}.",
                GetUserId(), result.TenantId, model.CompanyName, emailSent);

            TempData["SuccessMessage"] = emailSent
                ? $"Company '{model.CompanyName}' was created. The first administrator has been sent an email confirmation link."
                : $"Company '{model.CompanyName}' was created, but the confirmation email could not be sent. Check email configuration and resend confirmation before the administrator signs in.";

            return RedirectToAction(nameof(TenantDetails), new { id = result.TenantId });
        }

        // GET: /Platform
        public async Task<IActionResult> Index(string? search, SubscriptionTier? tier, bool? activeOnly)
        {
            var previousIgnore = _context.IgnoreTenantFilter;
            try
            {
                _context.IgnoreTenantFilter = true;

                var query = _context.Tenants.AsNoTracking().AsQueryable();

                if (!string.IsNullOrWhiteSpace(search))
                {
                    var term = search.Trim().ToLowerInvariant();
                    query = query.Where(t => t.CompanyName.ToLower().Contains(term) ||
                                             t.Slug.ToLower().Contains(term) ||
                                             t.ContactEmail.ToLower().Contains(term));
                }

                if (tier.HasValue)
                {
                    query = query.Where(t => t.SubscriptionTier == tier.Value);
                }

                if (activeOnly.HasValue)
                {
                    query = query.Where(t => t.IsActive == activeOnly.Value);
                }

                var allTenants = await _context.Tenants.AsNoTracking().Include(t => t.SubscriptionPlan).ToListAsync();
                var filteredTenants = await query.OrderByDescending(t => t.CreatedAt).ToListAsync();

                var memberships = await _context.TenantMemberships
                    .AsNoTracking()
                    .Where(m => m.IsActive)
                    .ToListAsync();

                var totalUsers = await _context.Users
                    .AsNoTracking()
                    .CountAsync(u => u.IsActive);

                var totalAiCalls = allTenants.Sum(t => t.CurrentMonthAiUsageCount);

                // Estimate MRR from the current database-backed plan price.
                decimal estimatedMrr = allTenants
                    .Where(t => t.IsActive && t.SubscriptionStatus == SubscriptionStatus.Active)
                    .Sum(t => t.SubscriptionPlan?.MonthlyPriceInr ?? 0m);

                var tierDistribution = new Dictionary<SubscriptionTier, int>();
                foreach (var tEnum in Enum.GetValues<SubscriptionTier>())
                {
                    tierDistribution[tEnum] = allTenants.Count(t => t.SubscriptionTier == tEnum);
                }

                var tenantSummaries = filteredTenants.Select(t => new PlatformTenantSummaryDto
                {
                    TenantId = t.TenantId,
                    CompanyName = t.CompanyName,
                    Slug = t.Slug,
                    ContactEmail = t.ContactEmail,
                    Tier = t.SubscriptionTier,
                    Status = t.SubscriptionStatus,
                    ActiveSeats = memberships.Count(m => m.TenantId == t.TenantId),
                    MaxUserSeats = t.MaxUserSeats,
                    CurrentMonthAiUsageCount = t.CurrentMonthAiUsageCount,
                    MonthlyAiRequestLimit = t.MonthlyAiRequestLimit,
                    IsActive = t.IsActive,
                    CreatedAt = t.CreatedAt
                }).ToList();

                var vm = new PlatformDashboardViewModel
                {
                    TotalTenantsCount = allTenants.Count,
                    ActiveTenantsCount = allTenants.Count(t => t.IsActive),
                    TotalPlatformUsersCount = totalUsers,
                    TotalAiCallsThisMonth = totalAiCalls,
                    EstimatedMonthlyMrrInr = estimatedMrr,
                    Search = search,
                    FilterTier = tier,
                    FilterActive = activeOnly,
                    TierDistribution = tierDistribution,
                    Tenants = tenantSummaries
                };

                return View(vm);
            }
            finally
            {
                _context.IgnoreTenantFilter = previousIgnore;
            }
        }

        // GET: /Platform/TenantDetails/{id}
        public async Task<IActionResult> TenantDetails(Guid id)
        {
            if (id == Guid.Empty) return BadRequest();

            var previousIgnore = _context.IgnoreTenantFilter;
            try
            {
                _context.IgnoreTenantFilter = true;

                var tenant = await _context.Tenants
                    .Include(t => t.Settings)
                    .Include(t => t.SubscriptionPlan)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(t => t.TenantId == id);

                if (tenant == null)
                    return NotFound();

                var memberships = await _context.TenantMemberships
                    .Include(m => m.User)
                    .AsNoTracking()
                    .Where(m => m.TenantId == id)
                    .ToListAsync();

                var leadsCount = await _context.Leads
                    .IgnoreQueryFilters()
                    .CountAsync(l => l.TenantId == id && !l.IsArchived);

                var recentAudits = await _context.AuditLogs
                    .IgnoreQueryFilters()
                    .Include(a => a.User)
                    .Where(a => a.TenantId == id)
                    .OrderByDescending(a => a.CreatedAt)
                    .Take(15)
                    .Select(a => new PlatformAuditSummaryDto
                    {
                        Action = a.Action,
                        EntityType = a.EntityType,
                        Details = a.DetailsJson,
                        CreatedAt = a.CreatedAt,
                        PerformedBy = a.User != null ? (a.User.FullName ?? a.User.UserName ?? a.UserId!) : "System"
                    })
                    .ToListAsync();

                ViewBag.SubscriptionPlans = await _context.SubscriptionPlans.AsNoTracking()
                    .Where(p => p.IsActive).OrderBy(p => p.SortOrder).ToListAsync();

                var vm = new PlatformTenantDetailsViewModel
                {
                    TenantId = tenant.TenantId,
                    CompanyName = tenant.CompanyName,
                    Slug = tenant.Slug,
                    ContactEmail = tenant.ContactEmail,
                    Phone = tenant.Phone,
                    DefaultCurrency = tenant.DefaultCurrency,
                    Tier = tenant.SubscriptionTier,
                    CurrentPlanId = tenant.SubscriptionPlanId,
                    Status = tenant.SubscriptionStatus,
                    TrialEndsAt = tenant.TrialEndsAt,
                    SubscriptionRenewsAt = tenant.SubscriptionRenewsAt,
                    IsActive = tenant.IsActive,
                    CreatedAt = tenant.CreatedAt,
                    UpdatedAt = tenant.UpdatedAt,

                    ActiveSeats = memberships.Count(m => m.IsActive),
                    MaxUserSeats = tenant.MaxUserSeats,
                    TotalLeadsCount = leadsCount,
                    MaxLeadsQuota = tenant.MaxLeadsQuota,
                    CurrentMonthAiUsageCount = tenant.CurrentMonthAiUsageCount,
                    MonthlyAiRequestLimit = tenant.MonthlyAiRequestLimit,

                    CurrencySymbol = tenant.Settings?.CurrencySymbol ?? "₹",
                    TimeZoneId = tenant.Settings?.TimeZoneId ?? "India Standard Time",
                    DateFormat = tenant.Settings?.DateFormat ?? "dd/MM/yyyy",
                    LeadAssignmentMode = tenant.Settings?.LeadAssignmentMode ?? "Manual",
                    RestrictLeadExport = tenant.Settings?.RestrictLeadExport ?? true,

                    Members = memberships.Select(m => new PlatformMemberSummaryDto
                    {
                        UserId = m.UserId,
                        FullName = m.User?.FullName ?? "Unknown",
                        Email = m.User?.Email ?? "No email",
                        Role = m.CompanyRole,
                        IsActive = m.IsActive,
                        JoinedAt = m.JoinedAt
                    }).ToList(),

                    RecentAudits = recentAudits
                };

                return View(vm);
            }
            finally
            {
                _context.IgnoreTenantFilter = previousIgnore;
            }
        }

        // POST: /Platform/ToggleTenantStatus
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleTenantStatus(ToggleTenantStatusViewModel model)
        {
            var previousIgnore = _context.IgnoreTenantFilter;
            try
            {
                _context.IgnoreTenantFilter = true;

                var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.TenantId == model.TenantId);
                if (tenant == null)
                {
                    TempData["ErrorMessage"] = "Workspace not found.";
                    return RedirectToAction(nameof(Index));
                }

                tenant.IsActive = !tenant.IsActive;
                tenant.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                var actionName = tenant.IsActive ? "TenantActivatedByPlatformSuperAdmin" : "TenantDeactivatedByPlatformSuperAdmin";
                await _auditService.LogAsync(GetUserId(), "Tenant", tenant.TenantId.ToString(), actionName, new
                {
                    tenant.CompanyName,
                    NewStatus = tenant.IsActive ? "Active" : "Deactivated",
                    Reason = model.Reason ?? "Administrative action"
                });

                _logger.LogInformation("Tenant {TenantId} ({CompanyName}) status toggled to {Status} by SuperAdmin {UserId}. Reason: {Reason}",
                    tenant.TenantId, tenant.CompanyName, tenant.IsActive ? "Active" : "Deactivated", GetUserId(), model.Reason);

                TempData["SuccessMessage"] = $"Workspace '{tenant.CompanyName}' is now {(tenant.IsActive ? "Active" : "Deactivated")}.";
                return RedirectToAction(nameof(Index));
            }
            finally
            {
                _context.IgnoreTenantFilter = previousIgnore;
            }
        }

        // POST: /Platform/UpdateTenantPlan
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateTenantPlan(UpdateTenantPlanViewModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "Invalid plan parameters.";
                return RedirectToAction(nameof(TenantDetails), new { id = model.TenantId });
            }

            var previousIgnore = _context.IgnoreTenantFilter;
            try
            {
                _context.IgnoreTenantFilter = true;

                var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.TenantId == model.TenantId);
                if (tenant == null)
                {
                    TempData["ErrorMessage"] = "Workspace not found.";
                    return RedirectToAction(nameof(Index));
                }

                var oldTier = tenant.SubscriptionTier;
                var oldPlanId = tenant.SubscriptionPlanId;
                var oldStatus = tenant.SubscriptionStatus;
                var oldSeats = tenant.MaxUserSeats;
                var oldLeads = tenant.MaxLeadsQuota;
                var oldAiLimit = tenant.MonthlyAiRequestLimit;
                var oldTrialEndsAt = tenant.TrialEndsAt;
                var oldRenewsAt = tenant.SubscriptionRenewsAt;

                tenant.SubscriptionTier = model.SubscriptionTier;
                tenant.SubscriptionStatus = model.SubscriptionStatus;
                tenant.MaxUserSeats = model.MaxUserSeats;
                tenant.MaxLeadsQuota = model.MaxLeadsQuota;
                tenant.MonthlyAiRequestLimit = model.MonthlyAiRequestLimit;
                tenant.TrialEndsAt = model.TrialEndsAt;

                if (model.SubscriptionPlanId.HasValue)
                {
                    var selectedPlan = await _context.SubscriptionPlans.FirstOrDefaultAsync(p =>
                        p.PlanId == model.SubscriptionPlanId.Value && p.IsActive);
                    if (selectedPlan == null)
                    {
                        TempData["ErrorMessage"] = "The selected subscription plan is unavailable or archived.";
                        return RedirectToAction(nameof(TenantDetails), new { id = model.TenantId });
                    }

                    tenant.SubscriptionPlanId = selectedPlan.PlanId;
                    tenant.MaxUserSeats = selectedPlan.MaxUserSeats;
                    tenant.MaxLeadsQuota = selectedPlan.MaxLeadsQuota;
                    tenant.MonthlyAiRequestLimit = selectedPlan.MonthlyAiRequestLimit;
                    tenant.SubscriptionTier = selectedPlan.Code == "starter" ? SubscriptionTier.Starter
                        : selectedPlan.Code == "professional" ? SubscriptionTier.Professional
                        : selectedPlan.Code == "enterprise" ? SubscriptionTier.Enterprise
                        : SubscriptionTier.Custom;
                    if (selectedPlan.TrialDays > 0)
                        tenant.TrialEndsAt = DateTime.UtcNow.AddDays(selectedPlan.TrialDays);
                }
                else
                {
                    tenant.SubscriptionPlanId = null;
                }
                tenant.SubscriptionRenewsAt = model.SubscriptionRenewsAt;
                tenant.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                await _auditService.LogAsync(GetUserId(), "Tenant", tenant.TenantId.ToString(), "TenantPlanUpdatedBySuperAdmin", new
                {
                    OldTier = oldTier.ToString(),
                    NewTier = tenant.SubscriptionTier.ToString(),
                    OldPlanId = oldPlanId,
                    NewPlanId = tenant.SubscriptionPlanId,
                    OldStatus = oldStatus.ToString(),
                    NewStatus = model.SubscriptionStatus.ToString(),
                    OldSeats = oldSeats,
                    NewSeats = model.MaxUserSeats,
                    OldLeadsQuota = oldLeads,
                    NewLeadsQuota = model.MaxLeadsQuota,
                    OldAiLimit = oldAiLimit,
                    NewAiLimit = model.MonthlyAiRequestLimit,
                    OldTrialEndsAt = oldTrialEndsAt,
                    NewTrialEndsAt = model.TrialEndsAt,
                    OldSubscriptionRenewsAt = oldRenewsAt,
                    NewSubscriptionRenewsAt = model.SubscriptionRenewsAt,
                    Reason = model.Reason ?? "Administrative subscription and quota adjustment"
                });

                _logger.LogInformation("SuperAdmin {UserId} updated tenant plan for {TenantId}: Tier {OldTier}->{NewTier}, Seats {OldSeats}->{NewSeats}",
                    GetUserId(), tenant.TenantId, oldTier, model.SubscriptionTier, oldSeats, model.MaxUserSeats);

                TempData["SuccessMessage"] = $"Subscription plan and quotas for '{tenant.CompanyName}' updated successfully.";
                return RedirectToAction(nameof(TenantDetails), new { id = model.TenantId });
            }
            finally
            {
                _context.IgnoreTenantFilter = previousIgnore;
            }
        }

        // POST: /Platform/EnterWorkspace
        // Explicit, time-limited platform-owner access. The middleware validates the protected cookie
        // and writes a permanent tenant audit entry before allowing CRM requests.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EnterWorkspace(Guid tenantId, string? reason)
        {
            if (tenantId == Guid.Empty || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 8 || reason.Trim().Length > 500)
            {
                TempData["ErrorMessage"] = "Select a company and provide an access reason of 8–500 characters.";
                return RedirectToAction(nameof(TenantDetails), new { id = tenantId });
            }

            var previousIgnore = _context.IgnoreTenantFilter;
            try
            {
                _context.IgnoreTenantFilter = true;
                var tenant = await _context.Tenants
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.IsActive);

                if (tenant == null)
                {
                    TempData["ErrorMessage"] = "The company does not exist or is currently inactive.";
                    return RedirectToAction(nameof(Index));
                }

                var expiresAt = DateTime.UtcNow.AddMinutes(60);
                var payload = $"{tenant.TenantId:N}|{expiresAt.Ticks}|{Uri.EscapeDataString(reason.Trim())}";
                var protector = _dataProtectionProvider.CreateProtector("AI_Powered_CRM.PlatformWorkspaceAccess.v1");
                var protectedPayload = protector.Protect(payload);

                Response.Cookies.Append(TenantResolutionMiddleware.PlatformWorkspaceCookieName, protectedPayload, new Microsoft.AspNetCore.Http.CookieOptions
                {
                    HttpOnly = true,
                    Secure = Request.IsHttps,
                    SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Strict,
                    IsEssential = true,
                    Expires = new DateTimeOffset(expiresAt)
                });

                _logger.LogWarning("Platform SuperAdmin {UserId} entered company workspace {TenantId} ({CompanyName}). Reason: {Reason}",
                    GetUserId(), tenant.TenantId, tenant.CompanyName, reason.Trim());

                TempData["SuccessMessage"] = $"You are entering {tenant.CompanyName}'s workspace. Access expires in 60 minutes and is audited.";
                return RedirectToAction("Index", "Dashboard");
            }
            finally
            {
                _context.IgnoreTenantFilter = previousIgnore;
            }
        }

        // POST: /Platform/ExitWorkspace
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ExitWorkspace()
        {
            Response.Cookies.Delete(TenantResolutionMiddleware.PlatformWorkspaceCookieName);
            Response.Cookies.Delete(TenantResolutionMiddleware.ActiveTenantCookieName);
            return RedirectToAction(nameof(Index));
        }

        // GET: /Platform/Support
        public async Task<IActionResult> Support()
        {
            var tickets = await _supportAccessService.GetAllTicketsAsync();

            var previousIgnore = _context.IgnoreTenantFilter;
            List<PlatformTenantSummaryDto> tenantList;
            try
            {
                _context.IgnoreTenantFilter = true;
                tenantList = await _context.Tenants
                    .AsNoTracking()
                    .Where(t => t.IsActive)
                    .OrderBy(t => t.CompanyName)
                    .Select(t => new PlatformTenantSummaryDto
                    {
                        TenantId = t.TenantId,
                        CompanyName = t.CompanyName,
                        Slug = t.Slug,
                        ContactEmail = t.ContactEmail,
                        Tier = t.SubscriptionTier
                    })
                    .ToListAsync();
            }
            finally
            {
                _context.IgnoreTenantFilter = previousIgnore;
            }

            var vm = new SupportAccessPortalViewModel
            {
                Tickets = tickets,
                AvailableTenants = tenantList
            };

            return View(vm);
        }

        // POST: /Platform/RequestSupportAccess
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RequestSupportAccess(RequestSupportAccessViewModel model)
        {
            if (model.TenantId == Guid.Empty || string.IsNullOrWhiteSpace(model.Reason))
            {
                TempData["ErrorMessage"] = "Please select a target company and provide a clear operational reason for support access.";
                return RedirectToAction(nameof(Support));
            }

            try
            {
                var userId = GetUserId();
                var ticket = await _supportAccessService.CreateTicketAsync(
                    model.TenantId,
                    userId,
                    model.Reason.Trim(),
                    User);

                TempData["SuccessMessage"] = $"Support access request submitted (Ticket: {ticket.TicketNumber}). Awaiting customer company approval.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create support access ticket for tenant {TenantId}", model.TenantId);
                TempData["ErrorMessage"] = $"Unable to submit ticket: {ex.Message}";
            }

            return RedirectToAction(nameof(Support));
        }

        // POST: /Platform/RevokeSupportAccess
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RevokeSupportAccess(Guid ticketId, string? reason)
        {
            try
            {
                await _supportAccessService.RevokeTicketAsync(
                    ticketId,
                    reason ?? "Revoked by Platform SuperAdmin",
                    User);

                TempData["SuccessMessage"] = "Support access ticket revoked immediately.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to revoke support access ticket {TicketId}", ticketId);
                TempData["ErrorMessage"] = $"Unable to revoke ticket: {ex.Message}";
            }

            return RedirectToAction(nameof(Support));
        }
    }
}
