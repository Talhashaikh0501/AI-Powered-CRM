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
        private readonly ApplicationDbContext      _context;
        private readonly IAuditService             _auditService;
        private readonly INotificationService      _notificationService;
        private readonly ILogger<FollowUpService>  _logger;

        public FollowUpService(
            ApplicationDbContext     context,
            IAuditService            auditService,
            INotificationService     notificationService,
            ILogger<FollowUpService> logger)
        {
            _context             = context;
            _auditService        = auditService;
            _notificationService = notificationService;
            _logger              = logger;
        }

        // ------------------------------------------------------------------
        // READ: list follow-ups (scoped by role)
        // ------------------------------------------------------------------
        public async Task<List<FollowUp>> GetFollowUpsAsync(
            string?          currentUserId,
            string?          role,
            FollowUpStatus?  status = null)
        {
            var query = _context.FollowUps
                .Include(f => f.Lead)
                .Include(f => f.AssignedUser)
                .Where(f => !f.Lead.IsArchived);

            if (role != "Admin" && role != "Manager")
            {
                if (string.IsNullOrEmpty(currentUserId))
                    return new List<FollowUp>();
                query = query.Where(f => f.AssignedUserId == currentUserId);
            }

            if (status.HasValue)
                query = query.Where(f => f.Status == status.Value);

            return await query.OrderBy(f => f.DueAt).ToListAsync();
        }

        public async Task<List<FollowUp>> GetOverdueFollowUpsAsync(
            string? currentUserId,
            string? role)
        {
            var query = _context.FollowUps
                .Include(f => f.Lead)
                .Include(f => f.AssignedUser)
                .Where(f => !f.Lead.IsArchived
                         && f.Status == FollowUpStatus.Pending
                         && f.DueAt < DateTime.UtcNow);

            if (role != "Admin" && role != "Manager")
            {
                if (string.IsNullOrEmpty(currentUserId))
                    return new List<FollowUp>();
                query = query.Where(f => f.AssignedUserId == currentUserId);
            }

            return await query.OrderBy(f => f.DueAt).ToListAsync();
        }

        public async Task<List<FollowUp>> GetTodayFollowUpsAsync(
            string? currentUserId,
            string? role)
        {
            var startOfDay = DateTime.UtcNow.Date;
            var endOfDay   = startOfDay.AddDays(1).AddTicks(-1);

            var query = _context.FollowUps
                .Include(f => f.Lead)
                .Include(f => f.AssignedUser)
                .Where(f => !f.Lead.IsArchived
                         && f.Status == FollowUpStatus.Pending
                         && f.DueAt >= startOfDay
                         && f.DueAt <= endOfDay);

            if (role != "Admin" && role != "Manager")
            {
                if (string.IsNullOrEmpty(currentUserId))
                    return new List<FollowUp>();
                query = query.Where(f => f.AssignedUserId == currentUserId);
            }

            return await query.OrderBy(f => f.DueAt).ToListAsync();
        }

        // ------------------------------------------------------------------
        // WRITE: schedule
        // Only Admin/Manager may assign a follow-up to a different user.
        // SalesExecutive can schedule on their own lead only.
        // ------------------------------------------------------------------
        public async Task<FollowUp> ScheduleFollowUpAsync(
            ScheduleFollowUpDto dto,
            string currentUserId,
            string currentUserRole)
        {
            var lead = await _context.Leads
                .FirstOrDefaultAsync(l => l.LeadId == dto.LeadId && !l.IsArchived);

            if (lead == null)
                throw new InvalidOperationException("Lead not found or archived.");

            // Ownership check for non-Admin/Manager
            if (currentUserRole != "Admin" && currentUserRole != "Manager")
            {
                if (string.IsNullOrEmpty(currentUserId) || lead.OwnerUserId != currentUserId)
                {
                    _logger.LogWarning(
                        "User {UserId} with role {Role} tried to schedule follow-up on Lead {LeadId} they do not own.",
                        currentUserId, currentUserRole, dto.LeadId);
                    throw new UnauthorizedAccessException("You can only schedule follow-ups on leads you own.");
                }
            }

            // Only Admin/Manager may assign follow-up to another user, and target user must be valid and active
            string assignedTo;
            if (currentUserRole == "Admin" || currentUserRole == "Manager")
            {
                if (!string.IsNullOrEmpty(dto.AssignedUserId))
                {
                    var targetUser = await _context.Users
                        .AsNoTracking()
                        .FirstOrDefaultAsync(u => u.Id == dto.AssignedUserId && u.IsActive);
                    if (targetUser == null)
                    {
                        throw new InvalidOperationException("Target user does not exist or is inactive.");
                    }
                    assignedTo = dto.AssignedUserId;
                }
                else
                {
                    assignedTo = lead.OwnerUserId ?? currentUserId;
                }
            }
            else
            {
                // SalesExecutive or non-elevated roles can only schedule for themselves
                assignedTo = currentUserId;
            }

            var followUp = new FollowUp
            {
                LeadId         = dto.LeadId,
                AssignedUserId = assignedTo,
                DueAt          = dto.DueAt,
                Type           = dto.Type,
                Notes          = dto.Notes,
                Status         = FollowUpStatus.Pending
            };

            _context.FollowUps.Add(followUp);

            _context.LeadActivities.Add(new LeadActivity
            {
                LeadId      = dto.LeadId,
                UserId      = currentUserId,
                Type        = ActivityType.Note,
                Description = $"Follow-up {dto.Type} scheduled for {dto.DueAt:yyyy-MM-dd HH:mm UTC}. Notes: {dto.Notes ?? "None"}",
                OccurredAt  = DateTime.UtcNow
            });

            lead.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await _notificationService.CreateNotificationAsync(
                assignedTo,
                "Follow-Up Scheduled",
                $"You have a follow-up scheduled for lead '{lead.Name}' on {dto.DueAt:MMM dd, yyyy HH:mm}.",
                lead.LeadId);

            await _auditService.LogAsync(
                currentUserId, "FollowUp",
                followUp.FollowUpId.ToString(), "Scheduled",
                new { dto.LeadId, dto.Type, dto.DueAt, AssignedTo = assignedTo });

            return followUp;
        }

        // ------------------------------------------------------------------
        // WRITE: complete
        // SalesExecutive may only complete follow-ups assigned to them.
        // ------------------------------------------------------------------
        public async Task<bool> CompleteFollowUpAsync(
            int    followUpId,
            string outcomeNotes,
            string currentUserId,
            string currentUserRole)
        {
            var followUp = await _context.FollowUps
                .Include(f => f.Lead)
                .FirstOrDefaultAsync(f => f.FollowUpId == followUpId);

            if (followUp == null) return false;

            // Ownership check
            if (currentUserRole != "Admin" && currentUserRole != "Manager")
            {
                if (string.IsNullOrEmpty(currentUserId) || followUp.AssignedUserId != currentUserId)
                {
                    _logger.LogWarning(
                        "User {UserId} tried to complete FollowUp {FollowUpId} not assigned to them.",
                        currentUserId, followUpId);
                    return false;
                }
            }

            followUp.Status      = FollowUpStatus.Completed;
            followUp.CompletedAt = DateTime.UtcNow;

            _context.LeadActivities.Add(new LeadActivity
            {
                LeadId      = followUp.LeadId,
                UserId      = currentUserId,
                Type = followUp.Type switch
                {
                    FollowUpType.Call    => ActivityType.Call,
                    FollowUpType.Meeting => ActivityType.Meeting,
                    FollowUpType.Email   => ActivityType.Email,
                    _                    => ActivityType.Note
                },
                Description = $"Follow-up completed: {outcomeNotes}",
                OccurredAt  = DateTime.UtcNow
            });

            followUp.Lead.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await _auditService.LogAsync(
                currentUserId, "FollowUp",
                followUpId.ToString(), "Completed",
                new { outcomeNotes });

            return true;
        }

        // ------------------------------------------------------------------
        // WRITE: reschedule
        // SalesExecutive may only reschedule follow-ups assigned to them.
        // ------------------------------------------------------------------
        public async Task<bool> RescheduleFollowUpAsync(
            int      followUpId,
            DateTime newDueAt,
            string?  reason,
            string   currentUserId,
            string   currentUserRole)
        {
            var followUp = await _context.FollowUps
                .Include(f => f.Lead)
                .FirstOrDefaultAsync(f => f.FollowUpId == followUpId);

            if (followUp == null) return false;

            // Ownership check
            if (currentUserRole != "Admin" && currentUserRole != "Manager")
            {
                if (string.IsNullOrEmpty(currentUserId) || followUp.AssignedUserId != currentUserId)
                {
                    _logger.LogWarning(
                        "User {UserId} tried to reschedule FollowUp {FollowUpId} not assigned to them.",
                        currentUserId, followUpId);
                    return false;
                }
            }

            var oldDate      = followUp.DueAt;
            followUp.DueAt   = newDueAt;
            followUp.Status  = FollowUpStatus.Pending;

            _context.LeadActivities.Add(new LeadActivity
            {
                LeadId      = followUp.LeadId,
                UserId      = currentUserId,
                Type        = ActivityType.Note,
                Description = $"Follow-up rescheduled from {oldDate:yyyy-MM-dd} to {newDueAt:yyyy-MM-dd HH:mm UTC}. Reason: {reason ?? "Not specified"}",
                OccurredAt  = DateTime.UtcNow
            });

            followUp.Lead.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await _auditService.LogAsync(
                currentUserId, "FollowUp",
                followUpId.ToString(), "Rescheduled",
                new { oldDate, newDueAt, reason });

            return true;
        }
    }
}
