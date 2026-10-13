using System;
using System.Security.Claims;
using System.Threading.Tasks;
using AI_Powered_CRM.AI;
using AI_Powered_CRM.DTOs;
using AI_Powered_CRM.Models.Enums;
using AI_Powered_CRM.Security;
using AI_Powered_CRM.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AI_Powered_CRM.ApiControllers
{
    // =========================================================================
    // API: /api/v1/leads
    // =========================================================================
    [ApiController]
    [Route("api/v1/[controller]")]
    [Authorize]
    public class LeadsController : ControllerBase
    {
        private readonly ILeadService         _leadService;
        private readonly IActivityService     _activityService;
        private readonly IFollowUpService     _followUpService;
        private readonly IAuthorizationService _authService;

        public LeadsController(
            ILeadService          leadService,
            IActivityService      activityService,
            IFollowUpService      followUpService,
            IAuthorizationService authService)
        {
            _leadService     = leadService;
            _activityService = activityService;
            _followUpService = followUpService;
            _authService     = authService;
        }

        private string GetUserId()   => User.FindFirstValue(ClaimTypes.NameIdentifier)
                                        ?? throw new InvalidOperationException("Not authenticated.");
        private string GetUserRole() => User.IsInRole("Admin")   ? "Admin"
                                      : User.IsInRole("Manager") ? "Manager"
                                                                  : "SalesExecutive";

        // GET /api/v1/leads
        [HttpGet]
        public async Task<IActionResult> GetLeads(
            [FromQuery] string?       search,
            [FromQuery] LeadStatus?   status,
            [FromQuery] LeadPriority? priority,
            [FromQuery] string?       source,
            [FromQuery] int           page     = 1,
            [FromQuery] int           pageSize = 15)
        {
            var (leads, total) = await _leadService.GetPagedLeadsAsync(
                search, status, priority, source, GetUserId(), GetUserRole(), page, pageSize);
            return Ok(new { total, page, pageSize, data = leads });
        }

        // GET /api/v1/leads/{id}
        [HttpGet("{id}")]
        public async Task<IActionResult> GetLead(int id)
        {
            var lead = await _leadService.GetLeadDetailsAsync(id, GetUserId(), GetUserRole());
            if (lead == null) return NotFound(new { message = "Lead not found or unauthorized." });
            return Ok(lead);
        }

        // POST /api/v1/leads
        [HttpPost]
        public async Task<IActionResult> CreateLead([FromBody] CreateLeadDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var currentUserId = GetUserId();
            var currentRole   = GetUserRole();

            // SalesExecutive cannot create leads for other owners
            if (currentRole == "SalesExecutive")
                dto.OwnerUserId = currentUserId;

            var lead = await _leadService.CreateLeadAsync(dto, currentUserId);
            return CreatedAtAction(nameof(GetLead), new { id = lead.LeadId },
                new { leadId = lead.LeadId, status = lead.Status.ToString(), message = "Lead created." });
        }

        // PUT /api/v1/leads/{id}
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateLead(int id, [FromBody] UpdateLeadDto dto)
        {
            if (id != dto.LeadId) return BadRequest(new { message = "ID mismatch." });
            if (!ModelState.IsValid) return BadRequest(ModelState);

            // Resource ownership check
            var auth = await _authService.AuthorizeAsync(User, id, new LeadOwnershipRequirement());
            if (!auth.Succeeded) return Forbid();

            var success = await _leadService.UpdateLeadAsync(dto, GetUserId(), GetUserRole());
            if (!success) return Forbid();
            return Ok(new { message = "Lead updated." });
        }

        // POST /api/v1/leads/{id}/archive  (Admin/Manager only)
        [HttpPost("{id}/archive")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> ArchiveLead(int id)
        {
            var success = await _leadService.ArchiveLeadAsync(id, GetUserId(), GetUserRole());
            if (!success) return Forbid();
            return Ok(new { message = "Lead archived." });
        }

        // POST /api/v1/leads/{id}/assign  (Admin/Manager only)
        [HttpPost("{id}/assign")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> AssignLead(int id, [FromBody] AssignLeadDto dto)
        {
            var success = await _leadService.AssignLeadAsync(id, dto.NewOwnerUserId, GetUserId(), GetUserRole());
            if (!success) return Forbid();
            return Ok(new { message = "Lead assigned." });
        }

        // GET /api/v1/leads/{id}/activities
        [HttpGet("{id}/activities")]
        public async Task<IActionResult> GetActivities(int id)
        {
            // Ownership check ensures SalesExecutive can only see their own lead's activities
            var auth = await _authService.AuthorizeAsync(User, id, new LeadOwnershipRequirement());
            if (!auth.Succeeded) return Forbid();

            var activities = await _activityService.GetActivitiesAsync(id, GetUserId(), GetUserRole());
            return Ok(activities);
        }

        // POST /api/v1/leads/{id}/activities
        [HttpPost("{id}/activities")]
        public async Task<IActionResult> AddActivity(int id, [FromBody] AddActivityDto dto)
        {
            dto.LeadId = id;
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var auth = await _authService.AuthorizeAsync(User, id, new LeadOwnershipRequirement());
            if (!auth.Succeeded) return Forbid();

            try
            {
                var activity = await _activityService.AddActivityAsync(dto, GetUserId(), GetUserRole());
                return Ok(activity);
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
        }

        // POST /api/v1/leads/{id}/followups
        [HttpPost("{id}/followups")]
        public async Task<IActionResult> ScheduleFollowUp(int id, [FromBody] ScheduleFollowUpDto dto)
        {
            dto.LeadId = id;
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var auth = await _authService.AuthorizeAsync(User, id, new LeadOwnershipRequirement());
            if (!auth.Succeeded) return Forbid();

            try
            {
                var followUp = await _followUpService.ScheduleFollowUpAsync(dto, GetUserId(), GetUserRole());
                return Ok(followUp);
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
        }
    }

    // =========================================================================
    // API: /api/v1/followups
    // =========================================================================
    [ApiController]
    [Route("api/v1/[controller]")]
    [Authorize]
    public class FollowUpsController : ControllerBase
    {
        private readonly IFollowUpService     _followUpService;
        private readonly IAuthorizationService _authService;

        public FollowUpsController(
            IFollowUpService      followUpService,
            IAuthorizationService authService)
        {
            _followUpService = followUpService;
            _authService     = authService;
        }

        private string GetUserId()   => User.FindFirstValue(ClaimTypes.NameIdentifier)
                                        ?? throw new InvalidOperationException("Not authenticated.");
        private string GetUserRole() => User.IsInRole("Admin")   ? "Admin"
                                      : User.IsInRole("Manager") ? "Manager"
                                                                  : "SalesExecutive";

        // GET /api/v1/followups
        [HttpGet]
        public async Task<IActionResult> GetFollowUps([FromQuery] FollowUpStatus? status)
        {
            var list = await _followUpService.GetFollowUpsAsync(GetUserId(), GetUserRole(), status);
            return Ok(list);
        }

        // PUT /api/v1/followups/{id}
        [HttpPut("{id}")]
        public async Task<IActionResult> Reschedule(int id, [FromBody] RescheduleFollowUpDto dto)
        {
            dto.FollowUpId = id;

            // Ownership check via resource handler
            var auth = await _authService.AuthorizeAsync(User, id, new FollowUpOwnershipRequirement());
            if (!auth.Succeeded) return Forbid();

            var success = await _followUpService.RescheduleFollowUpAsync(
                id, dto.NewDueAt, dto.Reason, GetUserId(), GetUserRole());

            if (!success) return NotFound();
            return Ok(new { message = "Follow-up rescheduled." });
        }

        // POST /api/v1/followups/{id}/complete
        [HttpPost("{id}/complete")]
        public async Task<IActionResult> Complete(int id, [FromBody] CompleteFollowUpDto dto)
        {
            dto.FollowUpId = id;

            // Ownership check
            var auth = await _authService.AuthorizeAsync(User, id, new FollowUpOwnershipRequirement());
            if (!auth.Succeeded) return Forbid();

            var success = await _followUpService.CompleteFollowUpAsync(
                id, dto.OutcomeNotes, GetUserId(), GetUserRole());

            if (!success) return NotFound();
            return Ok(new { message = "Follow-up completed." });
        }
    }

    // =========================================================================
    // API: /api/v1/ai
    // =========================================================================
    [ApiController]
    [Route("api/v1/ai")]
    [Authorize]
    public class AiController : ControllerBase
    {
        private readonly ILeadService         _leadService;
        private readonly IAIService           _aiService;
        private readonly IAuthorizationService _authService;

        public AiController(
            ILeadService          leadService,
            IAIService            aiService,
            IAuthorizationService authService)
        {
            _leadService = leadService;
            _aiService   = aiService;
            _authService = authService;
        }

        private string GetUserId()   => User.FindFirstValue(ClaimTypes.NameIdentifier)
                                        ?? throw new InvalidOperationException("Not authenticated.");
        private string GetUserRole() => User.IsInRole("Admin")   ? "Admin"
                                      : User.IsInRole("Manager") ? "Manager"
                                                                  : "SalesExecutive";

        // POST /api/v1/ai/leads/{id}/analyze
        [HttpPost("leads/{id}/analyze")]
        public async Task<IActionResult> AnalyzeLead(int id)
        {
            var auth = await _authService.AuthorizeAsync(User, id, new LeadOwnershipRequirement());
            if (!auth.Succeeded) return Forbid();

            var insight = await _leadService.RunAIAnalysisAsync(id, GetUserId(), GetUserRole());
            if (insight == null) return NotFound(new { message = "Lead not found." });
            return Ok(insight);
        }

        // POST /api/v1/ai/leads/{id}/draft-response
        [HttpPost("leads/{id}/draft-response")]
        public async Task<IActionResult> DraftResponse(int id, [FromBody] AIDraftResponseRequest? req)
        {
            var auth = await _authService.AuthorizeAsync(User, id, new LeadOwnershipRequirement());
            if (!auth.Succeeded) return Forbid();

            var lead = await _leadService.GetLeadDetailsAsync(id, GetUserId(), GetUserRole());
            if (lead == null) return NotFound();

            var draft = await _aiService.GenerateDraftResponseAsync(
                lead,
                System.Linq.Enumerable.ToList(lead.Activities),
                req?.UserTone,
                req?.CustomInstructions);

            return Ok(new { draft });
        }
    }

    // =========================================================================
    // API: /api/v1/dashboard
    // =========================================================================
    [ApiController]
    [Route("api/v1/[controller]")]
    [Authorize]
    public class DashboardController : ControllerBase
    {
        private readonly IReportService _reportService;
        public DashboardController(IReportService reportService) => _reportService = reportService;

        [HttpGet("summary")]
        public async Task<IActionResult> GetSummary()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var role   = User.IsInRole("Admin") ? "Admin" : User.IsInRole("Manager") ? "Manager" : "SalesExecutive";
            return Ok(await _reportService.GetDashboardSummaryAsync(userId, role));
        }
    }

    // =========================================================================
    // API: /api/v1/reports  (Admin/Manager only for staff & conversion reports)
    // =========================================================================
    [ApiController]
    [Route("api/v1/[controller]")]
    [Authorize]
    public class ReportsController : ControllerBase
    {
        private readonly IReportService _reportService;
        public ReportsController(IReportService reportService) => _reportService = reportService;

        [HttpGet("conversion")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> GetConversion()
            => Ok(await _reportService.GetConversionReportAsync());

        [HttpGet("sources")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> GetSources()
            => Ok(await _reportService.GetSourcePerformanceAsync());

        [HttpGet("staff")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> GetStaff()
            => Ok(await _reportService.GetStaffPerformanceAsync());
    }

    // =========================================================================
    // API: /api/v1/notifications
    // =========================================================================
    [ApiController]
    [Route("api/v1/[controller]")]
    [Authorize]
    public class NotificationsController : ControllerBase
    {
        private readonly INotificationService _notificationService;
        public NotificationsController(INotificationService n) => _notificationService = n;

        private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)
                                      ?? throw new InvalidOperationException("Not authenticated.");

        [HttpGet]
        public async Task<IActionResult> GetNotifications([FromQuery] bool unreadOnly = false)
            => Ok(await _notificationService.GetUserNotificationsAsync(GetUserId(), 20, unreadOnly));

        [HttpPost("{id}/read")]
        public async Task<IActionResult> MarkRead(int id)
        {
            var success = await _notificationService.MarkAsReadAsync(id, GetUserId());
            if (!success) return NotFound();
            return Ok(new { success = true });
        }
    }
}
