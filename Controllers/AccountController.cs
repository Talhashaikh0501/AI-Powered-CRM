using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Threading.Tasks;
using AI_Powered_CRM.Models;
using AI_Powered_CRM.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;

namespace AI_Powered_CRM.Controllers
{
    public class AccountController : Controller
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser>   _userManager;
        private readonly IAuditService                  _auditService;
        private readonly ILogger<AccountController>     _logger;

        public AccountController(
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser>   userManager,
            IAuditService                  auditService,
            ILogger<AccountController>     logger)
        {
            _signInManager = signInManager;
            _userManager   = userManager;
            _auditService  = auditService;
            _logger        = logger;
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
