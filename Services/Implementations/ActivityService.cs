using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AI_Powered_CRM.Data;
using AI_Powered_CRM.DTOs;
using AI_Powered_CRM.Models.Entities;
using AI_Powered_CRM.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AI_Powered_CRM.Services.Implementations
{
    public class ActivityService : IActivityService
    {
        private readonly ApplicationDbContext _context;
        private readonly IAuditService        _auditService;
        private readonly ILogger<ActivityService> _logger;

        public ActivityService(
            ApplicationDbContext     context,
            IAuditService            auditService,
            ILogger<ActivityService> logger)
        {
            _context      = context;
            _auditService = auditService;
            _logger       = logger;
        }

        /// <summary>
        /// Adds an activity to a lead.
        /// Enforces ownership: SalesExecutive can only log activities on their own leads.
        /// Managers and Admins may log on any lead.
        /// </summary>
        public async Task<LeadActivity> AddActivityAsync(
            AddActivityDto dto,
            string currentUserId,
            string currentUserRole)
        {
            var lead = await _context.Leads
                .FirstOrDefaultAsync(l => l.LeadId == dto.LeadId && !l.IsArchived);

            if (lead == null)
                throw new InvalidOperationException("Lead not found or archived.");

            // Ownership check
            if (currentUserRole == "SalesExecutive" && lead.OwnerUserId != currentUserId)
            {
                _logger.LogWarning(
                    "Unauthorized activity attempt. User {UserId} tried to add activity to Lead {LeadId} owned by {OwnerId}.",
                    currentUserId, dto.LeadId, lead.OwnerUserId);
                throw new UnauthorizedAccessException("You do not have permission to log activities on this lead.");
            }

            var activity = new LeadActivity
            {
                LeadId      = dto.LeadId,
                UserId      = currentUserId,          // always from server context, never from client
                Type        = dto.Type,
                Description = dto.Description.Trim(),
                OccurredAt  = DateTime.UtcNow
            };

            _context.LeadActivities.Add(activity);
            lead.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await _auditService.LogAsync(
                currentUserId, "LeadActivity",
                activity.ActivityId.ToString(), "Added",
                new { dto.LeadId, dto.Type });

            return activity;
        }

        /// <summary>
        /// Returns activities for a lead, scoped to ownership for SalesExecutive role.
        /// </summary>
        public async Task<List<LeadActivity>> GetActivitiesAsync(
            int    leadId,
            string currentUserId,
            string currentUserRole)
        {
            // Verify the caller can see this lead at all
            var lead = await _context.Leads
                .AsNoTracking()
                .FirstOrDefaultAsync(l => l.LeadId == leadId && !l.IsArchived);

            if (lead == null)
                return new List<LeadActivity>();

            if (currentUserRole == "SalesExecutive" && lead.OwnerUserId != currentUserId)
                return new List<LeadActivity>(); // silently return empty (ownership enforced)

            return await _context.LeadActivities
                .Include(a => a.User)
                .Where(a => a.LeadId == leadId)
                .OrderByDescending(a => a.OccurredAt)
                .ToListAsync();
        }
    }
}
