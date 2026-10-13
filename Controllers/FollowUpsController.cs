using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AI_Powered_CRM.DTOs;
using AI_Powered_CRM.Models;
using AI_Powered_CRM.Models.Enums;
using AI_Powered_CRM.Services.Interfaces;
using AI_Powered_CRM.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AI_Powered_CRM.Controllers
{
    [Authorize]
    public class FollowUpsController : Controller
    {
        private readonly IFollowUpService _followUpService;
        private readonly UserManager<ApplicationUser> _userManager;

        public FollowUpsController(
            IFollowUpService followUpService,
            UserManager<ApplicationUser> userManager)
        {
            _followUpService = followUpService;
            _userManager = userManager;
        }

        private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        private string GetUserRole() => User.IsInRole("Admin") ? "Admin" : (User.IsInRole("Manager") ? "Manager" : "SalesExecutive");

        // GET: /FollowUps
        public async Task<IActionResult> Index()
        {
            var userId = GetUserId();
            var role = GetUserRole();

            var overdue = await _followUpService.GetOverdueFollowUpsAsync(userId, role);
            var today = await _followUpService.GetTodayFollowUpsAsync(userId, role);
            var allPending = await _followUpService.GetFollowUpsAsync(userId, role, FollowUpStatus.Pending);
            var completed = await _followUpService.GetFollowUpsAsync(userId, role, FollowUpStatus.Completed);

            var startOfTomorrow = DateTime.UtcNow.Date.AddDays(1);
            var upcoming = allPending.Where(f => f.DueAt >= startOfTomorrow).ToList();

            var availableUsers = await _userManager.Users.Where(u => u.IsActive).ToListAsync();

            var vm = new FollowUpsViewModel
            {
                Overdue = overdue,
                Today = today,
                Upcoming = upcoming,
                Completed = completed.OrderByDescending(f => f.CompletedAt).Take(20).ToList(),
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

            var success = await _followUpService.CompleteFollowUpAsync(followUpId, outcomeNotes, GetUserId());
            if (success)
            {
                TempData["SuccessMessage"] = "Follow-up marked as completed.";
            }
            else
            {
                TempData["ErrorMessage"] = "Could not complete follow-up.";
            }

            return RedirectToAction(nameof(Index));
        }

        // POST: /FollowUps/Reschedule
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reschedule(int followUpId, DateTime newDueAt, string? reason)
        {
            var success = await _followUpService.RescheduleFollowUpAsync(followUpId, newDueAt, reason, GetUserId());
            if (success)
            {
                TempData["SuccessMessage"] = "Follow-up rescheduled successfully.";
            }
            else
            {
                TempData["ErrorMessage"] = "Could not reschedule follow-up.";
            }

            return RedirectToAction(nameof(Index));
        }
    }
}
