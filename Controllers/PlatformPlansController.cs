using System;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using AI_Powered_CRM.Data;
using AI_Powered_CRM.Models.Entities;
using AI_Powered_CRM.Services.Interfaces;
using AI_Powered_CRM.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AI_Powered_CRM.Controllers
{
    [Authorize(Policy = "PlatformSuperAdminOnly")]
    public class PlatformPlansController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IAuditService _auditService;

        public PlatformPlansController(ApplicationDbContext db, IAuditService auditService)
        {
            _db = db;
            _auditService = auditService;
        }

        private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "PlatformSuperAdmin";

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var plans = await _db.SubscriptionPlans.AsNoTracking()
                .OrderBy(p => p.SortOrder).ThenBy(p => p.Name)
                .Select(p => new SubscriptionPlanManagementItem
                {
                    PlanId = p.PlanId,
                    Name = p.Name,
                    Code = p.Code,
                    MonthlyPriceInr = p.MonthlyPriceInr,
                    YearlyPriceInr = p.YearlyPriceInr,
                    MaxUserSeats = p.MaxUserSeats,
                    MaxLeadsQuota = p.MaxLeadsQuota,
                    MonthlyAiRequestLimit = p.MonthlyAiRequestLimit,
                    AssignedCompanies = p.Tenants.Count,
                    IsActive = p.IsActive,
                    IsPublic = p.IsPublic,
                    IsRecommended = p.IsRecommended
                }).ToListAsync();

            return View(new SubscriptionPlanManagementViewModel { Plans = plans });
        }

        [HttpGet]
        public IActionResult Create() => View("Edit", new ManageSubscriptionPlanViewModel());

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ManageSubscriptionPlanViewModel model)
        {
            if (!ModelState.IsValid) return View("Edit", model);

            var code = NormalizeCode(model.Code);
            if (await _db.SubscriptionPlans.AnyAsync(p => p.Code == code))
            {
                ModelState.AddModelError(nameof(model.Code), "This plan code already exists.");
                return View("Edit", model);
            }

            var plan = new SubscriptionPlan();
            Apply(plan, model, code);
            plan.CreatedAt = DateTime.UtcNow;
            plan.UpdatedAt = DateTime.UtcNow;
            _db.SubscriptionPlans.Add(plan);
            await _db.SaveChangesAsync();
            await _auditService.LogAsync(GetUserId(), "SubscriptionPlan", plan.PlanId.ToString(), "SubscriptionPlanCreated", new { plan.Code, plan.Name, plan.MonthlyPriceInr, plan.YearlyPriceInr }, true);
            TempData["SuccessMessage"] = $"Plan '{plan.Name}' created.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Edit(Guid id)
        {
            var plan = await _db.SubscriptionPlans.FindAsync(id);
            if (plan == null) return NotFound();

            return View(new ManageSubscriptionPlanViewModel
            {
                PlanId = plan.PlanId,
                Code = plan.Code,
                Name = plan.Name,
                Tagline = plan.Tagline,
                MonthlyPriceInr = plan.MonthlyPriceInr,
                YearlyPriceInr = plan.YearlyPriceInr,
                TrialDays = plan.TrialDays,
                MaxUserSeats = plan.MaxUserSeats,
                MaxLeadsQuota = plan.MaxLeadsQuota,
                MonthlyAiRequestLimit = plan.MonthlyAiRequestLimit,
                IsRecommended = plan.IsRecommended,
                IsActive = plan.IsActive,
                IsPublic = plan.IsPublic,
                SortOrder = plan.SortOrder,
                Highlights = JoinJson(plan.HighlightsJson),
                IncludedFeatures = JoinJson(plan.IncludedFeaturesJson)
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(ManageSubscriptionPlanViewModel model)
        {
            if (!ModelState.IsValid || !model.PlanId.HasValue)
                return View(model);

            var plan = await _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.PlanId == model.PlanId.Value);
            if (plan == null) return NotFound();

            var code = NormalizeCode(model.Code);
            if (code != plan.Code && await _db.Tenants.AnyAsync(t => t.SubscriptionPlanId == plan.PlanId))
            {
                ModelState.AddModelError(nameof(model.Code), "The code cannot be changed while companies are assigned to this plan.");
                return View(model);
            }
            if (await _db.SubscriptionPlans.AnyAsync(p => p.PlanId != plan.PlanId && p.Code == code))
            {
                ModelState.AddModelError(nameof(model.Code), "This plan code already exists.");
                return View(model);
            }

            Apply(plan, model, code);
            plan.UpdatedAt = DateTime.UtcNow;

            // Quotas are copied onto tenant rows for fast enforcement. Keep assigned
            // workspaces synchronized when a SuperAdmin changes a plan's limits.
            var assignedTenants = await _db.Tenants.Where(t => t.SubscriptionPlanId == plan.PlanId).ToListAsync();
            foreach (var tenant in assignedTenants)
            {
                tenant.MaxUserSeats = plan.MaxUserSeats;
                tenant.MaxLeadsQuota = plan.MaxLeadsQuota;
                tenant.MonthlyAiRequestLimit = plan.MonthlyAiRequestLimit;
                tenant.UpdatedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();
            await _auditService.LogAsync(GetUserId(), "SubscriptionPlan", plan.PlanId.ToString(), "SubscriptionPlanUpdated", new { plan.Code, plan.Name, plan.MonthlyPriceInr, plan.YearlyPriceInr, plan.MaxUserSeats, plan.MaxLeadsQuota, plan.MonthlyAiRequestLimit, AssignedCompanies = assignedTenants.Count }, true);
            TempData["SuccessMessage"] = $"Plan '{plan.Name}' updated. {assignedTenants.Count} assigned workspace(s) synchronized.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Archive(Guid id)
        {
            var plan = await _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.PlanId == id);
            if (plan == null) return NotFound();

            plan.IsActive = false;
            plan.IsPublic = false;
            plan.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            await _auditService.LogAsync(GetUserId(), "SubscriptionPlan", plan.PlanId.ToString(), "SubscriptionPlanArchived", new { plan.Code, plan.Name }, true);
            TempData["SuccessMessage"] = $"Plan '{plan.Name}' archived. Existing assigned companies retain their plan.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(Guid id)
        {
            var plan = await _db.SubscriptionPlans.Include(p => p.Tenants)
                .FirstOrDefaultAsync(p => p.PlanId == id);
            if (plan == null) return NotFound();

            var wasAssigned = plan.Tenants.Count > 0;
            if (wasAssigned)
            {
                plan.IsActive = false;
                plan.IsPublic = false;
                plan.UpdatedAt = DateTime.UtcNow;
                TempData["SuccessMessage"] = $"Plan '{plan.Name}' is assigned to {plan.Tenants.Count} company(s), so it was archived instead of deleted.";
            }
            else
            {
                _db.SubscriptionPlans.Remove(plan);
                TempData["SuccessMessage"] = $"Unused plan '{plan.Name}' permanently deleted.";
            }

            await _db.SaveChangesAsync();
            await _auditService.LogAsync(GetUserId(), "SubscriptionPlan", id.ToString(), wasAssigned ? "SubscriptionPlanArchivedInsteadOfDeleted" : "SubscriptionPlanDeleted", new { plan.Code, plan.Name }, true);
            return RedirectToAction(nameof(Index));
        }

        private static void Apply(SubscriptionPlan plan, ManageSubscriptionPlanViewModel model, string code)
        {
            plan.Code = code;
            plan.Name = model.Name.Trim();
            plan.Tagline = model.Tagline?.Trim() ?? string.Empty;
            plan.MonthlyPriceInr = model.MonthlyPriceInr;
            plan.YearlyPriceInr = model.YearlyPriceInr;
            plan.TrialDays = model.TrialDays;
            plan.MaxUserSeats = model.MaxUserSeats;
            plan.MaxLeadsQuota = model.MaxLeadsQuota;
            plan.MonthlyAiRequestLimit = model.MonthlyAiRequestLimit;
            plan.IsRecommended = model.IsRecommended;
            plan.IsActive = model.IsActive;
            plan.IsPublic = model.IsPublic;
            plan.SortOrder = model.SortOrder;
            plan.HighlightsJson = JsonSerializer.Serialize(SplitLines(model.Highlights));
            plan.IncludedFeaturesJson = JsonSerializer.Serialize(SplitLines(model.IncludedFeatures));
        }

        private static string NormalizeCode(string code) => code.Trim().ToLowerInvariant();
        private static string[] SplitLines(string? value) => (value ?? string.Empty)
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim()).Where(x => x.Length > 0).Distinct().ToArray();

        private static string JoinJson(string json)
        {
            try { return string.Join(Environment.NewLine, JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>()); }
            catch { return string.Empty; }
        }
    }
}