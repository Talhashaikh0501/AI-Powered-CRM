using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AI_Powered_CRM.DTOs;
using AI_Powered_CRM.Models;
using AI_Powered_CRM.Models.Enums;
using AI_Powered_CRM.Security;
using AI_Powered_CRM.Services.Interfaces;
using AI_Powered_CRM.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AI_Powered_CRM.Controllers
{
    [Authorize(Policy = "TenantRequired")]
    public class FollowUpsController : Controller
    {
        private readonly IFollowUpService         _followUpService;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IAuthorizationService    _authService;

        public FollowUpsController(
            IFollowUpService         followUpService,
            UserManager<ApplicationUser> userManager,
            IAuthorizationService    authService)
        {
            _followUpService = followUpService;
            _userManager     = userManager;
            _authService     = authService;
        }

        private string GetUserId()   => User.FindFirstValue(ClaimTypes.NameIdentifier)
                                        ?? throw new InvalidOperationException("Not authenticated.");
        private string GetUserRole() => User.IsInRole("Admin")   ? "Admin"
                                      : User.IsInRole("Manager") ? "Manager"
                                                                  : "SalesExecutive";

        // GET: /FollowUps
        public async Task<IActionResult> Index()
        {
            var userId = GetUserId();
            var role   = GetUserRole();

            var overdue    = await _followUpService.GetOverdueFollowUpsAsync(userId, role);
            var today      = await _followUpService.GetTodayFollowUpsAsync(userId, role);
            var allPending = await _followUpService.GetFollowUpsAsync(userId, role, FollowUpStatus.Pending);
            var completed  = await _followUpService.GetFollowUpsAsync(userId, role, FollowUpStatus.Completed);

            var startOfTomorrow = DateTime.UtcNow.Date.AddDays(1);
            var upcoming = allPending.Where(f => f.DueAt >= startOfTomorrow).ToList();

            var availableUsers = await _userManager.Users.Where(u => u.IsActive).ToListAsync();

            var vm = new FollowUpsViewModel
            {
                Overdue        = overdue,
                Today          = today,
                Upcoming       = upcoming,
                Completed      = completed.OrderByDescending(f => f.CompletedAt).Take(20).ToList(),
                AvailableUsers = availableUsers
            };

            return View(vm);
        }

        // POST: /FollowUps/Complete
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Complete(int followUpId, string outcomeNotes)
        {
            if (string.IsNullOrWhiteSpace(outcomeNotes))
            {
                TempData["ErrorMessage"] = "Outcome notes are required to mark a follow-up complete.";
                return RedirectToAction(nameof(Index));
            }

            // Ownership check: SalesExecutive can only complete their own follow-ups
            var auth = await _authService.AuthorizeAsync(User, followUpId, new FollowUpOwnershipRequirement());
            if (!auth.Succeeded)
                return Forbid();

            var success = await _followUpService.CompleteFollowUpAsync(
                followUpId, outcomeNotes, GetUserId(), GetUserRole());

            TempData[success ? "SuccessMessage" : "ErrorMessage"] =
                success ? "Follow-up marked as completed." : "Could not complete follow-up.";

            return RedirectToAction(nameof(Index));
        }

        // POST: /FollowUps/Reschedule
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reschedule(int followUpId, DateTime newDueAt, string? reason)
        {
            // Ownership check
            var auth = await _authService.AuthorizeAsync(User, followUpId, new FollowUpOwnershipRequirement());
            if (!auth.Succeeded)
                return Forbid();

            var success = await _followUpService.RescheduleFollowUpAsync(
                followUpId, newDueAt, reason, GetUserId(), GetUserRole());

            TempData[success ? "SuccessMessage" : "ErrorMessage"] =
                success ? "Follow-up rescheduled successfully." : "Could not reschedule follow-up.";

            return RedirectToAction(nameof(Index));
        }
    }
}
