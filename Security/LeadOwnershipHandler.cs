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
            // Admin and Manager bypass ownership check
            if (context.User.IsInRole("Admin") || context.User.IsInRole("Manager"))
            {
                context.Succeed(requirement);
                return;
            }

            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                context.Fail();
                return;
            }

            var lead = await _db.Leads
                .AsNoTracking()
                .FirstOrDefaultAsync(l => l.LeadId == leadId && !l.IsArchived);

            if (lead != null && lead.OwnerUserId == userId)
            {
                context.Succeed(requirement);
            }
        }
    }
}
