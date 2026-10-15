using System;

namespace AI_Powered_CRM.Models.Interfaces
{
    /// <summary>
    /// Marker interface for all entities scoped to a specific tenant in the multi-tenant architecture.
    /// Used by EF Core global query filters and SaveChanges interceptors.
    /// </summary>
    public interface ITenantEntity
    {
        Guid TenantId { get; set; }
    }
}
