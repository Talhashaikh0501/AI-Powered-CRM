using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AI_Powered_CRM.Models;
using AI_Powered_CRM.Services.Interfaces;
using AI_Powered_CRM.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AI_Powered_CRM.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly IAuditService _auditService;

        public AdminController(
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            IAuditService auditService)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _auditService = auditService;
        }

        // GET: /Admin/Users
        public async Task<IActionResult> Users()
        {
            var users = await _userManager.Users.OrderBy(u => u.UserName).ToListAsync();
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
            var user = await _userManager.FindByIdAsync(id);
            if (user != null)
            {
                user.IsActive = !user.IsActive;
                await _userManager.UpdateAsync(user);
                await _auditService.LogAsync(User.Identity?.Name, "User", user.Id, "ToggleStatus", new { user.IsActive });
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
