using System.ComponentModel.DataAnnotations;
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
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AI_Powered_CRM.Controllers
{
    public class AccountController : Controller
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser>   _userManager;
        private readonly IAuditService                  _auditService;
        private readonly ICompanyOnboardingService      _onboardingService;
        private readonly ITenantInvitationService       _invitationService;
        private readonly ApplicationDbContext           _context;
        private readonly ITenantContext                 _tenantContext;
        private readonly ILogger<AccountController>     _logger;

        public AccountController(
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser>   userManager,
            IAuditService                  auditService,
            ICompanyOnboardingService      onboardingService,
            ITenantInvitationService       invitationService,
            ApplicationDbContext           context,
            ITenantContext                 tenantContext,
            ILogger<AccountController>     logger)
        {
            _signInManager     = signInManager;
            _userManager       = userManager;
            _auditService      = auditService;
            _onboardingService = onboardingService;
            _invitationService = invitationService;
            _context           = context;
            _tenantContext     = tenantContext;
            _logger            = logger;
        }

        // -------------------------------------------------------
        // GET /Account/Login
        // -------------------------------------------------------
        [HttpGet]
        [AllowAnonymous]
        public IActionResult Login(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true)
                return RedirectToAction("Index", "Dashboard");

            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        // -------------------------------------------------------
        // POST /Account/Login    (login rate-limit: 10 / 5 min)
        // -------------------------------------------------------
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("login")]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;

            if (!ModelState.IsValid)
                return View(model);

            // Resolve user – do NOT leak whether the account exists
            var user = await _userManager.FindByEmailAsync(model.Email);

            if (user == null || !user.IsActive)
            {
                // Generic message prevents user-enumeration
                ModelState.AddModelError(string.Empty, "Invalid email or password.");
                _logger.LogWarning("Login attempt for unknown / inactive account. Email hash: {Hash}",
                    model.Email.GetHashCode());
                // Audit – no credentials recorded
                await _auditService.LogAsync("anonymous", "Account", model.Email, "LoginFailed_UnknownUser");
                return View(model);
            }

            // Check lockout before attempting sign-in
            if (await _userManager.IsLockedOutAsync(user))
            {
                var lockoutEnd = await _userManager.GetLockoutEndDateAsync(user);
                _logger.LogWarning("Locked-out login attempt for user {UserId}", user.Id);
                await _auditService.LogAsync(user.Id, "Account", user.Id, "LoginFailed_LockedOut",
                    new { LockoutEnd = lockoutEnd });
                ModelState.AddModelError(string.Empty,
                    "Your account is temporarily locked. Please try again later.");
                return View(model);
            }

            var result = await _signInManager.PasswordSignInAsync(
                user.UserName ?? model.Email,
                model.Password,
                model.RememberMe,
                lockoutOnFailure: true   // ← triggers Identity lockout
            );

            if (result.Succeeded)
            {
                // Reset failed-access counter on success (Identity does this automatically;
                // we also log the successful login for audit trail)
                _logger.LogInformation("User {UserId} logged in successfully.", user.Id);
                await _auditService.LogAsync(user.Id, "Account", user.Id, "LoginSuccess");

                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                    return Redirect(returnUrl);

                return RedirectToAction("Index", "Dashboard");
            }

            if (result.IsLockedOut)
            {
                _logger.LogWarning("User {UserId} is now locked out after failed attempts.", user.Id);
                await _auditService.LogAsync(user.Id, "Account", user.Id, "AccountLockedOut");
                ModelState.AddModelError(string.Empty,
                    "Too many failed attempts. Your account is locked for 15 minutes.");
                return View(model);
            }

            // Failed login – increment lockout counter (done by Identity), log it
            var failCount = await _userManager.GetAccessFailedCountAsync(user);
            _logger.LogWarning("Failed login for user {UserId}. Attempt count: {Count}", user.Id, failCount);
            await _auditService.LogAsync(user.Id, "Account", user.Id, "LoginFailed",
                new { FailedAttempt = failCount });

            ModelState.AddModelError(string.Empty, "Invalid email or password.");
            return View(model);
        }

        // -------------------------------------------------------
        // POST /Account/Logout
        // -------------------------------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
            await _signInManager.SignOutAsync();
            _logger.LogInformation("User {UserId} logged out.", userId);
            await _auditService.LogAsync(userId, "Account", userId, "Logout");
            return RedirectToAction("Login", "Account");
        }

        // -------------------------------------------------------
        // GET /Account/RegisterCompany
        // -------------------------------------------------------
        [HttpGet]
        [AllowAnonymous]
        public IActionResult RegisterCompany()
        {
            if (User.Identity?.IsAuthenticated == true)
                return RedirectToAction("Index", "Dashboard");

            return View(new RegisterCompanyViewModel());
        }

        // -------------------------------------------------------
        // POST /Account/RegisterCompany
        // -------------------------------------------------------
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("login")]
        public async Task<IActionResult> RegisterCompany(RegisterCompanyViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var result = await _onboardingService.RegisterCompanyAsync(model);
            if (!result.Succeeded)
            {
                foreach (var err in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, err);
                }
                return View(model);
            }

            _logger.LogInformation("Company registered successfully: {CompanyName} (TenantId: {TenantId})",
                model.CompanyName, result.TenantId);

            var user = await _userManager.FindByEmailAsync(model.ContactEmail);
            if (user != null)
            {
                await _signInManager.SignInAsync(user, isPersistent: false);
                TempData["SuccessMessage"] = $"Welcome to your new workspace for {model.CompanyName}! Your setup is complete.";
                return RedirectToAction("Index", "Dashboard");
            }

            TempData["SuccessMessage"] = "Your company workspace was registered successfully. Please sign in.";
            return RedirectToAction(nameof(Login));
        }

        // -------------------------------------------------------
        // GET /Account/CheckSlug?slug=xyz
        // -------------------------------------------------------
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> CheckSlug(string slug)
        {
            if (string.IsNullOrWhiteSpace(slug))
                return Json(new { available = false, message = "Slug cannot be empty." });

            var isAvailable = await _onboardingService.IsSlugAvailableAsync(slug);
            return Json(new { available = isAvailable });
        }

        // -------------------------------------------------------
        // GET /Account/AcceptInvitation?token=xyz
        // -------------------------------------------------------
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> AcceptInvitation(string? token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                TempData["ErrorMessage"] = "Invalid invitation link.";
                return RedirectToAction(nameof(Login));
            }

            var invitation = await _invitationService.ValidateInvitationTokenAsync(token);
            if (invitation == null)
            {
                ViewBag.ErrorMessage = "This invitation is invalid, has expired, or has already been accepted.";
                return View("InvitationInvalid");
            }

            var model = new AcceptInvitationViewModel
            {
                Token = token,
                Email = invitation.Email,
                CompanyName = invitation.Tenant?.CompanyName ?? "Workspace",
                CompanyRole = invitation.CompanyRole
            };

            return View(model);
        }

        // -------------------------------------------------------
        // POST /Account/AcceptInvitation
        // -------------------------------------------------------
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("login")]
        public async Task<IActionResult> AcceptInvitation(AcceptInvitationViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var result = await _invitationService.AcceptInvitationAsync(model.Token, model.FullName, model.Password);
            if (!result.Succeeded)
            {
                foreach (var err in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, err);
                }
                return View(model);
            }

            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user != null)
            {
                await _signInManager.SignInAsync(user, isPersistent: false);
                TempData["SuccessMessage"] = $"Welcome aboard! You have joined {model.CompanyName}.";
                return RedirectToAction("Index", "Dashboard");
            }

            TempData["SuccessMessage"] = "Invitation accepted successfully. Please sign in with your password.";
            return RedirectToAction(nameof(Login));
        }

        // -------------------------------------------------------
        // POST /Account/SwitchTenant
        // -------------------------------------------------------
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SwitchTenant(Guid tenantId, string? returnUrl = null)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId) || tenantId == Guid.Empty)
            {
                return RedirectToAction("Index", "Dashboard");
            }

            var membership = await _context.TenantMemberships
                .IgnoreQueryFilters()
                .Include(m => m.Tenant)
                .FirstOrDefaultAsync(m => m.UserId == userId && m.TenantId == tenantId && m.IsActive);

            if (membership == null || membership.Tenant == null || !membership.Tenant.IsActive)
            {
                TempData["ErrorMessage"] = "You do not have active membership in the specified workspace.";
                return RedirectToAction("Index", "Dashboard");
            }

            Response.Cookies.Append(TenantResolutionMiddleware.ActiveTenantCookieName, tenantId.ToString(), new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Secure = Request.IsHttps,
                Expires = DateTimeOffset.UtcNow.AddDays(30)
            });

            await _auditService.LogAsync(userId, "Tenant", tenantId.ToString(), "WorkspaceSwitched",
                new { TargetTenantId = tenantId, TargetCompanyName = membership.Tenant.CompanyName });

            TempData["SuccessMessage"] = $"Active workspace switched to {membership.Tenant.CompanyName}.";

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return RedirectToAction("Index", "Dashboard");
        }

        // -------------------------------------------------------
        // GET /Account/Workspaces
        // -------------------------------------------------------
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> Workspaces()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToAction(nameof(Login));
            }

            var currentTenantId = _tenantContext.CurrentTenantId;

            var memberships = await _context.TenantMemberships
                .IgnoreQueryFilters()
                .Include(m => m.Tenant)
                .Where(m => m.UserId == userId && m.IsActive)
                .ToListAsync();

            var currentTenant = memberships.FirstOrDefault(m => m.TenantId == currentTenantId)?.Tenant;

            var vm = new SwitchTenantViewModel
            {
                CurrentTenantId = currentTenantId,
                CurrentTenantName = currentTenant?.CompanyName ?? "Current Workspace",
                Workspaces = memberships.Select(m => new UserWorkspaceItemDto
                {
                    TenantId = m.TenantId,
                    CompanyName = m.Tenant?.CompanyName ?? "Workspace",
                    Slug = m.Tenant?.Slug ?? string.Empty,
                    CompanyRole = m.CompanyRole.ToString(),
                    IsCurrent = m.TenantId == currentTenantId,
                    SubscriptionTier = m.Tenant?.SubscriptionTier.ToString() ?? "FreeTrial",
                    IsActive = m.IsActive && (m.Tenant?.IsActive ?? false)
                }).ToList()
            };

            return View(vm);
        }

        // -------------------------------------------------------
        // GET /Account/AccessDenied
        // -------------------------------------------------------
        [HttpGet]
        public IActionResult AccessDenied() => View();
    }

    // -------------------------------------------------------
    // View-model (kept in same file for parity with original)
    // -------------------------------------------------------
    public class LoginViewModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        public bool RememberMe { get; set; } = true;
    }
}
