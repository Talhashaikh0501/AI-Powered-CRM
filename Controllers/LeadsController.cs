using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using AI_Powered_CRM.AI;
using AI_Powered_CRM.Data;
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
    public class LeadsController : Controller
    {
        private readonly ILeadService _leadService;
        private readonly IActivityService _activityService;
        private readonly IFollowUpService _followUpService;
        private readonly IAIService _aiService;
        private readonly UserManager<ApplicationUser> _userManager;

        public LeadsController(
            ILeadService leadService,
            IActivityService activityService,
            IFollowUpService followUpService,
            IAIService aiService,
            UserManager<ApplicationUser> userManager)
        {
            _leadService = leadService;
            _activityService = activityService;
            _followUpService = followUpService;
            _aiService = aiService;
            _userManager = userManager;
        }

        private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        private string GetUserRole() => User.IsInRole("Admin") ? "Admin" : (User.IsInRole("Manager") ? "Manager" : "SalesExecutive");

        // GET: /Leads
        public async Task<IActionResult> Index(
            string? search,
            LeadStatus? status,
            LeadPriority? priority,
            string? source,
            int page = 1)
        {
            var userId = GetUserId();
            var role = GetUserRole();

            var (leads, totalCount) = await _leadService.GetPagedLeadsAsync(
                search, status, priority, source, userId, role, page, 15
            );

            var vm = new LeadIndexViewModel
            {
                Leads = leads,
                TotalCount = totalCount,
                CurrentPage = page,
                PageSize = 15,
                Search = search,
                Status = status,
                Priority = priority,
                Source = source
            };

            return View(vm);
        }

        // GET: /Leads/Details/{id} (Flagship Lead Intelligence Workspace)
        public async Task<IActionResult> Details(int id)
        {
            var userId = GetUserId();
            var role = GetUserRole();

            var lead = await _leadService.GetLeadDetailsAsync(id, userId, role);
            if (lead == null)
            {
                return NotFound();
            }

            var latestInsight = lead.AIInsights.OrderByDescending(i => i.GeneratedAt).FirstOrDefault();
            var reasons = new List<string>();
            if (latestInsight != null && !string.IsNullOrEmpty(latestInsight.ReasonJson))
            {
                try
                {
                    reasons = JsonSerializer.Deserialize<List<string>>(latestInsight.ReasonJson) ?? new List<string>();
                }
                catch
                {
                    reasons = new List<string> { latestInsight.ReasonJson };
                }
            }

            var availableUsers = await _userManager.Users.Where(u => u.IsActive).ToListAsync();

            var vm = new LeadDetailsViewModel
            {
                Lead = lead,
                LatestInsight = latestInsight,
                ParsedReasons = reasons,
                AvailableUsers = availableUsers,
                CurrentUserRole = role,
                CurrentUserId = userId
            };

            return View(vm);
        }

        // GET: /Leads/Create
        public async Task<IActionResult> Create()
        {
            var availableUsers = await _userManager.Users.Where(u => u.IsActive).ToListAsync();
            var vm = new LeadCreateEditViewModel
            {
                AvailableUsers = availableUsers,
                OwnerUserId = GetUserId()
            };
            return View(vm);
        }

        // POST: /Leads/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(LeadCreateEditViewModel model)
        {
            if (!ModelState.IsValid)
            {
                model.AvailableUsers = await _userManager.Users.Where(u => u.IsActive).ToListAsync();
                return View(model);
            }

            var dto = new CreateLeadDto
            {
                Name = model.Name,
                Phone = model.Phone,
                Email = model.Email,
                Company = model.Company,
                Source = model.Source,
                InterestedProduct = model.InterestedProduct,
                Budget = model.Budget,
                Status = model.Status,
                Priority = model.Priority,
                OwnerUserId = model.OwnerUserId
            };

            var lead = await _leadService.CreateLeadAsync(dto, GetUserId());
            TempData["SuccessMessage"] = $"Lead '{lead.Name}' successfully created!";
            return RedirectToAction(nameof(Details), new { id = lead.LeadId });
        }

        // GET: /Leads/Edit/{id}
        public async Task<IActionResult> Edit(int id)
        {
            var userId = GetUserId();
            var role = GetUserRole();

            var lead = await _leadService.GetLeadDetailsAsync(id, userId, role);
            if (lead == null) return NotFound();

            var availableUsers = await _userManager.Users.Where(u => u.IsActive).ToListAsync();

            var vm = new LeadCreateEditViewModel
            {
                LeadId = lead.LeadId,
                Name = lead.Name,
                Phone = lead.Phone,
                Email = lead.Email,
                Company = lead.Company,
                Source = lead.Source,
                InterestedProduct = lead.InterestedProduct,
                Budget = lead.Budget,
                Status = lead.Status,
                Priority = lead.Priority,
                OwnerUserId = lead.OwnerUserId,
                AvailableUsers = availableUsers
            };

            return View(vm);
        }

        // POST: /Leads/Edit/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, LeadCreateEditViewModel model)
        {
            if (id != model.LeadId) return BadRequest();

            if (!ModelState.IsValid)
            {
                model.AvailableUsers = await _userManager.Users.Where(u => u.IsActive).ToListAsync();
                return View(model);
            }

            var dto = new UpdateLeadDto
            {
                LeadId = model.LeadId.Value,
                Name = model.Name,
                Phone = model.Phone,
                Email = model.Email,
                Company = model.Company,
                Source = model.Source,
                InterestedProduct = model.InterestedProduct,
                Budget = model.Budget,
                Status = model.Status,
                Priority = model.Priority,
                OwnerUserId = model.OwnerUserId
            };

            var success = await _leadService.UpdateLeadAsync(dto, GetUserId(), GetUserRole());
            if (!success) return Forbid();

            TempData["SuccessMessage"] = "Lead updated successfully.";
            return RedirectToAction(nameof(Details), new { id = id });
        }

        // POST: /Leads/ChangeStatus
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangeStatus([FromBody] ChangeLeadStatusDto dto)
        {
            var success = await _leadService.ChangeStatusAsync(dto.LeadId, dto.NewStatus, dto.Notes, GetUserId(), GetUserRole());
            if (!success) return BadRequest(new { success = false, message = "Could not update status." });

            return Json(new { success = true, newStatus = dto.NewStatus.ToString() });
        }

        // POST: /Leads/Assign
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Assign(int leadId, string newOwnerUserId)
        {
            var success = await _leadService.AssignLeadAsync(leadId, newOwnerUserId, GetUserId(), GetUserRole());
            if (!success)
            {
                TempData["ErrorMessage"] = "Unauthorized to assign leads.";
            }
            else
            {
                TempData["SuccessMessage"] = "Lead owner reassigned successfully.";
            }

            return RedirectToAction(nameof(Details), new { id = leadId });
        }

        // POST: /Leads/Archive/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Archive(int id)
        {
            var success = await _leadService.ArchiveLeadAsync(id, GetUserId(), GetUserRole());
            if (!success) return Forbid();

            TempData["SuccessMessage"] = "Lead has been archived.";
            return RedirectToAction(nameof(Index));
        }

        // POST: /Leads/AddActivity
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddActivity(int leadId, ActivityType type, string description)
        {
            if (string.IsNullOrWhiteSpace(description))
            {
                TempData["ErrorMessage"] = "Activity note cannot be empty.";
                return RedirectToAction(nameof(Details), new { id = leadId });
            }

            await _activityService.AddActivityAsync(new AddActivityDto
            {
                LeadId = leadId,
                Type = type,
                Description = description
            }, GetUserId());

            TempData["SuccessMessage"] = "Activity logged to timeline.";
            return RedirectToAction(nameof(Details), new { id = leadId });
        }

        // POST: /Leads/ScheduleFollowUp
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ScheduleFollowUp(int leadId, DateTime dueAt, FollowUpType type, string? notes, string? assignedUserId)
        {
            await _followUpService.ScheduleFollowUpAsync(new ScheduleFollowUpDto
            {
                LeadId = leadId,
                DueAt = dueAt,
                Type = type,
                Notes = notes,
                AssignedUserId = assignedUserId
            }, GetUserId());

            TempData["SuccessMessage"] = "Follow-up successfully scheduled.";
            return RedirectToAction(nameof(Details), new { id = leadId });
        }

        // POST: /Leads/Analyze (AJAX)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Analyze(int id)
        {
            var insight = await _leadService.RunAIAnalysisAsync(id, GetUserId(), GetUserRole());
            if (insight == null)
            {
                return Json(new { success = false, message = "Lead not found or unauthorized." });
            }

            var reasons = new List<string>();
            try
            {
                reasons = JsonSerializer.Deserialize<List<string>>(insight.ReasonJson) ?? new List<string>();
            }
            catch { }

            return Json(new
            {
                success = true,
                classification = insight.Classification.ToString(),
                score = insight.Score,
                summary = insight.Summary,
                nextBestAction = insight.NextBestAction,
                riskLevel = insight.RiskLevel.ToString(),
                urgency = insight.Urgency.ToString(),
                reasons = reasons,
                suggestedResponse = insight.SuggestedResponse,
                provider = insight.Provider,
                model = insight.Model,
                generatedAt = insight.GeneratedAt.ToString("g")
            });
        }

        // POST: /Leads/DraftResponse (AJAX)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DraftResponse(int leadId, string? userTone, string? customInstructions)
        {
            var lead = await _leadService.GetLeadDetailsAsync(leadId, GetUserId(), GetUserRole());
            if (lead == null) return NotFound();

            var draft = await _aiService.GenerateDraftResponseAsync(
                lead,
                lead.Activities.OrderByDescending(a => a.OccurredAt).ToList(),
                userTone,
                customInstructions
            );

            return Json(new { success = true, draft = draft });
        }
    }
}
