using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AI_Powered_CRM.Data;
using AI_Powered_CRM.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AI_Powered_CRM.Security
{
    public class TenantResolutionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<TenantResolutionMiddleware> _logger;

        public const string TenantIdClaimType = "crm:tenant_id";
        public const string TenantSlugClaimType = "crm:tenant_slug";
        public const string CompanyRoleClaimType = "crm:company_role";
        public const string TenantHeaderName = "X-Tenant-ID";
        public const string TenantSlugHeaderName = "X-Tenant-Slug";
        public const string SupportReasonHeaderName = "X-Support-Reason";
        public const string SupportTicketHeaderName = "X-Support-Ticket";
        public const string ActiveTenantCookieName = "crm_active_tenant_id";
        public const string PlatformWorkspaceCookieName = "crm_platform_workspace_access";

        public TenantResolutionMiddleware(RequestDelegate next, ILogger<TenantResolutionMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext, ApplicationDbContext dbContext, IDataProtectionProvider dataProtectionProvider)
        {
            // 1. Unauthenticated requests: NEVER resolve tenant context
            if (context.User?.Identity?.IsAuthenticated != true)
            {
                await _next(context);
                return;
            }

            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                await _next(context);
                return;
            }

            // 2. Check Platform SuperAdmin
            var isSuperAdmin = context.User.IsInRole("PlatformSuperAdmin") || context.User.IsInRole("SuperAdmin");
            if (isSuperAdmin)
            {
                tenantContext.SetPlatformSuperAdmin(true);
            }

            // 3. Inspect requested tenant header (if provided) or active tenant cookie
            Guid? headerTenantId = null;
            bool isFromHeader = false;
            if (context.Request.Headers.TryGetValue(TenantHeaderName, out var headerVal) &&
                Guid.TryParse(headerVal, out var parsedHeaderId))
            {
                headerTenantId = parsedHeaderId;
                isFromHeader = true;
            }
            else if (context.Request.Cookies.TryGetValue(ActiveTenantCookieName, out var cookieTenantVal) &&
                     Guid.TryParse(cookieTenantVal, out var parsedCookieTenantId))
            {
                headerTenantId = parsedCookieTenantId;
            }

            // A platform owner can explicitly enter a selected company from the Platform UI.
            // The short-lived, Data-Protection-protected cookie cannot be forged by changing its contents.
            string? ownerWorkspaceReason = null;
            bool isOwnerWorkspaceSession = false;
            if (isSuperAdmin && !isFromHeader &&
                context.Request.Cookies.TryGetValue(PlatformWorkspaceCookieName, out var protectedWorkspace))
            {
                try
                {
                    var protector = dataProtectionProvider.CreateProtector("AI_Powered_CRM.PlatformWorkspaceAccess.v1");
                    var parts = protector.Unprotect(protectedWorkspace).Split('|', 3);
                    if (parts.Length == 3 &&
                        Guid.TryParseExact(parts[0], "N", out var ownerTenantId) &&
                        long.TryParse(parts[1], out var expiresUtcTicks) &&
                        DateTime.UtcNow.Ticks <= expiresUtcTicks)
                    {
                        var reason = Uri.UnescapeDataString(parts[2]);
                        if (!string.IsNullOrWhiteSpace(reason))
                        {
                            headerTenantId = ownerTenantId;
                            ownerWorkspaceReason = reason;
                            isOwnerWorkspaceSession = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Invalid or expired platform workspace access cookie for SuperAdmin {UserId}.", userId);
                }
            }

            // 4. Handle Platform SuperAdmin: explicit owner workspace session or approved delegated support ticket
            if (isSuperAdmin)
            {
                if (headerTenantId.HasValue)
                {
                    string? ticketRef = null;
                    if (context.Request.Headers.TryGetValue(SupportTicketHeaderName, out var ticketVal) && !string.IsNullOrWhiteSpace(ticketVal))
                    {
                        ticketRef = ticketVal.ToString().Trim();
                    }
                    else if (context.Request.Headers.TryGetValue(SupportReasonHeaderName, out var reasonVal) && !string.IsNullOrWhiteSpace(reasonVal))
                    {
                        ticketRef = reasonVal.ToString().Trim();
                    }

                    SupportAccessTicket? authorizedTicket = null;
                    if (!isOwnerWorkspaceSession)
                    {
                        // Delegated support continues to require a customer-approved server-side ticket.
                        if (string.IsNullOrWhiteSpace(ticketRef))
                        {
                            _logger.LogWarning("Security Block: Platform SuperAdmin {UserId} attempted delegated tenant access to {TenantId} without authorized ticket reference.", userId, headerTenantId.Value);
                            context.Response.StatusCode = StatusCodes.Status403Forbidden;
                            await WriteForbiddenOrErrorAsync(context, "Forbidden: delegated support access requires an approved server-side support ticket.");
                            return;
                        }

                        var now = DateTime.UtcNow;
                        authorizedTicket = await dbContext.SupportAccessTickets
                            .IgnoreQueryFilters()
                            .FirstOrDefaultAsync(t =>
                                t.TenantId == headerTenantId.Value &&
                                (t.TicketNumber == ticketRef || t.TicketId.ToString() == ticketRef) &&
                                t.Status == "Approved" &&
                                t.ExpiresAt >= now &&
                                !string.IsNullOrWhiteSpace(t.SuperAdminUserId) &&
                                t.SuperAdminUserId == userId);

                        if (authorizedTicket == null)
                        {
                            _logger.LogWarning("Security Alert: Platform SuperAdmin {UserId} attempted delegated access to {TenantId} with unapproved/expired ticket reference '{TicketRef}'.", userId, headerTenantId.Value, ticketRef);
                            context.Response.StatusCode = StatusCodes.Status403Forbidden;
                            await WriteForbiddenOrErrorAsync(context, "Forbidden: No active, server-approved support ticket found for this tenant and administrator.");
                            return;
                        }
                    }

                    var targetTenant = await dbContext.Tenants
                        .IgnoreQueryFilters()
                        .FirstOrDefaultAsync(t => t.TenantId == headerTenantId.Value && t.IsActive);

                    if (targetTenant == null)
                    {
                        _logger.LogWarning("Platform SuperAdmin attempted delegated access to non-existent or inactive tenant {TenantId}.", headerTenantId.Value);
                        context.Response.StatusCode = StatusCodes.Status404NotFound;
                        await WriteForbiddenOrErrorAsync(context, "Specified tenant was not found or is inactive.");
                        return;
                    }

                    // Establish audited support session using verified reason from approved ticket
                    var verifiedReason = isOwnerWorkspaceSession ? ownerWorkspaceReason! : authorizedTicket!.Reason;
                    tenantContext.SetDelegatedSupportSession(targetTenant.TenantId, targetTenant.Slug, verifiedReason);
                    // Platform owner workspace mode receives company-admin claims only for this verified tenant request.
                    ApplyTenantRoleClaims(context.User, AI_Powered_CRM.Models.Enums.CompanyRole.CompanyAdmin);

                    // Mandatory Permanent Audit Log (Fail Closed: request is aborted if audit cannot be persisted)
                    try
                    {
                        dbContext.IgnoreTenantFilter = true;
                        dbContext.AuditLogs.Add(new AuditLog
                        {
                            TenantId = targetTenant.TenantId,
                            UserId = userId,
                            EntityType = "Tenant",
                            EntityId = targetTenant.TenantId.ToString(),
                            Action = "PlatformSuperAdminWorkspaceAccess",
                            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
                            {
                                AccessMode = isOwnerWorkspaceSession ? "PlatformOwnerWorkspace" : "CustomerApprovedSupport",
                                TicketId = authorizedTicket?.TicketId,
                                TicketNumber = authorizedTicket?.TicketNumber,
                                VerifiedReason = verifiedReason,
                                TargetTenant = targetTenant.CompanyName,
                                SuperAdminId = userId,
                                ClientIp = context.Connection.RemoteIpAddress?.ToString()
                            }),
                            CreatedAt = DateTime.UtcNow
                        });
                        await dbContext.SaveChangesAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogCritical(ex, "SECURITY ALERT: Mandatory audit logging failed for Platform SuperAdmin delegated access. Failing closed.");
                        tenantContext.Clear();
                        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                        await WriteForbiddenOrErrorAsync(context, "Security Alert: Delegated support access cannot proceed because mandatory security audit logging failed.");
                        return;
                    }
                    finally
                    {
                        dbContext.IgnoreTenantFilter = false;
                    }
                }

                await _next(context);
                return;
            }

            // 5. Standard Authenticated Users: Require Verified, Active Tenant Membership
            if (headerTenantId.HasValue)
            {
                var membership = await dbContext.TenantMemberships
                    .IgnoreQueryFilters()
                    .Include(m => m.Tenant)
                    .FirstOrDefaultAsync(m => m.UserId == userId && m.TenantId == headerTenantId.Value);

                if (membership == null)
                {
                    if (isFromHeader)
                    {
                        _logger.LogWarning("SECURITY ALERT: User {UserId} attempted unauthorized access to tenant {TenantId} via header tampering.", userId, headerTenantId.Value);
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        await WriteForbiddenOrErrorAsync(context, "Forbidden: You do not have active membership in the specified company.");
                        return;
                    }
                    else
                    {
                        // Clean up stale or invalid cookie and fallback to default membership
                        context.Response.Cookies.Delete(ActiveTenantCookieName);
                        headerTenantId = null;
                    }
                }
                else if (!membership.IsActive)
                {
                    _logger.LogWarning("SECURITY ALERT: User {UserId} attempted access with inactive membership to tenant {TenantId}.", userId, headerTenantId.Value);
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await WriteForbiddenOrErrorAsync(context, "Forbidden: Your organization membership has been deactivated.");
                    return;
                }
                else if (membership.Tenant == null || !membership.Tenant.IsActive)
                {
                    _logger.LogWarning("SECURITY ALERT: User {UserId} attempted access to suspended tenant {TenantId}.", userId, headerTenantId.Value);
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await WriteForbiddenOrErrorAsync(context, "Forbidden: Your organization account is currently inactive or suspended.");
                    return;
                }
                else
                {
                    ApplyTenantRoleClaims(context.User, membership.CompanyRole);
                    tenantContext.SetTenant(membership.TenantId, membership.Tenant.Slug);
                }
            }

            if (!headerTenantId.HasValue)
            {
                var tenantClaim = context.User.FindFirst(TenantIdClaimType)?.Value;
                TenantMembership? activeMembership = null;

                if (!string.IsNullOrEmpty(tenantClaim) && Guid.TryParse(tenantClaim, out var claimTenantId))
                {
                    activeMembership = await dbContext.TenantMemberships
                        .IgnoreQueryFilters()
                        .Include(m => m.Tenant)
                        .FirstOrDefaultAsync(m => m.UserId == userId && m.TenantId == claimTenantId);
                }

                if (activeMembership == null)
                {
                    activeMembership = await dbContext.TenantMemberships
                        .IgnoreQueryFilters()
                        .Include(m => m.Tenant)
                        .Where(m => m.UserId == userId)
                        .OrderByDescending(m => m.JoinedAt)
                        .FirstOrDefaultAsync();
                }

                if (activeMembership != null)
                {
                    if (!activeMembership.IsActive)
                    {
                        _logger.LogWarning("User {UserId} has deactivated membership in tenant {TenantId}.", userId, activeMembership.TenantId);
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        await WriteForbiddenOrErrorAsync(context, "Forbidden: Your organization membership has been deactivated.");
                        return;
                    }
                    else if (activeMembership.Tenant == null || !activeMembership.Tenant.IsActive)
                    {
                        _logger.LogWarning("Tenant {TenantId} is suspended for user {UserId}.", activeMembership.TenantId, userId);
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        await WriteForbiddenOrErrorAsync(context, "Forbidden: Your organization account is currently inactive or suspended.");
                        return;
                    }
                    else
                    {
                        ApplyTenantRoleClaims(context.User, activeMembership.CompanyRole);
                        tenantContext.SetTenant(activeMembership.TenantId, activeMembership.Tenant.Slug);
                    }
                }
                else
                {
                    _logger.LogWarning("Authenticated user {UserId} has no company memberships.", userId);
                }
            }

            await _next(context);
        }

        private static void ApplyTenantRoleClaims(ClaimsPrincipal principal, AI_Powered_CRM.Models.Enums.CompanyRole companyRole)
        {
            var identity = principal.Identities.FirstOrDefault(i => i.IsAuthenticated);
            if (identity == null)
                return;

            // Tenant roles must come from the active membership, not from stale/global role claims.
            var tenantRoleNames = new[] { "Admin", "CompanyAdmin", "Manager", "SalesExecutive" };
            foreach (var claim in identity.FindAll(ClaimTypes.Role).Where(c => tenantRoleNames.Contains(c.Value)).ToList())
                identity.RemoveClaim(claim);

            switch (companyRole)
            {
                case AI_Powered_CRM.Models.Enums.CompanyRole.CompanyAdmin:
                    identity.AddClaim(new Claim(ClaimTypes.Role, "Admin"));
                    identity.AddClaim(new Claim(ClaimTypes.Role, "CompanyAdmin"));
                    break;
                case AI_Powered_CRM.Models.Enums.CompanyRole.Manager:
                    identity.AddClaim(new Claim(ClaimTypes.Role, "Manager"));
                    break;
                case AI_Powered_CRM.Models.Enums.CompanyRole.SalesExecutive:
                    identity.AddClaim(new Claim(ClaimTypes.Role, "SalesExecutive"));
                    break;
            }
        }

        private static async Task WriteForbiddenOrErrorAsync(HttpContext context, string message)
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(new { error = message }));
            }
            else
            {
                context.Response.ContentType = "text/plain";
                await context.Response.WriteAsync(message);
            }
        }
    }
}
