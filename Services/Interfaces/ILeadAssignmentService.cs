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
        Task<string> DetermineLeadOwnerAsync(Guid tenantId, string? requestedOwnerId, string defaultUserId, bool acquireLock = true);

        /// <summary>
        /// Acquires an atomic, distributed per-tenant lock coordinating lead assignment and persistence.
        /// Mandatory distributed application lock backed by SQL Server sp_getapplock when running against SQL Server
        /// (failing closed if unavailable), with in-memory semaphore serialization for intra-process concurrency.
        /// </summary>
        Task<IAsyncDisposable> AcquireTenantAssignmentLockAsync(Guid tenantId, TimeSpan? timeout = null);

        /// <summary>
        /// Notifies the assignment service that a determined lead has been committed to the database,
        /// reconciling temporary in-flight workload reservation counters.
        /// </summary>
        void NotifyLeadPersisted(Guid tenantId, string assignedUserId);

        /// <summary>
        /// Rolls back temporary in-flight workload reservations if lead creation or persistence throws an exception.
        /// </summary>
        void RollbackInFlightReservation(Guid tenantId, string assignedUserId);
    }
}
