using System.Linq;
using System.Threading.Tasks;
using AI_Powered_CRM.Data;
using AI_Powered_CRM.Models;
using AI_Powered_CRM.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AI_Powered_CRM.Controllers
{
    [Route("Platform")]
    public class PlatformAuthController : Controller
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ApplicationDbContext _context;
        private readonly IAuditService _auditService;
        private readonly ILogger<PlatformAuthController> _logger;

        public PlatformAuthController(
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            ApplicationDbContext context,
            IAuditService auditService,
            ILogger<PlatformAuthController> logger)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _context = context;
            _auditService = auditService;
            _logger = logger;
        }

        [HttpGet("Login")]
        [AllowAnonymous]
        public IActionResult Login(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                if (User.IsInRole("PlatformSuperAdmin") || User.IsInRole("SuperAdmin"))
                    return RedirectToAction("Index", "Platform");

                return RedirectToAction("Index", "Dashboard");
            }

            ViewData["ReturnUrl"] = returnUrl;
            return View("~/Views/PlatformAuth/Login.cshtml");
        }

        [HttpPost("Login")]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("login")]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;

            if (!ModelState.IsValid)
                return View("~/Views/PlatformAuth/Login.cshtml", model);

            var user = await _userManager.FindByEmailAsync(model.Email.Trim());
            if (user == null || !user.IsActive)
            {
                ModelState.AddModelError(string.Empty, "Invalid email or password.");
                await _auditService.LogAsync("anonymous", "PlatformAuth", model.Email, "PlatformLoginFailed");
                return View("~/Views/PlatformAuth/Login.cshtml", model);
            }

            var hasPlatformRole =
                await _userManager.IsInRoleAsync(user, "PlatformSuperAdmin") ||
                await _userManager.IsInRoleAsync(user, "SuperAdmin");

            if (!hasPlatformRole)
            {
                await _auditService.LogAsync(user.Id, "PlatformAuth", user.Id, "PlatformLoginBlocked_NonPlatformAccount");
                ModelState.AddModelError(string.Empty, "This account is not authorized for Platform SuperAdmin access.");
                return View("~/Views/PlatformAuth/Login.cshtml", model);
            }

            var previousIgnore = _context.IgnoreTenantFilter;
            bool hasTenantMembership;
            try
            {
                _context.IgnoreTenantFilter = true;
                hasTenantMembership = await _context.TenantMemberships
                    .IgnoreQueryFilters()
                    .AnyAsync(m => m.UserId == user.Id);
            }
            finally
            {
                _context.IgnoreTenantFilter = previousIgnore;
            }

            if (hasTenantMembership)
            {
                _logger.LogCritical("Platform login blocked because privileged account {UserId} has tenant membership.", user.Id);
                await _auditService.LogAsync(user.Id, "PlatformAuth", user.Id, "PlatformLoginBlocked_TenantMembershipCollision");
                ModelState.AddModelError(string.Empty, "This platform account is not configured correctly. Contact the system owner.");
                return View("~/Views/PlatformAuth/Login.cshtml", model);
            }

            if (await _userManager.IsLockedOutAsync(user))
            {
                ModelState.AddModelError(string.Empty, "Your account is temporarily locked. Please try again later.");
                await _auditService.LogAsync(user.Id, "PlatformAuth", user.Id, "PlatformLoginFailed_LockedOut");
                return View("~/Views/PlatformAuth/Login.cshtml", model);
            }

            var result = await _signInManager.PasswordSignInAsync(
                user.UserName ?? user.Email ?? model.Email,
                model.Password,
                model.RememberMe,
                lockoutOnFailure: true);

            if (result.Succeeded)
            {
                await _auditService.LogAsync(user.Id, "PlatformAuth", user.Id, "PlatformLoginSuccess");
                _logger.LogInformation("Platform SuperAdmin {UserId} signed in.", user.Id);

                if (!string.IsNullOrWhiteSpace(returnUrl) &&
                    Url.IsLocalUrl(returnUrl) &&
                    returnUrl.StartsWith("/Platform", System.StringComparison.OrdinalIgnoreCase))
                {
                    return Redirect(returnUrl);
                }

                return RedirectToAction("Index", "Platform");
            }

            if (result.IsLockedOut)
                ModelState.AddModelError(string.Empty, "Too many failed attempts. Your account is locked for 15 minutes.");
            else
                ModelState.AddModelError(string.Empty, "Invalid email or password.");

            await _auditService.LogAsync(user.Id, "PlatformAuth", user.Id,
                result.IsLockedOut ? "PlatformLoginLockedOut" : "PlatformLoginFailed");
            return View("~/Views/PlatformAuth/Login.cshtml", model);
        }
    }
}
