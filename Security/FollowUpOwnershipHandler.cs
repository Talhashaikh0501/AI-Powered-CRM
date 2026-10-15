using System.Security.Claims;
using System.Threading.Tasks;
using AI_Powered_CRM.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace AI_Powered_CRM.Security
{
    /// <summary>
    /// Requirement: the caller must be assigned to this follow-up OR be Admin/Manager.
    /// </summary>
    public class FollowUpOwnershipRequirement : IAuthorizationRequirement { }

    public class FollowUpOwnershipHandler
        : AuthorizationHandler<FollowUpOwnershipRequirement, int>
    {
        private readonly ApplicationDbContext _db;

        public FollowUpOwnershipHandler(ApplicationDbContext db) => _db = db;

        protected override async Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            FollowUpOwnershipRequirement requirement,
            int followUpId)
        {
            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                context.Fail();
                return;
            }

            var followUp = await _db.FollowUps
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.FollowUpId == followUpId);

            if (followUp == null)
            {
                context.Fail();
                return;
            }

            // CompanyAdmin, Admin, and Manager can access any follow-up within their tenant
            if (context.User.IsInRole("CompanyAdmin") || context.User.IsInRole("Admin") || context.User.IsInRole("Manager"))
            {
                context.Succeed(requirement);
                return;
            }

            // SalesExecutive can only access their assigned follow-up
            if (followUp.AssignedUserId == userId)
            {
                context.Succeed(requirement);
            }
        }
    }
}
