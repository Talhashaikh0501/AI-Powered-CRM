using System;

namespace AI_Powered_CRM.Security
{
    public interface ITenantContext
    {
        Guid CurrentTenantId { get; }
        string? CurrentTenantSlug { get; }
        bool IsResolved { get; }
        bool IsPlatformSuperAdmin { get; }
        bool IsDelegatedSupportSession { get; }
        string? SupportAccessReason { get; }
        void SetTenant(Guid tenantId, string? slug = null);
        void SetPlatformSuperAdmin(bool isSuperAdmin);
        void SetDelegatedSupportSession(Guid tenantId, string? slug, string supportReason);
        void Clear();
    }
}
