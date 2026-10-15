using System;

namespace AI_Powered_CRM.Security
{
    public class TenantContext : ITenantContext
    {
        public Guid CurrentTenantId { get; private set; } = Guid.Empty;
        public string? CurrentTenantSlug { get; private set; }
        public bool IsResolved => CurrentTenantId != Guid.Empty;
        public bool IsPlatformSuperAdmin { get; private set; }
        public bool IsDelegatedSupportSession { get; private set; }
        public string? SupportAccessReason { get; private set; }

        public void SetTenant(Guid tenantId, string? slug = null)
        {
            CurrentTenantId = tenantId;
            CurrentTenantSlug = slug;
            IsDelegatedSupportSession = false;
            SupportAccessReason = null;
        }

        public void SetPlatformSuperAdmin(bool isSuperAdmin)
        {
            IsPlatformSuperAdmin = isSuperAdmin;
        }

        public void SetDelegatedSupportSession(Guid tenantId, string? slug, string supportReason)
        {
            CurrentTenantId = tenantId;
            CurrentTenantSlug = slug;
            IsPlatformSuperAdmin = true;
            IsDelegatedSupportSession = true;
            SupportAccessReason = supportReason;
        }

        public void Clear()
        {
            CurrentTenantId = Guid.Empty;
            CurrentTenantSlug = null;
            IsDelegatedSupportSession = false;
            SupportAccessReason = null;
        }
    }
}
