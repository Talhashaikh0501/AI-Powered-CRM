using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AI_Powered_CRM.Data;
using AI_Powered_CRM.DTOs;
using AI_Powered_CRM.Models.Entities;
using AI_Powered_CRM.Models.Enums;
using AI_Powered_CRM.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AI_Powered_CRM.Services.Implementations
{
    public class FollowUpService : IFollowUpService
    {
        private readonly ApplicationDbContext _context;
        private readonly IAuditService _auditService;
        private readonly INotificationService _notificationService;
        private readonly ILogger<FollowUpService> _logger;

        public FollowUpService(
            ApplicationDbContext context,
            IAuditService auditService,
            INotificationService notificationService,
            ILogger<FollowUpService> logger)
        {
            _context = context;
            _auditService = auditService;
            _notificationService = notificationService;
            _logger = logger;
        }

        public async Task<List<FollowUp>> GetFollowUpsAsync(string? currentUserId, string? role, FollowUpStatus? status = null)
        {
            var query = _context.FollowUps
                .Include(f => f.Lead)
                .Include(f => f.AssignedUser)
                .Where(f => !f.Lead.IsArchived);

            if (role == "SalesExecutive" && !string.IsNullOrEmpty(currentUserId))
            {
                query = query.Where(f => f.AssignedUserId == currentUserId);
            }

            if (status.HasValue)
            {
                query = query.Where(f => f.Status == status.Value);
            }

            return await query.OrderBy(f => f.DueAt).ToListAsync();
        }

        public async Task<List<FollowUp>> GetOverdueFollowUpsAsync(string? currentUserId, string? role)
        {
            var query = _context.FollowUps
                .Include(f => f.Lead)
                .Include(f => f.AssignedUser)
                .Where(f => !f.Lead.IsArchived && f.Status == FollowUpStatus.Pending && f.DueAt < DateTime.UtcNow);

            if (role == "SalesExecutive" && !string.IsNullOrEmpty(currentUserId))
            {
                query = query.Where(f => f.AssignedUserId == currentUserId);
            }

            return await query.OrderBy(f => f.DueAt).ToListAsync();
        }

        public async Task<List<FollowUp>> GetTodayFollowUpsAsync(string? currentUserId, string? role)
        {
            var startOfDay = DateTime.UtcNow.Date;
            var endOfDay = startOfDay.AddDays(1).AddTicks(-1);

            var query = _context.FollowUps
                .Include(f => f.Lead)
                .Include(f => f.AssignedUser)
                .Where(f => !f.Lead.IsArchived &&
                            f.Status == FollowUpStatus.Pending &&
                            f.DueAt >= startOfDay &&
                            f.DueAt <= endOfDay);

            if (role == "SalesExecutive" && !string.IsNullOrEmpty(currentUserId))
            {
                query = query.Where(f => f.AssignedUserId == currentUserId);
            }

            return await query.OrderBy(f => f.DueAt).ToListAsync();
        }

        public async Task<FollowUp> ScheduleFollowUpAsync(ScheduleFollowUpDto dto, string currentUserId)
        {
            var lead = await _context.Leads.FirstOrDefaultAsync(l => l.LeadId == dto.LeadId && !l.IsArchived);
            if (lead == null) throw new InvalidOperationException("Lead not found or archived.");

            var assignedTo = !string.IsNullOrEmpty(dto.AssignedUserId) ? dto.AssignedUserId : lead.OwnerUserId ?? currentUserId;

            var followUp = new FollowUp
            {
                LeadId = dto.LeadId,
                AssignedUserId = assignedTo,
                DueAt = dto.DueAt,
                Type = dto.Type,
                Notes = dto.Notes,
                Status = FollowUpStatus.Pending
            };

            _context.FollowUps.Add(followUp);

            _context.LeadActivities.Add(new LeadActivity
            {
                LeadId = dto.LeadId,
                UserId = currentUserId,
                Type = ActivityType.Note,
                Description = $"Follow-up {dto.Type} scheduled for {dto.DueAt:yyyy-MM-dd HH:mm UTC}. Notes: {dto.Notes ?? "None"}",
                OccurredAt = DateTime.UtcNow
            });

            lead.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            await _notificationService.CreateNotificationAsync(
                assignedTo,
                "Follow-Up Scheduled",
                $"You have a follow-up scheduled for lead '{lead.Name}' on {dto.DueAt:MMM dd, yyyy HH:mm}.",
                lead.LeadId
            );

            await _auditService.LogAsync(currentUserId, "FollowUp", followUp.FollowUpId.ToString(), "Scheduled", dto);

            return followUp;
        }

        public async Task<bool> CompleteFollowUpAsync(int followUpId, string outcomeNotes, string currentUserId)
        {
            var followUp = await _context.FollowUps
                .Include(f => f.Lead)
                .FirstOrDefaultAsync(f => f.FollowUpId == followUpId);

            if (followUp == null) return false;

            followUp.Status = FollowUpStatus.Completed;
            followUp.CompletedAt = DateTime.UtcNow;

            _context.LeadActivities.Add(new LeadActivity
            {
                LeadId = followUp.LeadId,
                UserId = currentUserId,
                Type = followUp.Type switch
                {
                    FollowUpType.Call => ActivityType.Call,
                    FollowUpType.Meeting => ActivityType.Meeting,
                    FollowUpType.Email => ActivityType.Email,
                    _ => ActivityType.Note
                },
                Description = $"Follow-up completed: {outcomeNotes}",
                OccurredAt = DateTime.UtcNow
            });

            followUp.Lead.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await _auditService.LogAsync(currentUserId, "FollowUp", followUpId.ToString(), "Completed", new { outcomeNotes });

            return true;
        }

        public async Task<bool> RescheduleFollowUpAsync(int followUpId, DateTime newDueAt, string? reason, string currentUserId)
        {
            var followUp = await _context.FollowUps
                .Include(f => f.Lead)
                .FirstOrDefaultAsync(f => f.FollowUpId == followUpId);

            if (followUp == null) return false;

            var oldDate = followUp.DueAt;
            followUp.DueAt = newDueAt;
            followUp.Status = FollowUpStatus.Pending;

            _context.LeadActivities.Add(new LeadActivity
            {
                LeadId = followUp.LeadId,
                UserId = currentUserId,
                Type = ActivityType.Note,
                Description = $"Follow-up rescheduled from {oldDate:yyyy-MM-dd} to {newDueAt:yyyy-MM-dd HH:mm UTC}. Reason: {reason ?? "Not specified"}",
                OccurredAt = DateTime.UtcNow
            });

            followUp.Lead.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await _auditService.LogAsync(currentUserId, "FollowUp", followUpId.ToString(), "Rescheduled", new { oldDate, newDueAt, reason });

            return true;
        }
    }
}
