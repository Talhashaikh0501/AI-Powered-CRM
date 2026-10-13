using System.Collections.Generic;
using System.Threading.Tasks;
using AI_Powered_CRM.Models.Entities;

namespace AI_Powered_CRM.Services.Interfaces
{
    public interface IAuditService
    {
        Task LogAsync(string? userId, string entityType, string entityId, string action, object? details = null);
        Task<List<AuditLog>> GetRecentLogsAsync(int count = 50);
    }
}
