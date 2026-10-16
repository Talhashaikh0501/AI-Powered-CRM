using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AI_Powered_CRM.Data;
using AI_Powered_CRM.Models.Entities;
using AI_Powered_CRM.Models.Enums;
using AI_Powered_CRM.Services.Interfaces;
using AI_Powered_CRM.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

        public PlatformController(
            ApplicationDbContext context,
            ISupportAccessService supportAccessService,
            IAuditService auditService,
            ILogger<PlatformController> logger)
        {
            _context = context;
            _supportAccessService = supportAccessService;
            _auditService = auditService;
            _logger = logger;
        }

        private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "SuperAdmin";

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

                var allTenants = await _context.Tenants.AsNoTracking().ToListAsync();
                var filteredTenants = await query.OrderByDescending(t => t.CreatedAt).ToListAsync();

                var memberships = await _context.TenantMemberships
                    .AsNoTracking()
                    .Where(m => m.IsActive)
                    .ToListAsync();

                var totalUsers = await _context.Users
                    .AsNoTracking()
                    .CountAsync(u => u.IsActive);

                var totalAiCalls = allTenants.Sum(t => t.CurrentMonthAiUsageCount);

                // Estimated MRR based on active commercial tiers (Starter: ₹1,499, Pro: ₹3,999, Enterprise: ₹8,999)
                decimal estimatedMrr = allTenants.Where(t => t.IsActive && t.SubscriptionStatus == SubscriptionStatus.Active).Sum(t => t.SubscriptionTier switch
                {
                    SubscriptionTier.Starter => 1499m,
                    SubscriptionTier.Professional => 3999m,
                    SubscriptionTier.Enterprise => 8999m,
                    _ => 0m
                });

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

                var vm = new PlatformTenantDetailsViewModel
                {
                    TenantId = tenant.TenantId,
                    CompanyName = tenant.CompanyName,
                    Slug = tenant.Slug,
                    ContactEmail = tenant.ContactEmail,
                    Phone = tenant.Phone,
                    DefaultCurrency = tenant.DefaultCurrency,
                    Tier = tenant.SubscriptionTier,
                    Status = tenant.SubscriptionStatus,
                    TrialEndsAt = tenant.TrialEndsAt,
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
                var oldStatus = tenant.SubscriptionStatus;
                var oldSeats = tenant.MaxUserSeats;
                var oldLeads = tenant.MaxLeadsQuota;
                var oldAiLimit = tenant.MonthlyAiRequestLimit;

                tenant.SubscriptionTier = model.SubscriptionTier;
                tenant.SubscriptionStatus = model.SubscriptionStatus;
                tenant.MaxUserSeats = model.MaxUserSeats;
                tenant.MaxLeadsQuota = model.MaxLeadsQuota;
                tenant.MonthlyAiRequestLimit = model.MonthlyAiRequestLimit;
                tenant.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                await _auditService.LogAsync(GetUserId(), "Tenant", tenant.TenantId.ToString(), "TenantPlanUpdatedBySuperAdmin", new
                {
                    OldTier = oldTier.ToString(),
                    NewTier = model.SubscriptionTier.ToString(),
                    OldStatus = oldStatus.ToString(),
                    NewStatus = model.SubscriptionStatus.ToString(),
                    OldSeats = oldSeats,
                    NewSeats = model.MaxUserSeats,
                    OldLeadsQuota = oldLeads,
                    NewLeadsQuota = model.MaxLeadsQuota,
                    OldAiLimit = oldAiLimit,
                    NewAiLimit = model.MonthlyAiRequestLimit,
                    Reason = model.Reason ?? "Administrative quota adjustment"
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
