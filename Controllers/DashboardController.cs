using System.Security.Claims;
using System.Threading.Tasks;
using AI_Powered_CRM.Services.Interfaces;
using AI_Powered_CRM.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AI_Powered_CRM.Controllers
{
    [Authorize(Policy = "TenantRequired")]
    public class DashboardController : Controller
    {
        private readonly IReportService _reportService;

        public DashboardController(IReportService reportService)
        {
            _reportService = reportService;
        }

        public async Task<IActionResult> Index()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var role = User.IsInRole("Admin") ? "Admin" : (User.IsInRole("Manager") ? "Manager" : (User.IsInRole("SalesExecutive") ? "SalesExecutive" : "Unknown"));
            var userName = User.Identity?.Name ?? "User";

            var summary = await _reportService.GetDashboardSummaryAsync(userId, role);

            var vm = new DashboardViewModel
            {
                Summary = summary,
                UserRole = role,
                UserName = userName
            };

            return View(vm);
        }
    }
}
