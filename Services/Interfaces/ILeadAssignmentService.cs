using System;
using System.Threading.Tasks;

namespace AI_Powered_CRM.Services.Interfaces
{
    public interface ILeadAssignmentService
    {
        /// <summary>
        /// Determines the appropriate owner user ID for a new lead based on the tenant's assignment mode,
        /// candidate availability, current load, and optional manual assignment overrides.
        /// Guaranteed concurrency-safe across simultaneous requests within each tenant.
        /// </summary>
        Task<string> DetermineLeadOwnerAsync(Guid tenantId, string? requestedOwnerId, string defaultUserId);

        /// <summary>
        /// Notifies the assignment service that a determined lead has been committed to the database,
        /// reconciling temporary in-flight workload reservation counters.
        /// </summary>
        void NotifyLeadPersisted(Guid tenantId, string assignedUserId);
    }
}
