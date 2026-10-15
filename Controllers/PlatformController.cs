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
        public async Task<IActionResult> Index()
        {
            var previousIgnore = _context.IgnoreTenantFilter;
            try
            {
                _context.IgnoreTenantFilter = true;

                var tenants = await _context.Tenants
                    .AsNoTracking()
                    .OrderByDescending(t => t.CreatedAt)
                    .ToListAsync();

                var memberships = await _context.TenantMemberships
                    .AsNoTracking()
                    .Where(m => m.IsActive)
                    .ToListAsync();

                var totalUsers = await _context.Users
                    .AsNoTracking()
                    .CountAsync(u => u.IsActive);

                var totalAiCalls = tenants.Sum(t => t.CurrentMonthAiUsageCount);

                var tierDistribution = new Dictionary<SubscriptionTier, int>();
                foreach (var tier in Enum.GetValues<SubscriptionTier>())
                {
                    tierDistribution[tier] = tenants.Count(t => t.SubscriptionTier == tier);
                }

                var tenantSummaries = tenants.Select(t => new PlatformTenantSummaryDto
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
                    TotalTenantsCount = tenants.Count,
                    ActiveTenantsCount = tenants.Count(t => t.IsActive),
                    TotalPlatformUsersCount = totalUsers,
                    TotalAiCallsThisMonth = totalAiCalls,
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
