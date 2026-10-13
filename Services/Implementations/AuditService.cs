using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AI_Powered_CRM.Data;
using AI_Powered_CRM.Models.Entities;
using AI_Powered_CRM.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AI_Powered_CRM.Services.Implementations
{
    public class AuditService : IAuditService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<AuditService> _logger;

        public AuditService(ApplicationDbContext context, ILogger<AuditService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task LogAsync(string? userId, string entityType, string entityId, string action, object? details = null)
        {
            try
            {
                var detailsJson = details != null ? JsonSerializer.Serialize(details) : "{}";
                var log = new AuditLog
                {
                    UserId = userId,
                    EntityType = entityType,
                    EntityId = entityId,
                    Action = action,
                    DetailsJson = detailsJson,
                    CreatedAt = DateTime.UtcNow
                };

                _context.AuditLogs.Add(log);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to record audit log for Entity {EntityType} {EntityId}", entityType, entityId);
            }
        }

        public async Task<List<AuditLog>> GetRecentLogsAsync(int count = 50)
        {
            return await _context.AuditLogs
                .Include(a => a.User)
                .OrderByDescending(a => a.CreatedAt)
                .Take(count)
                .ToListAsync();
        }
    }
}
