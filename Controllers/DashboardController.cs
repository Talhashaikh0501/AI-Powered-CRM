using System.Security.Claims;
using System.Threading.Tasks;
using AI_Powered_CRM.Data;
using AI_Powered_CRM.Models.Enums;
using AI_Powered_CRM.Services.Interfaces;
using AI_Powered_CRM.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AI_Powered_CRM.Controllers
{
    [Authorize(Policy = "TenantRequired")]
    public class DashboardController : Controller
    {
        private readonly IReportService _reportService;
        private readonly ApplicationDbContext _context;

        public DashboardController(IReportService reportService, ApplicationDbContext context)
        {
            _reportService = reportService;
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId) || _context.CurrentTenantId == Guid.Empty)
                return Forbid();

            // The active workspace membership is the source of truth for the user's CRM role.
            var membership = await _context.TenantMemberships
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(m => m.Tenant)
                .FirstOrDefaultAsync(m =>
                    m.UserId == userId &&
                    m.TenantId == _context.CurrentTenantId &&
                    m.IsActive &&
                    m.Tenant != null &&
                    m.Tenant.IsActive);

            if (membership == null)
                return Forbid();

            var role = membership.CompanyRole switch
            {
                CompanyRole.CompanyAdmin => "Admin",
                CompanyRole.Manager => "Manager",
                CompanyRole.SalesExecutive => "SalesExecutive",
                _ => "Unknown"
            };

            if (role == "Unknown")
                return Forbid();

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
