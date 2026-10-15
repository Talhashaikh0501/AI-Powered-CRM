using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AI_Powered_CRM.AI;
using AI_Powered_CRM.Data;
using AI_Powered_CRM.DTOs;
using AI_Powered_CRM.Models.Entities;
using AI_Powered_CRM.Models.Enums;
using AI_Powered_CRM.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AI_Powered_CRM.Services.Implementations
{
    public class LeadService : ILeadService
    {
        private readonly ApplicationDbContext _context;
        private readonly IAIService _aiService;
        private readonly IAuditService _auditService;
        private readonly INotificationService _notificationService;
        private readonly ILogger<LeadService> _logger;

        public LeadService(
            ApplicationDbContext context,
            IAIService aiService,
            IAuditService auditService,
            INotificationService notificationService,
            ILogger<LeadService> logger)
        {
            _context = context;
            _aiService = aiService;
            _auditService = auditService;
            _notificationService = notificationService;
            _logger = logger;
        }

        public async Task<(List<Lead> Leads, int TotalCount)> GetPagedLeadsAsync(
            string? search,
            LeadStatus? status,
            LeadPriority? priority,
            string? source,
            string? currentUserId,
            string? role,
            int page = 1,
            int pageSize = 15)
        {
            var query = _context.Leads
                .Include(l => l.OwnerUser)
                .Include(l => l.AIInsights)
                .Where(l => !l.IsArchived);

            // Scope based on role - denied by default for unknown/non-elevated roles
            if (role != "Admin" && role != "Manager")
            {
                if (string.IsNullOrEmpty(currentUserId))
                {
                    return (new List<Lead>(), 0);
                }
                query = query.Where(l => l.OwnerUserId == currentUserId);
            }

            // Search filter
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLower();
                query = query.Where(l =>
                    l.Name.ToLower().Contains(s) ||
                    l.Phone.Contains(s) ||
                    (l.Email != null && l.Email.ToLower().Contains(s)) ||
                    (l.Company != null && l.Company.ToLower().Contains(s)) ||
                    l.InterestedProduct.ToLower().Contains(s));
            }

            // Status filter
            if (status.HasValue)
            {
                query = query.Where(l => l.Status == status.Value);
            }

            // Priority filter
            if (priority.HasValue)
            {
                query = query.Where(l => l.Priority == priority.Value);
            }

            // Source filter
            if (!string.IsNullOrWhiteSpace(source))
            {
                query = query.Where(l => l.Source == source);
            }

            int totalCount = await query.CountAsync();

            var leads = await query
                .OrderByDescending(l => l.UpdatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return (leads, totalCount);
        }

        public async Task<Lead?> GetLeadDetailsAsync(int id, string? currentUserId, string? role)
        {
            var lead = await _context.Leads
                .Include(l => l.OwnerUser)
                .Include(l => l.Activities.OrderByDescending(a => a.OccurredAt))
                    .ThenInclude(a => a.User)
                .Include(l => l.FollowUps.OrderBy(f => f.DueAt))
                    .ThenInclude(f => f.AssignedUser)
                .Include(l => l.StatusHistories.OrderByDescending(h => h.ChangedAt))
                    .ThenInclude(h => h.ChangedByUser)
                .Include(l => l.AIInsights.OrderByDescending(i => i.GeneratedAt))
                .FirstOrDefaultAsync(l => l.LeadId == id && !l.IsArchived);

            if (lead == null) return null;

            // Role scope check - denied by default for non-admin/manager
            if (role != "Admin" && role != "Manager")
            {
                if (string.IsNullOrEmpty(currentUserId) || lead.OwnerUserId != currentUserId)
                {
                    return null; // Forbidden
                }
            }

            return lead;
        }

        public async Task<Lead> CreateLeadAsync(CreateLeadDto dto, string currentUserId, string? role = null)
        {
            string ownerUserId;
            if (role == "Admin" || role == "Manager")
            {
                if (!string.IsNullOrWhiteSpace(dto.OwnerUserId))
                {
                    var targetUser = await _context.Users
                        .AsNoTracking()
                        .FirstOrDefaultAsync(u => u.Id == dto.OwnerUserId && u.IsActive);
                    ownerUserId = targetUser != null ? targetUser.Id : currentUserId;
                }
                else
                {
                    ownerUserId = currentUserId;
                }
            }
            else
            {
                // SalesExecutive or unknown role can ONLY create lead assigned to self
                ownerUserId = currentUserId;
            }

            var lead = new Lead
            {
                Name = dto.Name.Trim(),
                Phone = dto.Phone.Trim(),
                Email = dto.Email?.Trim(),
                Company = dto.Company?.Trim(),
                Source = dto.Source,
                InterestedProduct = dto.InterestedProduct.Trim(),
                Budget = dto.Budget,
                Status = dto.Status,
                Priority = dto.Priority,
                OwnerUserId = ownerUserId,
                IsArchived = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.Leads.Add(lead);
            await _context.SaveChangesAsync();

            // Initial status history
            _context.LeadStatusHistories.Add(new LeadStatusHistory
            {
                LeadId = lead.LeadId,
                OldStatus = LeadStatus.New,
                NewStatus = lead.Status,
                ChangedByUserId = currentUserId,
                Notes = "Lead created",
                ChangedAt = DateTime.UtcNow
            });

            // Initial activity
            _context.LeadActivities.Add(new LeadActivity
            {
                LeadId = lead.LeadId,
                UserId = currentUserId,
                Type = ActivityType.Note,
                Description = $"Lead created via {lead.Source} with interested product '{lead.InterestedProduct}'.",
                OccurredAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(currentUserId, "Lead", lead.LeadId.ToString(), "Created", new { lead.Name, lead.Phone, lead.Budget });

            // Automatically run initial AI analysis
            try
            {
                await RunAIAnalysisAsync(lead.LeadId, currentUserId, "Admin");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Initial AI analysis on lead creation deferred or failed for Lead {LeadId}", lead.LeadId);
            }

            return lead;
        }

        public async Task<bool> UpdateLeadAsync(UpdateLeadDto dto, string currentUserId, string role)
        {
            var lead = await _context.Leads.FirstOrDefaultAsync(l => l.LeadId == dto.LeadId && !l.IsArchived);
            if (lead == null) return false;

            if (role != "Admin" && role != "Manager")
            {
                if (string.IsNullOrEmpty(currentUserId) || lead.OwnerUserId != currentUserId)
                {
                    return false;
                }
            }

            lead.Name = dto.Name.Trim();
            lead.Phone = dto.Phone.Trim();
            lead.Email = dto.Email?.Trim();
            lead.Company = dto.Company?.Trim();
            lead.Source = dto.Source;
            lead.InterestedProduct = dto.InterestedProduct.Trim();
            lead.Budget = dto.Budget;
            lead.Priority = dto.Priority;
            lead.UpdatedAt = DateTime.UtcNow;

            // Only Admin or Manager can reassign owner during update, and target user must be active
            if (role == "Admin" || role == "Manager")
            {
                if (!string.IsNullOrEmpty(dto.OwnerUserId) && lead.OwnerUserId != dto.OwnerUserId)
                {
                    var targetUser = await _context.Users
                        .AsNoTracking()
                        .FirstOrDefaultAsync(u => u.Id == dto.OwnerUserId && u.IsActive);
                    if (targetUser == null)
                    {
                        return false; // Reject assignment to invalid or inactive target
                    }
                    lead.OwnerUserId = dto.OwnerUserId;
                }
            }

            await _context.SaveChangesAsync();
            await _auditService.LogAsync(currentUserId, "Lead", lead.LeadId.ToString(), "Updated", dto);
            return true;
        }

        public async Task<bool> ChangeStatusAsync(int leadId, LeadStatus newStatus, string? notes, string currentUserId, string role)
        {
            var lead = await _context.Leads.FirstOrDefaultAsync(l => l.LeadId == leadId && !l.IsArchived);
            if (lead == null) return false;

            if (role != "Admin" && role != "Manager")
            {
                if (string.IsNullOrEmpty(currentUserId) || lead.OwnerUserId != currentUserId)
                {
                    return false;
                }
            }

            if (lead.Status == newStatus) return true;

            var oldStatus = lead.Status;
            lead.Status = newStatus;
            lead.UpdatedAt = DateTime.UtcNow;

            _context.LeadStatusHistories.Add(new LeadStatusHistory
            {
                LeadId = leadId,
                OldStatus = oldStatus,
                NewStatus = newStatus,
                ChangedByUserId = currentUserId,
                Notes = notes ?? $"Status updated to {newStatus}",
                ChangedAt = DateTime.UtcNow
            });

            _context.LeadActivities.Add(new LeadActivity
            {
                LeadId = leadId,
                UserId = currentUserId,
                Type = ActivityType.StatusChange,
                Description = $"Pipeline status changed from '{oldStatus}' to '{newStatus}'. Notes: {notes ?? "None"}",
                OccurredAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();
            await _auditService.LogAsync(currentUserId, "Lead", leadId.ToString(), "StatusChanged", new { oldStatus, newStatus, notes });

            return true;
        }

        public async Task<bool> AssignLeadAsync(int leadId, string newOwnerId, string assignedByUserId, string role)
        {
            if (role != "Admin" && role != "Manager")
            {
                return false;
            }

            var targetUser = await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == newOwnerId && u.IsActive);
            if (targetUser == null)
            {
                _logger.LogWarning("Reassignment target user {NewOwnerId} does not exist or is inactive.", newOwnerId);
                return false;
            }

            var lead = await _context.Leads.FirstOrDefaultAsync(l => l.LeadId == leadId && !l.IsArchived);
            if (lead == null) return false;

            var oldOwner = lead.OwnerUserId;
            lead.OwnerUserId = newOwnerId;
            lead.UpdatedAt = DateTime.UtcNow;

            _context.LeadActivities.Add(new LeadActivity
            {
                LeadId = leadId,
                UserId = assignedByUserId,
                Type = ActivityType.Note,
                Description = $"Lead reassigned to new owner.",
                OccurredAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            await _notificationService.CreateNotificationAsync(
                newOwnerId,
                "New Lead Assigned",
                $"You have been assigned to lead '{lead.Name}' ({lead.InterestedProduct}).",
                lead.LeadId
            );

            await _auditService.LogAsync(assignedByUserId, "Lead", leadId.ToString(), "Assigned", new { oldOwner, newOwnerId });
            return true;
        }

        public async Task<bool> ArchiveLeadAsync(int leadId, string currentUserId, string role)
        {
            if (role != "Admin" && role != "Manager")
            {
                return false;
            }

            var lead = await _context.Leads.FirstOrDefaultAsync(l => l.LeadId == leadId);
            if (lead == null) return false;

            lead.IsArchived = true;
            lead.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await _auditService.LogAsync(currentUserId, "Lead", leadId.ToString(), "Archived");
            return true;
        }

        public async Task<AILeadInsight?> RunAIAnalysisAsync(int leadId, string currentUserId, string role)
        {
            var lead = await _context.Leads
                .Include(l => l.Activities)
                .Include(l => l.FollowUps)
                .FirstOrDefaultAsync(l => l.LeadId == leadId && !l.IsArchived);

            if (lead == null) return null;

            if (role != "Admin" && role != "Manager")
            {
                if (string.IsNullOrEmpty(currentUserId) || lead.OwnerUserId != currentUserId)
                {
                    return null;
                }
            }

            var result = await _aiService.AnalyzeLeadAsync(
                lead,
                lead.Activities.OrderByDescending(a => a.OccurredAt).ToList(),
                lead.FollowUps.OrderBy(f => f.DueAt).ToList()
            );

            // Parse enums
            Enum.TryParse<AIClassification>(result.Classification, true, out var classification);
            Enum.TryParse<RiskLevel>(result.RiskLevel, true, out var riskLevel);
            Enum.TryParse<UrgencyLevel>(result.Urgency, true, out var urgency);

            var insight = new AILeadInsight
            {
                LeadId = leadId,
                Classification = classification,
                Score = result.Score,
                Summary = result.Summary,
                NextBestAction = result.NextBestAction,
                RiskLevel = riskLevel,
                Urgency = urgency,
                ReasonJson = JsonSerializer.Serialize(result.Reasons),
                SuggestedResponse = result.SuggestedResponse,
                Provider = result.Provider,
                Model = result.Model,
                GeneratedAt = DateTime.UtcNow
            };

            _context.AILeadInsights.Add(insight);
            lead.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await _auditService.LogAsync(currentUserId, "AILeadInsight", insight.InsightId.ToString(), "Generated", new { leadId, result.Score, result.Classification });

            return insight;
        }

        public async Task<Dictionary<LeadStatus, List<Lead>>> GetPipelineKanbanAsync(string? currentUserId, string? role)
        {
            var query = _context.Leads
                .Include(l => l.OwnerUser)
                .Include(l => l.AIInsights)
                .Where(l => !l.IsArchived);

            if (role != "Admin" && role != "Manager")
            {
                if (string.IsNullOrEmpty(currentUserId))
                {
                    return new Dictionary<LeadStatus, List<Lead>>();
                }
                query = query.Where(l => l.OwnerUserId == currentUserId);
            }

            var leads = await query.OrderByDescending(l => l.UpdatedAt).ToListAsync();

            var stages = Enum.GetValues<LeadStatus>();
            var kanban = new Dictionary<LeadStatus, List<Lead>>();

            foreach (var stage in stages)
            {
                kanban[stage] = leads.Where(l => l.Status == stage).ToList();
            }

            return kanban;
        }
    }
}
