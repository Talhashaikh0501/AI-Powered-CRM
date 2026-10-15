using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AI_Powered_CRM.ViewModels;

namespace AI_Powered_CRM.Services.Interfaces
{
    public class CompanyRegistrationResult
    {
        public bool Succeeded { get; set; }
        public Guid? TenantId { get; set; }
        public string? UserId { get; set; }
        public List<string> Errors { get; set; } = new List<string>();

        public static CompanyRegistrationResult Success(Guid tenantId, string userId) =>
            new CompanyRegistrationResult { Succeeded = true, TenantId = tenantId, UserId = userId };

        public static CompanyRegistrationResult Failure(params string[] errors) =>
            new CompanyRegistrationResult { Succeeded = false, Errors = new List<string>(errors) };
    }

    public interface ICompanyOnboardingService
    {
        Task<CompanyRegistrationResult> RegisterCompanyAsync(RegisterCompanyViewModel model);
        Task<bool> IsSlugAvailableAsync(string slug);
    }
}
