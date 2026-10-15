using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AI_Powered_CRM.Models.Enums;
using AI_Powered_CRM.ViewModels;

namespace AI_Powered_CRM.Services.Interfaces
{
    public interface ISubscriptionBillingService
    {
        List<SubscriptionPlanDto> GetAvailablePlans(SubscriptionTier? currentTier = null);
        Task<SubscriptionDashboardViewModel?> GetSubscriptionDashboardAsync(Guid tenantId);
        Task<BillingHistoryViewModel> GetBillingHistoryAsync(Guid tenantId);
    }
}
