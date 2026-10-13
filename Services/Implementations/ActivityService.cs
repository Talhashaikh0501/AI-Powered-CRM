using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AI_Powered_CRM.Data;
using AI_Powered_CRM.DTOs;
using AI_Powered_CRM.Models.Entities;
using AI_Powered_CRM.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AI_Powered_CRM.Services.Implementations
{
    public class ActivityService : IActivityService
    {
        private readonly ApplicationDbContext _context;
        private readonly IAuditService _auditService;

        public ActivityService(ApplicationDbContext context, IAuditService auditService)
        {
            _context = context;
            _auditService = auditService;
        }

        public async Task<LeadActivity> AddActivityAsync(AddActivityDto dto, string currentUserId)
        {
            var lead = await _context.Leads.FirstOrDefaultAsync(l => l.LeadId == dto.LeadId && !l.IsArchived);
            if (lead == null) throw new InvalidOperationException("Lead not found or archived.");

            var activity = new LeadActivity
            {
                LeadId = dto.LeadId,
                UserId = currentUserId,
                Type = dto.Type,
                Description = dto.Description.Trim(),
                OccurredAt = DateTime.UtcNow
            };

            _context.LeadActivities.Add(activity);
            lead.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await _auditService.LogAsync(currentUserId, "LeadActivity", activity.ActivityId.ToString(), "Added", dto);

            return activity;
        }

        public async Task<List<LeadActivity>> GetActivitiesAsync(int leadId)
        {
            return await _context.LeadActivities
                .Include(a => a.User)
                .Where(a => a.LeadId == leadId)
                .OrderByDescending(a => a.OccurredAt)
                .ToListAsync();
        }
    }
}
