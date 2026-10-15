using System.Threading.Tasks;
using AI_Powered_CRM.Services.Interfaces;
using AI_Powered_CRM.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AI_Powered_CRM.Controllers
{
    [Authorize(Policy = "TenantRequired")]
    [Authorize(Roles = "Admin,Manager,CompanyAdmin")]
    public class ReportsController : Controller
    {
        private readonly IReportService _reportService;

        public ReportsController(IReportService reportService)
        {
            _reportService = reportService;
        }

        public async Task<IActionResult> Index()
        {
            var conversion = await _reportService.GetConversionReportAsync();
            var sources = await _reportService.GetSourcePerformanceAsync();
            var staff = await _reportService.GetStaffPerformanceAsync();

            var vm = new ReportsViewModel
            {
                ConversionReport = conversion,
                SourcePerformance = sources,
                StaffPerformance = staff
            };

            return View(vm);
        }
    }
}
