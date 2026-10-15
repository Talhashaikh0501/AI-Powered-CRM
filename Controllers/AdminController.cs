using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AI_Powered_CRM.Data;
using AI_Powered_CRM.Models;
using AI_Powered_CRM.Models.Entities;
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
    [Authorize(Roles = "Admin,CompanyAdmin")]
    public class AdminController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly IAuditService _auditService;
        private readonly ApplicationDbContext _context;
        private readonly ITenantContext _tenantContext;
        private readonly ITenantInvitationService _invitationService;
        private readonly ISubscriptionQuotaService _quotaService;

        public AdminController(
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            IAuditService auditService,
            ApplicationDbContext context,
            ITenantContext tenantContext,
            ITenantInvitationService invitationService,
            ISubscriptionQuotaService quotaService)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _auditService = auditService;
            _context = context;
            _tenantContext = tenantContext;
            _invitationService = invitationService;
            _quotaService = quotaService;
        }

        // GET: /Admin/Users
        public async Task<IActionResult> Users()
        {
            var currentTenantId = _tenantContext.CurrentTenantId;
            var tenantUserIds = await _context.TenantMemberships
                .Where(m => m.TenantId == currentTenantId)
                .Select(m => m.UserId)
                .ToListAsync();

            var users = await _userManager.Users
                .Where(u => tenantUserIds.Contains(u.Id))
                .OrderBy(u => u.UserName)
                .ToListAsync();

            var list = new List<UserWithRoleDto>();

            foreach (var u in users)
            {
                var roles = await _userManager.GetRolesAsync(u);
                list.Add(new UserWithRoleDto
                {
                    Id = u.Id,
                    UserName = u.UserName ?? string.Empty,
                    FullName = u.FullName ?? u.UserName ?? "User",
                    Email = u.Email ?? string.Empty,
                    Role = roles.FirstOrDefault() ?? "SalesExecutive",
                    IsActive = u.IsActive,
                    CreatedAt = u.CreatedAt
                });
            }

            return View(new AdminUsersViewModel { Users = list });
        }

        // POST: /Admin/ToggleUserStatus
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleUserStatus(string id)
        {
            var currentTenantId = _tenantContext.CurrentTenantId;
            var isMember = await _context.TenantMemberships
                .AnyAsync(m => m.TenantId == currentTenantId && m.UserId == id);

            if (!isMember)
            {
                return Forbid();
            }

            var user = await _userManager.FindByIdAsync(id);
            if (user != null)
            {
                user.IsActive = !user.IsActive;
                await _userManager.UpdateAsync(user);
                await _userManager.UpdateSecurityStampAsync(user); // Invalidate active user session immediately

                var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                await _auditService.LogAsync(adminId, "User", user.Id, "ToggleStatus", new { user.IsActive, user.UserName }, isSecurityCritical: true);
                TempData["SuccessMessage"] = $"User status updated to {(user.IsActive ? "Active" : "Disabled")}.";
            }
            return RedirectToAction(nameof(Users));
        }

        // GET: /Admin/Audit
        public async Task<IActionResult> Audit()
        {
            var logs = await _auditService.GetRecentLogsAsync(100);
            return View(logs);
        }

        // GET: /Admin/Invitations
        public async Task<IActionResult> Invitations()
        {
            var currentTenantId = _tenantContext.CurrentTenantId;
            var tenant = await _context.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.TenantId == currentTenantId);
            var invitations = await _invitationService.GetTenantInvitationsAsync(currentTenantId);

            var activeSeats = await _context.TenantMemberships
                .Where(m => m.TenantId == currentTenantId && m.IsActive)
                .CountAsync();

            var viewModel = new AdminInvitationsViewModel
            {
                Invitations = invitations,
                ActiveUserSeats = activeSeats,
                MaxUserSeats = tenant?.MaxUserSeats ?? 5
            };

            return View(viewModel);
        }

        // POST: /Admin/InviteUser
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> InviteUser(InviteUserViewModel model)
        {
            var currentTenantId = _tenantContext.CurrentTenantId;
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "admin";

            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "Please provide a valid employee email and role.";
                return RedirectToAction(nameof(Invitations));
            }

            var result = await _invitationService.CreateInvitationAsync(currentTenantId, model.Email, model.CompanyRole, currentUserId);
            if (!result.Succeeded)
            {
                TempData["ErrorMessage"] = string.Join(" ", result.Errors);
                return RedirectToAction(nameof(Invitations));
            }

            var inviteUrl = Url.Action("AcceptInvitation", "Account", new { token = result.RawToken }, Request.Scheme);
            TempData["SuccessMessage"] = $"Invitation generated for {model.Email}.";
            TempData["InviteUrl"] = inviteUrl;

            return RedirectToAction(nameof(Invitations));
        }

        // POST: /Admin/RevokeInvitation
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RevokeInvitation(Guid invitationId)
        {
            var currentTenantId = _tenantContext.CurrentTenantId;
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "admin";

            var success = await _invitationService.RevokeInvitationAsync(currentTenantId, invitationId, currentUserId);
            if (success)
            {
                TempData["SuccessMessage"] = "Invitation revoked successfully.";
            }
            else
            {
                TempData["ErrorMessage"] = "Unable to revoke invitation. It may have already been accepted or removed.";
            }

            return RedirectToAction(nameof(Invitations));
        }

        // GET: /Admin/Settings
        public async Task<IActionResult> Settings()
        {
            var currentTenantId = _tenantContext.CurrentTenantId;
            var tenant = await _context.Tenants
                .IgnoreQueryFilters()
                .Include(t => t.Settings)
                .FirstOrDefaultAsync(t => t.TenantId == currentTenantId);

            if (tenant == null)
            {
                return NotFound("Tenant not found.");
            }

            var vm = new CompanySettingsViewModel
            {
                TenantId = tenant.TenantId,
                CompanyName = tenant.CompanyName,
                Slug = tenant.Slug,
                ContactEmail = tenant.ContactEmail,
                Phone = tenant.Phone,
                CurrencyCode = tenant.Settings?.CurrencyCode ?? tenant.DefaultCurrency ?? "INR",
                CurrencySymbol = tenant.Settings?.CurrencySymbol ?? "₹",
                DateFormat = tenant.Settings?.DateFormat ?? "dd/MM/yyyy",
                TimeZoneId = tenant.Settings?.TimeZoneId ?? "India Standard Time",
                FiscalYearStartMonth = tenant.Settings?.FiscalYearStartMonth ?? 4,
                LeadAssignmentMode = tenant.Settings?.LeadAssignmentMode ?? "Manual",
                RestrictLeadExport = tenant.Settings?.RestrictLeadExport ?? true
            };

            return View(vm);
        }

        // POST: /Admin/Settings
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Settings(CompanySettingsViewModel model)
        {
            var currentTenantId = _tenantContext.CurrentTenantId;
            if (model.TenantId != currentTenantId)
            {
                return Forbid();
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var tenant = await _context.Tenants
                .IgnoreQueryFilters()
                .Include(t => t.Settings)
                .FirstOrDefaultAsync(t => t.TenantId == currentTenantId);

            if (tenant == null)
            {
                return NotFound("Tenant not found.");
            }

            tenant.CompanyName = model.CompanyName.Trim();
            tenant.ContactEmail = model.ContactEmail.Trim();
            tenant.Phone = model.Phone?.Trim();
            tenant.UpdatedAt = DateTime.UtcNow;

            if (tenant.Settings == null)
            {
                tenant.Settings = new TenantSetting
                {
                    TenantId = tenant.TenantId,
                    CurrencyCode = model.CurrencyCode.Trim(),
                    CurrencySymbol = model.CurrencySymbol.Trim(),
                    DateFormat = model.DateFormat.Trim(),
                    TimeZoneId = model.TimeZoneId.Trim(),
                    FiscalYearStartMonth = model.FiscalYearStartMonth,
                    LeadAssignmentMode = model.LeadAssignmentMode,
                    RestrictLeadExport = model.RestrictLeadExport,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.TenantSettings.Add(tenant.Settings);
            }
            else
            {
                tenant.Settings.CurrencyCode = model.CurrencyCode.Trim();
                tenant.Settings.CurrencySymbol = model.CurrencySymbol.Trim();
                tenant.Settings.DateFormat = model.DateFormat.Trim();
                tenant.Settings.TimeZoneId = model.TimeZoneId.Trim();
                tenant.Settings.FiscalYearStartMonth = model.FiscalYearStartMonth;
                tenant.Settings.LeadAssignmentMode = model.LeadAssignmentMode;
                tenant.Settings.RestrictLeadExport = model.RestrictLeadExport;
                tenant.Settings.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();

            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "admin";
            await _auditService.LogAsync(currentUserId, "TenantSetting", tenant.TenantId.ToString(), "CompanySettingsUpdated",
                new { tenant.CompanyName, tenant.ContactEmail, model.CurrencySymbol, model.LeadAssignmentMode });

            TempData["SuccessMessage"] = "Company preferences and workspace settings saved successfully.";
            return RedirectToAction(nameof(Settings));
        }
    }
}
