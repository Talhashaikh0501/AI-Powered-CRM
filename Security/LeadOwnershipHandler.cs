using System.Security.Claims;
using System.Threading.Tasks;
using AI_Powered_CRM.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace AI_Powered_CRM.Security
{
    /// <summary>
    /// Requirement: the caller must own the lead OR be Admin/Manager.
    /// </summary>
    public class LeadOwnershipRequirement : IAuthorizationRequirement { }

    public class LeadOwnershipHandler
        : AuthorizationHandler<LeadOwnershipRequirement, int>
    {
        private readonly ApplicationDbContext _db;

        public LeadOwnershipHandler(ApplicationDbContext db) => _db = db;

        protected override async Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            LeadOwnershipRequirement requirement,
            int leadId)
        {
            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                context.Fail();
                return;
            }

            var lead = await _db.Leads
                .AsNoTracking()
                .FirstOrDefaultAsync(l => l.LeadId == leadId && !l.IsArchived);

            if (lead == null)
            {
                context.Fail();
                return;
            }

            // CompanyAdmin, Admin, and Manager can access any lead within their tenant
            if (context.User.IsInRole("CompanyAdmin") || context.User.IsInRole("Admin") || context.User.IsInRole("Manager"))
            {
                context.Succeed(requirement);
                return;
            }

            // SalesExecutive can only access their owned lead
            if (lead.OwnerUserId == userId)
            {
                context.Succeed(requirement);
            }
        }
    }
}
