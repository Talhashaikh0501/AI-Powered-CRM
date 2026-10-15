using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;

namespace AI_Powered_CRM.Security
{
    /// <summary>
    /// Requirement enforcing that the current request has an active, securely resolved tenant context.
    /// </summary>
    public class TenantRequirement : IAuthorizationRequirement { }

    /// <summary>
    /// Authorization handler evaluating whether tenant context is resolved and active.
    /// Fails closed if tenant context is unauthenticated, unresolved, or inactive.
    /// </summary>
    public class TenantAuthorizationHandler : AuthorizationHandler<TenantRequirement>
    {
        private readonly ITenantContext _tenantContext;

        public TenantAuthorizationHandler(ITenantContext tenantContext)
        {
            _tenantContext = tenantContext;
        }

        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context, 
            TenantRequirement requirement)
        {
            // Deny if user is not authenticated
            if (context.User?.Identity?.IsAuthenticated != true)
            {
                context.Fail();
                return Task.CompletedTask;
            }

            // Tenant context must be resolved and have a valid CurrentTenantId
            if (_tenantContext.IsResolved && _tenantContext.CurrentTenantId != System.Guid.Empty)
            {
                context.Succeed(requirement);
            }
            else
            {
                context.Fail();
            }

            return Task.CompletedTask;
        }
    }
}
