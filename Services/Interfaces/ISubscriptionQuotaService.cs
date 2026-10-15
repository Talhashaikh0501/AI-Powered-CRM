using System;
using System.Threading.Tasks;

namespace AI_Powered_CRM.Services.Interfaces
{
    public interface ISubscriptionQuotaService
    {
        Task<bool> CanCreateLeadAsync(Guid tenantId);
        Task<bool> CanInviteUserAsync(Guid tenantId);
        Task<bool> CanExecuteAiInferenceAsync(Guid tenantId);
        Task RecordAiUsageAsync(Guid tenantId);
    }
}
