using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AI_Powered_CRM.Data;
using AI_Powered_CRM.Models;
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

        public AdminController(
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            IAuditService auditService,
            ApplicationDbContext context,
            ITenantContext tenantContext)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _auditService = auditService;
            _context = context;
            _tenantContext = tenantContext;
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
    }
}
