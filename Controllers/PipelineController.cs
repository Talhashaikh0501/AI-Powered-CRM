using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AI_Powered_CRM.DTOs;
using AI_Powered_CRM.Models.Enums;
using AI_Powered_CRM.Services.Interfaces;
using AI_Powered_CRM.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AI_Powered_CRM.Controllers
{
    [Authorize]
    public class PipelineController : Controller
    {
        private readonly ILeadService _leadService;

        public PipelineController(ILeadService leadService)
        {
            _leadService = leadService;
        }

        private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        private string GetUserRole() => User.IsInRole("Admin") ? "Admin" : (User.IsInRole("Manager") ? "Manager" : "SalesExecutive");

        // GET: /Pipeline
        public async Task<IActionResult> Index()
        {
            var userId = GetUserId();
            var role = GetUserRole();

            var stages = await _leadService.GetPipelineKanbanAsync(userId, role);

            var activeStages = stages
                .Where(s => s.Key != LeadStatus.Converted && s.Key != LeadStatus.Lost)
                .SelectMany(s => s.Value)
                .ToList();

            var totalVal = activeStages.Where(l => l.Budget.HasValue).Sum(l => l.Budget!.Value);

            var vm = new PipelineViewModel
            {
                Stages = stages,
                TotalActivePipelineValue = totalVal,
                TotalActiveDealsCount = activeStages.Count
            };

            return View(vm);
        }

        // POST: /Pipeline/MoveStage (AJAX)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MoveStage([FromBody] ChangeLeadStatusDto dto)
        {
            var success = await _leadService.ChangeStatusAsync(
                dto.LeadId, dto.NewStatus, dto.Notes ?? "Moved via Pipeline Kanban", GetUserId(), GetUserRole()
            );

            if (!success)
            {
                return BadRequest(new { success = false, message = "Could not move deal stage." });
            }

            return Json(new { success = true, newStatus = dto.NewStatus.ToString() });
        }
    }
}
