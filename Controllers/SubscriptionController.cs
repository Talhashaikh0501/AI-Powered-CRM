using System;
using System.Security.Claims;
using System.Threading.Tasks;
using AI_Powered_CRM.Data;
using AI_Powered_CRM.Models.Enums;
using AI_Powered_CRM.Security;
using AI_Powered_CRM.Services.Interfaces;
using AI_Powered_CRM.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AI_Powered_CRM.Controllers
{
    [Authorize]
    public class SubscriptionController : Controller
    {
        private readonly ISubscriptionBillingService _billingService;
        private readonly ITenantContext _tenantContext;
        private readonly ApplicationDbContext _db;
        private readonly IAuditService _auditService;
        private readonly ILogger<SubscriptionController> _logger;

        public SubscriptionController(
            ISubscriptionBillingService billingService,
            ITenantContext tenantContext,
            ApplicationDbContext db,
            IAuditService auditService,
            ILogger<SubscriptionController> logger)
        {
            _billingService = billingService;
            _tenantContext = tenantContext;
            _db = db;
            _auditService = auditService;
            _logger = logger;
        }

        // -------------------------------------------------------
        // GET /Subscription/Index
        // -------------------------------------------------------
        [Authorize(Policy = "TenantRequired")]
        public async Task<IActionResult> Index()
        {
            var currentTenantId = _tenantContext.CurrentTenantId;
            var vm = await _billingService.GetSubscriptionDashboardAsync(currentTenantId);

            if (vm == null)
            {
                return NotFound("Workspace subscription details not found.");
            }

            return View(vm);
        }

        // -------------------------------------------------------
        // GET /Subscription/Pricing
        // -------------------------------------------------------
        [AllowAnonymous]
        public async Task<IActionResult> Pricing()
        {
            SubscriptionTier currentTier = SubscriptionTier.FreeTrial;
            SubscriptionStatus currentStatus = SubscriptionStatus.Trialing;

            if (_tenantContext.IsResolved)
            {
                var tenant = await _db.Tenants
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(t => t.TenantId == _tenantContext.CurrentTenantId);

                if (tenant != null)
                {
                    currentTier = tenant.SubscriptionTier;
                    currentStatus = tenant.SubscriptionStatus;
                }
            }

            var plans = _billingService.GetAvailablePlans(currentTier);

            var vm = new PricingPageViewModel
            {
                CurrentTier = currentTier,
                CurrentStatus = currentStatus,
                Plans = plans,
                IsAnnualBilling = false,
                CurrencySymbol = "₹"
            };

            return View(vm);
        }

        // -------------------------------------------------------
        // GET /Subscription/Billing
        // -------------------------------------------------------
        [Authorize(Policy = "TenantRequired")]
        public async Task<IActionResult> Billing(string filter = "All")
        {
            var currentTenantId = _tenantContext.CurrentTenantId;
            var vm = await _billingService.GetBillingHistoryAsync(currentTenantId);
            vm.Filter = filter;

            return View(vm);
        }

        // -------------------------------------------------------
        // POST /Subscription/RequestPlanChange (Honest informational modal handler)
        // -------------------------------------------------------
        [HttpPost]
        [Authorize(Policy = "TenantRequired")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RequestPlanChange(SubscriptionTier targetTier, string billingCycle = "monthly")
        {
            var currentTenantId = _tenantContext.CurrentTenantId;
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "user";

            // Log user intent for analytics & customer success
            await _auditService.LogAsync(userId, "Subscription", currentTenantId.ToString(), "PlanChangeRequested",
                new { TargetTier = targetTier.ToString(), BillingCycle = billingCycle });

            _logger.LogInformation("User {UserId} in tenant {TenantId} requested plan change to {TargetTier} ({BillingCycle})",
                userId, currentTenantId, targetTier, billingCycle);

            TempData["InfoMessage"] = $"Thank you for your interest in the {targetTier} plan! Automated online payment gateway integration is launching in our final release. Our team has received your plan change request and will assist you.";

            return RedirectToAction(nameof(Index));
        }
    }
}
