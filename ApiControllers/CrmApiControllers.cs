using System;
using System.Security.Claims;
using System.Threading.Tasks;
using AI_Powered_CRM.AI;
using AI_Powered_CRM.DTOs;
using AI_Powered_CRM.Models.Enums;
using AI_Powered_CRM.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AI_Powered_CRM.ApiControllers
{
    [ApiController]
    [Route("api/v1/[controller]")]
    [Authorize]
    public class LeadsController : ControllerBase
    {
        private readonly ILeadService _leadService;
        private readonly IActivityService _activityService;
        private readonly IFollowUpService _followUpService;

        public LeadsController(ILeadService leadService, IActivityService activityService, IFollowUpService followUpService)
        {
            _leadService = leadService;
            _activityService = activityService;
            _followUpService = followUpService;
        }

        private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        private string GetUserRole() => User.IsInRole("Admin") ? "Admin" : (User.IsInRole("Manager") ? "Manager" : "SalesExecutive");

        [HttpGet]
        public async Task<IActionResult> GetLeads([FromQuery] string? search, [FromQuery] LeadStatus? status, [FromQuery] LeadPriority? priority, [FromQuery] string? source, [FromQuery] int page = 1, [FromQuery] int pageSize = 15)
        {
            var (leads, total) = await _leadService.GetPagedLeadsAsync(search, status, priority, source, GetUserId(), GetUserRole(), page, pageSize);
            return Ok(new { total, page, pageSize, data = leads });
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetLead(int id)
        {
            var lead = await _leadService.GetLeadDetailsAsync(id, GetUserId(), GetUserRole());
            if (lead == null) return NotFound(new { message = "Lead not found or unauthorized" });
            return Ok(lead);
        }

        [HttpPost]
        public async Task<IActionResult> CreateLead([FromBody] CreateLeadDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var lead = await _leadService.CreateLeadAsync(dto, GetUserId());
            return CreatedAtAction(nameof(GetLead), new { id = lead.LeadId }, new { leadId = lead.LeadId, status = lead.Status.ToString(), message = "Lead created successfully" });
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateLead(int id, [FromBody] UpdateLeadDto dto)
        {
            if (id != dto.LeadId) return BadRequest(new { message = "ID mismatch" });
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var success = await _leadService.UpdateLeadAsync(dto, GetUserId(), GetUserRole());
            if (!success) return Forbid();
            return Ok(new { message = "Lead updated successfully" });
        }

        [HttpPost("{id}/archive")]
        public async Task<IActionResult> ArchiveLead(int id)
        {
            var success = await _leadService.ArchiveLeadAsync(id, GetUserId(), GetUserRole());
            if (!success) return Forbid();
            return Ok(new { message = "Lead archived successfully" });
        }

        [HttpPost("{id}/assign")]
        public async Task<IActionResult> AssignLead(int id, [FromBody] AssignLeadDto dto)
        {
            var success = await _leadService.AssignLeadAsync(id, dto.NewOwnerUserId, GetUserId(), GetUserRole());
            if (!success) return Forbid();
            return Ok(new { message = "Lead assigned successfully" });
        }

        [HttpGet("{id}/activities")]
        public async Task<IActionResult> GetActivities(int id)
        {
            var activities = await _activityService.GetActivitiesAsync(id);
            return Ok(activities);
        }

        [HttpPost("{id}/activities")]
        public async Task<IActionResult> AddActivity(int id, [FromBody] AddActivityDto dto)
        {
            dto.LeadId = id;
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var activity = await _activityService.AddActivityAsync(dto, GetUserId());
            return Ok(activity);
        }

        [HttpPost("{id}/followups")]
        public async Task<IActionResult> ScheduleFollowUp(int id, [FromBody] ScheduleFollowUpDto dto)
        {
            dto.LeadId = id;
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var followUp = await _followUpService.ScheduleFollowUpAsync(dto, GetUserId());
            return Ok(followUp);
        }
    }

    [ApiController]
    [Route("api/v1/[controller]")]
    [Authorize]
    public class FollowUpsController : ControllerBase
    {
        private readonly IFollowUpService _followUpService;

        public FollowUpsController(IFollowUpService followUpService)
        {
            _followUpService = followUpService;
        }

        private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        private string GetUserRole() => User.IsInRole("Admin") ? "Admin" : (User.IsInRole("Manager") ? "Manager" : "SalesExecutive");

        [HttpGet]
        public async Task<IActionResult> GetFollowUps([FromQuery] FollowUpStatus? status)
        {
            var list = await _followUpService.GetFollowUpsAsync(GetUserId(), GetUserRole(), status);
            return Ok(list);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Reschedule(int id, [FromBody] RescheduleFollowUpDto dto)
        {
            dto.FollowUpId = id;
            var success = await _followUpService.RescheduleFollowUpAsync(id, dto.NewDueAt, dto.Reason, GetUserId());
            if (!success) return NotFound();
            return Ok(new { message = "Follow-up rescheduled" });
        }

        [HttpPost("{id}/complete")]
        public async Task<IActionResult> Complete(int id, [FromBody] CompleteFollowUpDto dto)
        {
            dto.FollowUpId = id;
            var success = await _followUpService.CompleteFollowUpAsync(id, dto.OutcomeNotes, GetUserId());
            if (!success) return NotFound();
            return Ok(new { message = "Follow-up marked complete" });
        }
    }

    [ApiController]
    [Route("api/v1/ai")]
    [Authorize]
    public class AiController : ControllerBase
    {
        private readonly ILeadService _leadService;
        private readonly IAIService _aiService;

        public AiController(ILeadService leadService, IAIService aiService)
        {
            _leadService = leadService;
            _aiService = aiService;
        }

        private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        private string GetUserRole() => User.IsInRole("Admin") ? "Admin" : (User.IsInRole("Manager") ? "Manager" : "SalesExecutive");

        [HttpPost("leads/{id}/analyze")]
        public async Task<IActionResult> AnalyzeLead(int id)
        {
            var insight = await _leadService.RunAIAnalysisAsync(id, GetUserId(), GetUserRole());
            if (insight == null) return NotFound(new { message = "Lead not found or unauthorized" });
            return Ok(insight);
        }

        [HttpPost("leads/{id}/draft-response")]
        public async Task<IActionResult> DraftResponse(int id, [FromBody] AIDraftResponseRequest? req)
        {
            var lead = await _leadService.GetLeadDetailsAsync(id, GetUserId(), GetUserRole());
            if (lead == null) return NotFound();

            var draft = await _aiService.GenerateDraftResponseAsync(
                lead,
                System.Linq.Enumerable.ToList(lead.Activities),
                req?.UserTone,
                req?.CustomInstructions
            );

            return Ok(new { draft });
        }
    }

    [ApiController]
    [Route("api/v1/[controller]")]
    [Authorize]
    public class DashboardController : ControllerBase
    {
        private readonly IReportService _reportService;

        public DashboardController(IReportService reportService)
        {
            _reportService = reportService;
        }

        [HttpGet("summary")]
        public async Task<IActionResult> GetSummary()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var role = User.IsInRole("Admin") ? "Admin" : (User.IsInRole("Manager") ? "Manager" : "SalesExecutive");
            var summary = await _reportService.GetDashboardSummaryAsync(userId, role);
            return Ok(summary);
        }
    }

    [ApiController]
    [Route("api/v1/[controller]")]
    [Authorize]
    public class ReportsController : ControllerBase
    {
        private readonly IReportService _reportService;

        public ReportsController(IReportService reportService)
        {
            _reportService = reportService;
        }

        [HttpGet("conversion")]
        public async Task<IActionResult> GetConversion()
        {
            return Ok(await _reportService.GetConversionReportAsync());
        }

        [HttpGet("sources")]
        public async Task<IActionResult> GetSources()
        {
            return Ok(await _reportService.GetSourcePerformanceAsync());
        }

        [HttpGet("staff")]
        public async Task<IActionResult> GetStaff()
        {
            return Ok(await _reportService.GetStaffPerformanceAsync());
        }
    }

    [ApiController]
    [Route("api/v1/[controller]")]
    [Authorize]
    public class NotificationsController : ControllerBase
    {
        private readonly INotificationService _notificationService;

        public NotificationsController(INotificationService notificationService)
        {
            _notificationService = notificationService;
        }

        private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        [HttpGet]
        public async Task<IActionResult> GetNotifications([FromQuery] bool unreadOnly = false)
        {
            var list = await _notificationService.GetUserNotificationsAsync(GetUserId(), 20, unreadOnly);
            return Ok(list);
        }

        [HttpPost("{id}/read")]
        public async Task<IActionResult> MarkRead(int id)
        {
            var success = await _notificationService.MarkAsReadAsync(id, GetUserId());
            if (!success) return NotFound();
            return Ok(new { success = true });
        }
    }
}
