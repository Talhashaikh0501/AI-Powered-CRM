using System;
using System.Threading.Tasks;
using AI_Powered_CRM.Data;
using AI_Powered_CRM.Models.Enums;
using AI_Powered_CRM.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AI_Powered_CRM.Services.Implementations
{
    public class SubscriptionQuotaService : ISubscriptionQuotaService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<SubscriptionQuotaService> _logger;

        public SubscriptionQuotaService(ApplicationDbContext db, ILogger<SubscriptionQuotaService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<bool> CanCreateLeadAsync(Guid tenantId)
        {
            if (tenantId == Guid.Empty) return true;

            var tenant = await _db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.TenantId == tenantId);
            if (tenant == null || !tenant.IsActive) return false;

            if (tenant.SubscriptionTier == SubscriptionTier.Enterprise) return true;

            var currentLeadCount = await _db.Leads.IgnoreQueryFilters().CountAsync(l => l.TenantId == tenantId && !l.IsArchived);
            return currentLeadCount < tenant.MaxLeadsQuota;
        }

        public async Task<bool> CanInviteUserAsync(Guid tenantId)
        {
            if (tenantId == Guid.Empty) return true;

            var tenant = await _db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.TenantId == tenantId);
            if (tenant == null || !tenant.IsActive) return false;

            if (tenant.SubscriptionTier == SubscriptionTier.Enterprise) return true;

            var currentUsersCount = await _db.TenantMemberships.IgnoreQueryFilters().CountAsync(m => m.TenantId == tenantId && m.IsActive);
            return currentUsersCount < tenant.MaxUserSeats;
        }

        public async Task<bool> CanExecuteAiInferenceAsync(Guid tenantId)
        {
            if (tenantId == Guid.Empty) return true;

            var tenant = await _db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.TenantId == tenantId);
            if (tenant == null || !tenant.IsActive) return false;

            if (tenant.SubscriptionTier == SubscriptionTier.Enterprise) return true;

            return tenant.CurrentMonthAiUsageCount < tenant.MonthlyAiRequestLimit;
        }

        public async Task RecordAiUsageAsync(Guid tenantId)
        {
            if (tenantId == Guid.Empty) return;

            var tenant = await _db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.TenantId == tenantId);
            if (tenant != null)
            {
                tenant.CurrentMonthAiUsageCount++;
                await _db.SaveChangesAsync();
            }
        }
    }
}
