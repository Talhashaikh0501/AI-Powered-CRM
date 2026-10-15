using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AI_Powered_CRM.Data;
using AI_Powered_CRM.Models;
using AI_Powered_CRM.Models.Entities;
using AI_Powered_CRM.Models.Enums;
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
        private readonly ISupportAccessService _supportAccessService;

        public AdminController(
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            IAuditService auditService,
            ApplicationDbContext context,
            ITenantContext tenantContext,
            ITenantInvitationService invitationService,
            ISubscriptionQuotaService quotaService,
            ISupportAccessService supportAccessService)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _auditService = auditService;
            _context = context;
            _tenantContext = tenantContext;
            _invitationService = invitationService;
            _quotaService = quotaService;
            _supportAccessService = supportAccessService;
        }

        // GET: /Admin/Users
        public async Task<IActionResult> Users(string? search = null, string? roleFilter = null)
        {
            var currentTenantId = _tenantContext.CurrentTenantId;
            var tenant = await _context.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.TenantId == currentTenantId);

            var memberships = await _context.TenantMemberships
                .Where(m => m.TenantId == currentTenantId)
                .ToListAsync();

            var activeSeats = memberships.Count(m => m.IsActive);
            var activeAdminCount = memberships.Count(m => m.IsActive && m.CompanyRole == CompanyRole.CompanyAdmin);
            var tenantUserIds = memberships.Select(m => m.UserId).ToList();

            var usersQuery = _userManager.Users
                .Where(u => tenantUserIds.Contains(u.Id));

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                usersQuery = usersQuery.Where(u =>
                    (u.FullName != null && u.FullName.ToLower().Contains(term)) ||
                    (u.UserName != null && u.UserName.ToLower().Contains(term)) ||
                    (u.Email != null && u.Email.ToLower().Contains(term)));
            }

            var users = await usersQuery.OrderBy(u => u.UserName).ToListAsync();

            var list = new List<UserWithRoleDto>();
            foreach (var u in users)
            {
                var mem = memberships.FirstOrDefault(m => m.UserId == u.Id);
                var compRole = mem?.CompanyRole ?? CompanyRole.SalesExecutive;

                if (!string.IsNullOrWhiteSpace(roleFilter) && !string.Equals(compRole.ToString(), roleFilter, System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var roles = await _userManager.GetRolesAsync(u);
                var isLastAdmin = compRole == CompanyRole.CompanyAdmin && (mem?.IsActive ?? false) && activeAdminCount <= 1;

                list.Add(new UserWithRoleDto
                {
                    Id = u.Id,
                    UserName = u.UserName ?? string.Empty,
                    FullName = u.FullName ?? u.UserName ?? "User",
                    Email = u.Email ?? string.Empty,
                    Role = roles.FirstOrDefault() ?? compRole.ToString(),
                    CompanyRole = compRole,
                    IsActive = (mem?.IsActive ?? false) && u.IsActive,
                    IsLastAdmin = isLastAdmin,
                    CreatedAt = u.CreatedAt
                });
            }

            var vm = new AdminUsersViewModel
            {
                Users = list,
                SearchTerm = search,
                RoleFilter = roleFilter,
                ActiveUserSeats = activeSeats,
                MaxUserSeats = tenant?.MaxUserSeats ?? 5
            };

            return View(vm);
        }

        // POST: /Admin/ChangeUserRole
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangeUserRole(string userId, CompanyRole newRole)
        {
            var currentTenantId = _tenantContext.CurrentTenantId;
            var membership = await _context.TenantMemberships
                .FirstOrDefaultAsync(m => m.TenantId == currentTenantId && m.UserId == userId);

            if (membership == null)
            {
                TempData["ErrorMessage"] = "User not found in current workspace.";
                return RedirectToAction(nameof(Users));
            }

            if (membership.CompanyRole == CompanyRole.CompanyAdmin && newRole != CompanyRole.CompanyAdmin)
            {
                var activeAdmins = await _context.TenantMemberships
                    .CountAsync(m => m.TenantId == currentTenantId && m.IsActive && m.CompanyRole == CompanyRole.CompanyAdmin);

                if (activeAdmins <= 1)
                {
                    TempData["ErrorMessage"] = "Cannot change the role of the last active Company Administrator in this workspace.";
                    return RedirectToAction(nameof(Users));
                }
            }

            var oldRole = membership.CompanyRole;
            membership.CompanyRole = newRole;

            var user = await _userManager.FindByIdAsync(userId);
            if (user != null)
            {
                var existingRoles = await _userManager.GetRolesAsync(user);
                var crmRoles = new[] { "CompanyAdmin", "Admin", "Manager", "SalesExecutive" };
                var rolesToRemove = existingRoles.Where(r => crmRoles.Contains(r)).ToList();
                if (rolesToRemove.Any())
                {
                    await _userManager.RemoveFromRolesAsync(user, rolesToRemove);
                }

                var rolesToAdd = new List<string> { newRole.ToString() };
                if (newRole == CompanyRole.CompanyAdmin)
                {
                    rolesToAdd.Add("Admin");
                }

                foreach (var r in rolesToAdd)
                {
                    if (!await _roleManager.RoleExistsAsync(r))
                    {
                        await _roleManager.CreateAsync(new IdentityRole(r));
                    }
                }
                await _userManager.AddToRolesAsync(user, rolesToAdd);
                await _userManager.UpdateSecurityStampAsync(user);
            }

            await _context.SaveChangesAsync();

            var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            await _auditService.LogAsync(adminId, "TenantMembership", membership.MembershipId.ToString(), "UserRoleChanged",
                new { TargetUserId = userId, OldRole = oldRole.ToString(), NewRole = newRole.ToString() }, isSecurityCritical: true);

            TempData["SuccessMessage"] = $"Role for {user?.FullName ?? user?.UserName ?? "employee"} updated to {newRole}.";
            return RedirectToAction(nameof(Users));
        }

        // POST: /Admin/ToggleUserStatus
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleUserStatus(string id)
        {
            var currentTenantId = _tenantContext.CurrentTenantId;
            var membership = await _context.TenantMemberships
                .FirstOrDefaultAsync(m => m.TenantId == currentTenantId && m.UserId == id);

            if (membership == null)
            {
                return Forbid();
            }

            // Prevent deactivating the only remaining CompanyAdmin
            if (membership.IsActive && membership.CompanyRole == CompanyRole.CompanyAdmin)
            {
                var activeAdmins = await _context.TenantMemberships
                    .CountAsync(m => m.TenantId == currentTenantId && m.IsActive && m.CompanyRole == CompanyRole.CompanyAdmin);

                if (activeAdmins <= 1)
                {
                    TempData["ErrorMessage"] = "Cannot deactivate the only active Company Administrator in this workspace.";
                    return RedirectToAction(nameof(Users));
                }
            }

            membership.IsActive = !membership.IsActive;

            var user = await _userManager.FindByIdAsync(id);
            if (user != null)
            {
                // If member is being deactivated in their current workspace, also toggle or invalidate security stamp
                user.IsActive = membership.IsActive;
                await _userManager.UpdateAsync(user);
                await _userManager.UpdateSecurityStampAsync(user); // Invalidate active user session immediately

                var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                await _auditService.LogAsync(adminId, "User", user.Id, "ToggleStatus",
                    new { MembershipIsActive = membership.IsActive, user.UserName }, isSecurityCritical: true);
                TempData["SuccessMessage"] = $"Employee status updated to {(membership.IsActive ? "Active" : "Disabled")}.";
            }

            await _context.SaveChangesAsync();
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

        // GET: /Admin/SupportAccess
        public async Task<IActionResult> SupportAccess()
        {
            var currentTenantId = _tenantContext.CurrentTenantId;
            var tickets = await _supportAccessService.GetTicketsForTenantAsync(currentTenantId);
            return View(tickets);
        }

        // POST: /Admin/ApproveSupportAccess
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveSupportAccess(Guid ticketId)
        {
            var currentTenantId = _tenantContext.CurrentTenantId;
            var ticket = await _supportAccessService.GetTicketByIdAsync(ticketId);

            if (ticket == null || ticket.TenantId != currentTenantId)
            {
                TempData["ErrorMessage"] = "Support access ticket not found for this workspace.";
                return RedirectToAction(nameof(SupportAccess));
            }

            try
            {
                await _supportAccessService.ApproveTicketAsync(ticketId, TimeSpan.FromHours(4), User);
                TempData["SuccessMessage"] = $"Support access approved for 4 hours (Ticket: {ticket.TicketNumber}).";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Failed to approve ticket: {ex.Message}";
            }

            return RedirectToAction(nameof(SupportAccess));
        }

        // POST: /Admin/RevokeSupportAccess
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RevokeSupportAccess(Guid ticketId, string? reason)
        {
            var currentTenantId = _tenantContext.CurrentTenantId;
            var ticket = await _supportAccessService.GetTicketByIdAsync(ticketId);

            if (ticket == null || ticket.TenantId != currentTenantId)
            {
                TempData["ErrorMessage"] = "Support access ticket not found for this workspace.";
                return RedirectToAction(nameof(SupportAccess));
            }

            try
            {
                await _supportAccessService.RevokeTicketAsync(ticketId, reason ?? "Revoked by Customer Company Administrator", User);
                TempData["SuccessMessage"] = "Support access has been revoked immediately.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Failed to revoke ticket: {ex.Message}";
            }

            return RedirectToAction(nameof(SupportAccess));
        }
    }
}
