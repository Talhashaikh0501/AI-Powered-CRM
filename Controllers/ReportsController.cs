using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using AI_Powered_CRM.Data;
using AI_Powered_CRM.Models.Entities;
using AI_Powered_CRM.Security;
using AI_Powered_CRM.Services.Interfaces;
using AI_Powered_CRM.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AI_Powered_CRM.Controllers
{
    [Authorize(Policy = "TenantRequired")]
    [Authorize(Roles = "Admin,Manager,CompanyAdmin")]
    public class ReportsController : Controller
    {
        private readonly IReportService _reportService;
        private readonly ApplicationDbContext _context;
        private readonly IAuditService _auditService;
        private readonly ITenantContext _tenantContext;

        public ReportsController(
            IReportService reportService,
            ApplicationDbContext context,
            IAuditService auditService,
            ITenantContext tenantContext)
        {
            _reportService = reportService;
            _context = context;
            _auditService = auditService;
            _tenantContext = tenantContext;
        }

        private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        private string GetUserRole() => User.IsInRole("Admin") ? "Admin" 
            : (User.IsInRole("CompanyAdmin") ? "CompanyAdmin" 
            : (User.IsInRole("Manager") ? "Manager" : "SalesExecutive"));

        // GET: /Reports
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

        // GET: /Reports/Insights
        public async Task<IActionResult> Insights()
        {
            var userId = GetUserId();
            var role = GetUserRole();

            var vm = await _reportService.GetAIInsightsAsync(userId, role);
            return View(vm);
        }

        // GET: /Reports/ExportConversionCsv
        public async Task<IActionResult> ExportConversionCsv()
        {
            var conversion = await _reportService.GetConversionReportAsync();
            var sources = await _reportService.GetSourcePerformanceAsync();
            var staff = await _reportService.GetStaffPerformanceAsync();

            var sb = new StringBuilder();

            // Section 1: Executive Conversion Summary
            sb.AppendLine("EXECUTIVE CONVERSION SUMMARY");
            sb.AppendLine("Metric,Value");
            sb.AppendLine($"Total Leads Tracked,{conversion.TotalLeads}");
            sb.AppendLine($"Deals Won / Converted,{conversion.ConvertedLeads}");
            sb.AppendLine($"Deals Lost,{conversion.LostLeads}");
            sb.AppendLine($"Win Rate (%),{conversion.ConversionRate}%");
            sb.AppendLine($"Won Deal Budget Value (INR - Estimated),₹{conversion.TotalWonDealValue:N2}");
            sb.AppendLine($"Average Won Deal Size (INR - Estimated),₹{conversion.AverageDealSize:N2}");
            sb.AppendLine();

            // Section 2: Acquisition Channel Performance
            sb.AppendLine("ACQUISITION CHANNEL PERFORMANCE & ROI");
            sb.AppendLine("Channel,Total Leads,Converted Leads,Conversion Rate (%),Estimated Won Value (INR)");
            foreach (var s in sources)
            {
                sb.AppendLine($"\"{EscapeCsv(s.Source)}\",{s.LeadCount},{s.ConvertedCount},{s.ConversionRate}%,{s.EstimatedWonValue:F2}");
            }
            sb.AppendLine();

            // Section 3: Staff Performance Leaderboard
            sb.AppendLine("SALES REPRESENTATIVE LEADERBOARD");
            sb.AppendLine("Representative,Assigned Leads,Completed Follow-Ups,Deals Won,Conversion Rate (%),Closed Deal Value (INR)");
            foreach (var rep in staff)
            {
                sb.AppendLine($"\"{EscapeCsv(rep.StaffName)}\",{rep.AssignedLeadsCount},{rep.CompletedFollowUpsCount},{rep.ConvertedLeadsCount},{rep.ConversionRate}%,{rep.ClosedRevenue:F2}");
            }

            var userId = GetUserId();
            await _auditService.LogAsync(userId, "Report", "Conversion", "ExportedCsv", new { ExportType = "ConversionReport" });

            var fileName = $"crm_conversion_report_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
            return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", fileName);
        }

        // GET: /Reports/ExportLeadsCsv
        public async Task<IActionResult> ExportLeadsCsv()
        {
            var currentTenantId = _tenantContext.CurrentTenantId;
            var settings = await _context.TenantSettings.FirstOrDefaultAsync(s => s.TenantId == currentTenantId);

            // Check if export is restricted to Workspace Admins
            var isRestricted = settings?.RestrictLeadExport ?? true;
            if (isRestricted && !User.IsInRole("Admin") && !User.IsInRole("CompanyAdmin"))
            {
                TempData["ErrorMessage"] = "Lead data export is restricted to Workspace Administrators.";
                return RedirectToAction(nameof(Index));
            }

            var leads = await _context.Leads
                .Include(l => l.OwnerUser)
                .Where(l => !l.IsArchived)
                .OrderByDescending(l => l.CreatedAt)
                .ToListAsync();

            var sb = new StringBuilder();
            sb.AppendLine("LeadId,Name,Phone,Email,Company,Source,InterestedProduct,Budget (INR),Status,Priority,Owner,CreatedAt");

            foreach (var l in leads)
            {
                var ownerName = l.OwnerUser?.FullName ?? l.OwnerUser?.UserName ?? "Unassigned";
                sb.AppendLine($"{l.LeadId},\"{EscapeCsv(l.Name)}\",\"{EscapeCsv(l.Phone)}\",\"{EscapeCsv(l.Email ?? "")}\",\"{EscapeCsv(l.Company ?? "")}\",\"{EscapeCsv(l.Source)}\",\"{EscapeCsv(l.InterestedProduct)}\",{l.Budget?.ToString("F2") ?? "0.00"},{l.Status},{l.Priority},\"{EscapeCsv(ownerName)}\",{l.CreatedAt:yyyy-MM-dd HH:mm:ss}");
            }

            var userId = GetUserId();
            await _auditService.LogAsync(userId, "Lead", "All", "ExportedCsv",
                new { TenantId = currentTenantId, LeadCount = leads.Count }, isSecurityCritical: true);

            var fileName = $"leads_export_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
            return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", fileName);
        }

        private static string EscapeCsv(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace("\"", "\"\"");
        }
    }
}
