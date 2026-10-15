using System;
using System.Threading.Tasks;

namespace AI_Powered_CRM.Services.Interfaces
{
    public interface ILeadAssignmentService
    {
        /// <summary>
        /// Determines the appropriate owner user ID for a new lead based on the tenant's assignment mode,
        /// candidate availability, current load, and optional manual assignment overrides.
        /// </summary>
        Task<string> DetermineLeadOwnerAsync(Guid tenantId, string? requestedOwnerId, string defaultUserId);
    }
}
