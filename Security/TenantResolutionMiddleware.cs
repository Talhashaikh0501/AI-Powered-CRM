using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AI_Powered_CRM.Data;
using AI_Powered_CRM.Models.Entities;
using Microsoft.AspNetCore.Http;
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

        public TenantResolutionMiddleware(RequestDelegate next, ILogger<TenantResolutionMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext, ApplicationDbContext dbContext)
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

            // 3. Inspect requested tenant header (if provided)
            Guid? headerTenantId = null;
            if (context.Request.Headers.TryGetValue(TenantHeaderName, out var headerVal) &&
                Guid.TryParse(headerVal, out var parsedHeaderId))
            {
                headerTenantId = parsedHeaderId;
            }

            // 4. Handle Platform SuperAdmin: Require Server-Verified, Authorized, Audited Delegated Support Access
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

                    // Strict check: deliberate, server-verified support ticket is required
                    if (string.IsNullOrWhiteSpace(ticketRef))
                    {
                        _logger.LogWarning("Security Block: Platform SuperAdmin {UserId} attempted delegated tenant access to {TenantId} without authorized ticket reference.", userId, headerTenantId.Value);
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        await WriteForbiddenOrErrorAsync(context, "Forbidden: Platform SuperAdmin delegated tenant access requires an approved server-side support ticket (X-Support-Ticket).");
                        return;
                    }

                    // Server-side verification of approved support ticket in database
                    var now = DateTime.UtcNow;
                    var authorizedTicket = await dbContext.SupportAccessTickets
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
                    tenantContext.SetDelegatedSupportSession(targetTenant.TenantId, targetTenant.Slug, authorizedTicket.Reason);

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
                            Action = "PlatformSuperAdminDelegatedAccess",
                            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
                            {
                                TicketId = authorizedTicket.TicketId,
                                TicketNumber = authorizedTicket.TicketNumber,
                                VerifiedReason = authorizedTicket.Reason,
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
                    _logger.LogWarning("SECURITY ALERT: User {UserId} attempted unauthorized access to tenant {TenantId} via header tampering.", userId, headerTenantId.Value);
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await WriteForbiddenOrErrorAsync(context, "Forbidden: You do not have active membership in the specified company.");
                    return;
                }

                if (!membership.IsActive)
                {
                    _logger.LogWarning("SECURITY ALERT: User {UserId} attempted access with inactive membership to tenant {TenantId}.", userId, headerTenantId.Value);
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await WriteForbiddenOrErrorAsync(context, "Forbidden: Your organization membership has been deactivated.");
                    return;
                }

                if (membership.Tenant == null || !membership.Tenant.IsActive)
                {
                    _logger.LogWarning("SECURITY ALERT: User {UserId} attempted access to suspended tenant {TenantId}.", userId, headerTenantId.Value);
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await WriteForbiddenOrErrorAsync(context, "Forbidden: Your organization account is currently inactive or suspended.");
                    return;
                }

                tenantContext.SetTenant(membership.TenantId, membership.Tenant.Slug);
            }
            else
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
