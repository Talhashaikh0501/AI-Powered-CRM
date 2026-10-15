using System;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using AI_Powered_CRM.Data;
using AI_Powered_CRM.Models;
using AI_Powered_CRM.Models.Entities;
using AI_Powered_CRM.Models.Enums;
using AI_Powered_CRM.Security;
using AI_Powered_CRM.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace AI_Powered_CRM.Services.Implementations
{
    public class SupportAccessService : ISupportAccessService
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ISystemSecurityContext _systemSecurityContext;
        private readonly ILogger<SupportAccessService> _logger;

        public SupportAccessService(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IHttpContextAccessor httpContextAccessor,
            ISystemSecurityContext systemSecurityContext,
            ILogger<SupportAccessService> logger)
        {
            _context = context;
            _userManager = userManager;
            _httpContextAccessor = httpContextAccessor;
            _systemSecurityContext = systemSecurityContext;
            _logger = logger;
        }

        private string ResolveTrustedUserId(ClaimsPrincipal? explicitCaller, string? assertedUserId, string operationName)
        {
            var principal = explicitCaller ?? _httpContextAccessor.HttpContext?.User;
            if (principal?.Identity?.IsAuthenticated == true)
            {
                var authenticatedId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
                if (!string.IsNullOrWhiteSpace(authenticatedId))
                {
                    if (!string.IsNullOrWhiteSpace(assertedUserId) &&
                        !string.Equals(assertedUserId, authenticatedId, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogWarning("Security Alert: Authenticated user {AuthId} attempted {Operation} asserting mismatched client user ID {AssertedId}.",
                            authenticatedId, operationName, assertedUserId);
                        throw new System.Security.SecurityException(
                            $"Identity mismatch: Server authenticated user '{authenticatedId}' does not match asserted user ID '{assertedUserId}'.");
                    }
                    return authenticatedId;
                }
            }

            if (_systemSecurityContext.IsSystemExecution)
            {
                return $"SYSTEM:{_systemSecurityContext.SystemExecutionReason}";
            }

            _logger.LogWarning("Security Violation: Attempted {Operation} without authenticated server principal or authorized system execution context.", operationName);
            throw new UnauthorizedAccessException(
                $"Authentication required: No trusted server-side authentication principal or authorized system execution context exists for {operationName}. Arbitrary unauthenticated client user IDs are strictly rejected.");
        }

        public Task<SupportAccessTicket> CreateTicketAsync(
            Guid tenantId, 
            string superAdminUserId, 
            string reason, 
            string assertedCreatorUserId)
        {
            return CreateTicketInternalAsync(tenantId, superAdminUserId, reason, null, assertedCreatorUserId);
        }

        public Task<SupportAccessTicket> CreateTicketAsync(
            Guid tenantId, 
            string superAdminUserId, 
            string reason, 
            ClaimsPrincipal? creatorPrincipal = null)
        {
            return CreateTicketInternalAsync(tenantId, superAdminUserId, reason, creatorPrincipal, null);
        }

        private async Task<SupportAccessTicket> CreateTicketInternalAsync(
            Guid tenantId, 
            string superAdminUserId, 
            string reason, 
            ClaimsPrincipal? creatorPrincipal,
            string? assertedCreatorUserId)
        {
            if (tenantId == Guid.Empty)
                throw new ArgumentException("A valid TenantId must be specified.", nameof(tenantId));

            if (string.IsNullOrWhiteSpace(superAdminUserId))
                throw new ArgumentException("An assigned Platform SuperAdmin user ID is strictly required.", nameof(superAdminUserId));

            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("A detailed business reason is strictly required.", nameof(reason));

            var creatorId = ResolveTrustedUserId(creatorPrincipal, assertedCreatorUserId, "ticket creation");

            // Verify target tenant exists and is active
            var targetTenant = await _context.Tenants
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.TenantId == tenantId);

            if (targetTenant == null || !targetTenant.IsActive)
                throw new InvalidOperationException($"Target tenant '{tenantId}' was not found or is inactive.");

            // If not running under system automation, verify user existence, status, and authority
            if (!creatorId.StartsWith("SYSTEM:"))
            {
                var creatorUser = await _userManager.FindByIdAsync(creatorId);
                if (creatorUser == null)
                    throw new InvalidOperationException($"Creator user '{creatorId}' does not exist.");

                if (!creatorUser.IsActive)
                    throw new UnauthorizedAccessException($"Creator user '{creatorId}' is inactive or suspended.");

                var isCreatorSuperAdmin = await _userManager.IsInRoleAsync(creatorUser, "SuperAdmin") ||
                                          await _userManager.IsInRoleAsync(creatorUser, "PlatformSuperAdmin");

                var isCreatorTenantAdmin = false;
                if (!isCreatorSuperAdmin)
                {
                    isCreatorTenantAdmin = await _context.TenantMemberships
                        .IgnoreQueryFilters()
                        .AnyAsync(m => m.TenantId == tenantId &&
                                       m.UserId == creatorUser.Id &&
                                       m.IsActive &&
                                       (m.CompanyRole == CompanyRole.CompanyAdmin || m.CompanyRole.ToString() == "CompanyAdmin" || m.CompanyRole.ToString() == "Admin"));
                }

                if (!isCreatorSuperAdmin && !isCreatorTenantAdmin)
                {
                    _logger.LogWarning("Security Alert: User {UserId} lacking authority attempted to create support ticket for tenant {TenantId}.",
                        creatorId, tenantId);
                    throw new UnauthorizedAccessException($"User '{creatorId}' is not authorized to create support tickets for tenant '{tenantId}'.");
                }
            }

            // Verify assigned SuperAdmin user exists, is active, and has SuperAdmin role
            var superAdminUser = await _userManager.FindByIdAsync(superAdminUserId);
            if (superAdminUser == null)
                throw new InvalidOperationException($"Designated SuperAdmin user '{superAdminUserId}' does not exist.");

            if (!superAdminUser.IsActive)
                throw new InvalidOperationException($"Designated SuperAdmin user '{superAdminUserId}' is inactive.");

            var isSuperAdmin = await _userManager.IsInRoleAsync(superAdminUser, "SuperAdmin") ||
                               await _userManager.IsInRoleAsync(superAdminUser, "PlatformSuperAdmin");
            if (!isSuperAdmin)
                throw new InvalidOperationException($"User '{superAdminUserId}' does not possess the Platform SuperAdmin role.");

            var ticketNumber = $"TKT-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

            var ticket = new SupportAccessTicket
            {
                TicketId = Guid.NewGuid(),
                TicketNumber = ticketNumber,
                TenantId = tenantId,
                SuperAdminUserId = superAdminUserId,
                CreatedByUserId = creatorId,
                Reason = reason.Trim(),
                Status = "Pending",
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(4)
            };

            var auditLog = new AuditLog
            {
                TenantId = tenantId,
                UserId = creatorId.StartsWith("SYSTEM:") ? null : creatorId,
                EntityType = "SupportAccessTicket",
                EntityId = ticket.TicketId.ToString(),
                Action = "SupportTicketCreated",
                DetailsJson = JsonSerializer.Serialize(new
                {
                    ticket.TicketNumber,
                    ticket.TenantId,
                    ticket.SuperAdminUserId,
                    ticket.CreatedByUserId,
                    ticket.Reason,
                    ticket.Status,
                    CreatedBy = creatorId
                }),
                CreatedAt = DateTime.UtcNow
            };

            var isRelational = _context.Database.IsRelational();
            IDbContextTransaction? tx = isRelational ? await _context.Database.BeginTransactionAsync() : null;

            var previousIgnore = _context.IgnoreTenantFilter;
            try
            {
                _context.IgnoreTenantFilter = true;
                _context.SupportAccessTickets.Add(ticket);
                _context.AuditLogs.Add(auditLog);
                await _context.SaveChangesAsync();

                if (tx != null)
                {
                    await tx.CommitAsync();
                }
            }
            catch (Exception ex)
            {
                if (tx != null)
                {
                    await tx.RollbackAsync();
                }
                _context.Entry(ticket).State = EntityState.Detached;
                _context.Entry(auditLog).State = EntityState.Detached;

                _logger.LogCritical(ex, "Failed to persist atomic support ticket creation for {TicketNumber}. Changes rolled back.", ticket.TicketNumber);
                throw new InvalidOperationException($"Security audit failure: Unable to persist creation audit for ticket {ticket.TicketNumber}. Changes rolled back.", ex);
            }
            finally
            {
                _context.IgnoreTenantFilter = previousIgnore;
                tx?.Dispose();
            }

            return ticket;
        }

        public Task<SupportAccessTicket> ApproveTicketAsync(
            Guid ticketId, 
            string assertedApproverUserId, 
            TimeSpan duration)
        {
            return ApproveTicketInternalAsync(ticketId, duration, null, assertedApproverUserId);
        }

        public Task<SupportAccessTicket> ApproveTicketAsync(
            Guid ticketId, 
            TimeSpan? duration = null, 
            ClaimsPrincipal? approverPrincipal = null)
        {
            return ApproveTicketInternalAsync(ticketId, duration ?? TimeSpan.FromHours(4), approverPrincipal, null);
        }

        private async Task<SupportAccessTicket> ApproveTicketInternalAsync(
            Guid ticketId, 
            TimeSpan duration, 
            ClaimsPrincipal? approverPrincipal,
            string? assertedApproverUserId)
        {
            if (ticketId == Guid.Empty)
                throw new ArgumentException("A valid TicketId must be specified.", nameof(ticketId));

            var approverId = ResolveTrustedUserId(approverPrincipal, assertedApproverUserId, "ticket approval");

            var previousIgnore = _context.IgnoreTenantFilter;
            SupportAccessTicket? ticket;
            try
            {
                _context.IgnoreTenantFilter = true;
                ticket = await _context.SupportAccessTickets
                    .FirstOrDefaultAsync(t => t.TicketId == ticketId);
            }
            finally
            {
                _context.IgnoreTenantFilter = previousIgnore;
            }

            if (ticket == null)
                throw new KeyNotFoundException($"Support access ticket '{ticketId}' was not found.");

            if (!string.Equals(ticket.Status, "Pending", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Ticket '{ticket.TicketNumber}' is not in Pending status (Current: {ticket.Status}).");

            // System execution cannot approve tickets (delegated support access requires independent human authorization)
            if (approverId.StartsWith("SYSTEM:"))
            {
                throw new InvalidOperationException("Automated system execution context cannot approve delegated support access tickets. Explicit independent authorized human approval is strictly required.");
            }

            // Approver must exist and be active
            var approverUser = await _userManager.FindByIdAsync(approverId);
            if (approverUser == null)
                throw new InvalidOperationException($"Approver user '{approverId}' does not exist.");

            if (!approverUser.IsActive)
                throw new UnauthorizedAccessException($"Approver user '{approverId}' is inactive or suspended.");

            // Strict Anti-Self-Approval Verification
            if (string.Equals(approverUser.Id, ticket.SuperAdminUserId, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Security Alert: Assigned SuperAdmin {ApproverId} attempted self-approval of ticket {TicketNumber}.", approverUser.Id, ticket.TicketNumber);
                throw new InvalidOperationException("Self-approval is strictly forbidden. The designated SuperAdmin assignee cannot approve their own support ticket.");
            }

            if (string.Equals(approverUser.Id, ticket.CreatedByUserId, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Security Alert: Ticket creator {ApproverId} attempted self-approval of ticket {TicketNumber}.", approverUser.Id, ticket.TicketNumber);
                throw new InvalidOperationException("Self-approval is strictly forbidden. The ticket creator/requester cannot approve their own support ticket.");
            }

            // Strict Authority Verification:
            // Must be an active CompanyAdmin of ticket.TenantId OR an independent Platform SuperAdmin
            var isSuperAdmin = await _userManager.IsInRoleAsync(approverUser, "SuperAdmin") ||
                               await _userManager.IsInRoleAsync(approverUser, "PlatformSuperAdmin");

            var isCompanyAdminOfTenant = false;
            if (!isSuperAdmin)
            {
                isCompanyAdminOfTenant = await _context.TenantMemberships
                    .IgnoreQueryFilters()
                    .AnyAsync(m => m.TenantId == ticket.TenantId &&
                                   m.UserId == approverUser.Id &&
                                   m.IsActive &&
                                   (m.CompanyRole == CompanyRole.CompanyAdmin || m.CompanyRole.ToString() == "CompanyAdmin" || m.CompanyRole.ToString() == "Admin"));
            }

            if (!isSuperAdmin && !isCompanyAdminOfTenant)
            {
                _logger.LogWarning("Security Alert: User {UserId} lacking CompanyAdmin or SuperAdmin authority attempted to approve ticket {TicketNumber} for tenant {TenantId}.",
                    approverUser.Id, ticket.TicketNumber, ticket.TenantId);
                throw new UnauthorizedAccessException($"User '{approverUser.Id}' is not authorized to approve support tickets for tenant '{ticket.TenantId}'. Approver must have an active CompanyAdmin membership in the target tenant or independent Platform SuperAdmin role.");
            }

            var previousStatus = ticket.Status;
            var previousApprovedBy = ticket.ApprovedByUserId;
            var previousApprovedAt = ticket.ApprovedAt;
            var previousExpiresAt = ticket.ExpiresAt;

            ticket.Status = "Approved";
            ticket.ApprovedByUserId = approverUser.Id;
            ticket.ApprovedAt = DateTime.UtcNow;
            ticket.ExpiresAt = DateTime.UtcNow.Add(duration > TimeSpan.Zero ? duration : TimeSpan.FromHours(4));

            var auditLog = new AuditLog
            {
                TenantId = ticket.TenantId,
                UserId = approverUser.Id,
                EntityType = "SupportAccessTicket",
                EntityId = ticket.TicketId.ToString(),
                Action = "SupportTicketApproved",
                DetailsJson = JsonSerializer.Serialize(new
                {
                    ticket.TicketNumber,
                    ticket.TenantId,
                    ticket.SuperAdminUserId,
                    ticket.ApprovedByUserId,
                    ticket.ApprovedAt,
                    ticket.ExpiresAt
                }),
                CreatedAt = DateTime.UtcNow
            };

            var isRelational = _context.Database.IsRelational();
            IDbContextTransaction? tx = isRelational ? await _context.Database.BeginTransactionAsync() : null;

            try
            {
                _context.IgnoreTenantFilter = true;
                _context.AuditLogs.Add(auditLog);
                await _context.SaveChangesAsync();

                if (tx != null)
                {
                    await tx.CommitAsync();
                }
            }
            catch (Exception ex)
            {
                if (tx != null)
                {
                    await tx.RollbackAsync();
                }

                // Explicitly revert in-memory entity properties and reset tracker
                ticket.Status = previousStatus;
                ticket.ApprovedByUserId = previousApprovedBy;
                ticket.ApprovedAt = previousApprovedAt;
                ticket.ExpiresAt = previousExpiresAt;

                _context.Entry(ticket).State = EntityState.Unchanged;
                _context.Entry(auditLog).State = EntityState.Detached;

                _logger.LogCritical(ex, "CRITICAL: Audit log failed after ticket approval for {TicketNumber}. Rolling back and failing closed.", ticket.TicketNumber);
                throw new InvalidOperationException($"Security audit failure: Unable to persist approval audit for ticket {ticket.TicketNumber}. Changes rolled back.", ex);
            }
            finally
            {
                _context.IgnoreTenantFilter = previousIgnore;
                tx?.Dispose();
            }

            return ticket;
        }

        public Task<SupportAccessTicket> RevokeTicketAsync(
            Guid ticketId, 
            string assertedRevokerUserId, 
            string reason)
        {
            return RevokeTicketInternalAsync(ticketId, reason, null, assertedRevokerUserId);
        }

        public Task<SupportAccessTicket> RevokeTicketAsync(
            Guid ticketId, 
            string reason, 
            ClaimsPrincipal? revokerPrincipal = null)
        {
            return RevokeTicketInternalAsync(ticketId, reason, revokerPrincipal, null);
        }

        private async Task<SupportAccessTicket> RevokeTicketInternalAsync(
            Guid ticketId, 
            string reason, 
            ClaimsPrincipal? revokerPrincipal,
            string? assertedRevokerUserId)
        {
            if (ticketId == Guid.Empty)
                throw new ArgumentException("A valid TicketId must be specified.", nameof(ticketId));

            var revokerId = ResolveTrustedUserId(revokerPrincipal, assertedRevokerUserId, "ticket revocation");

            var previousIgnore = _context.IgnoreTenantFilter;
            SupportAccessTicket? ticket;
            try
            {
                _context.IgnoreTenantFilter = true;
                ticket = await _context.SupportAccessTickets.FirstOrDefaultAsync(t => t.TicketId == ticketId);
            }
            finally
            {
                _context.IgnoreTenantFilter = previousIgnore;
            }

            if (ticket == null)
                throw new KeyNotFoundException($"Support access ticket '{ticketId}' was not found.");

            if (!revokerId.StartsWith("SYSTEM:"))
            {
                var revokerUser = await _userManager.FindByIdAsync(revokerId);
                if (revokerUser == null)
                    throw new InvalidOperationException($"Revoker user '{revokerId}' does not exist.");

                if (!revokerUser.IsActive)
                    throw new UnauthorizedAccessException($"Revoker user '{revokerId}' is inactive or suspended.");

                // Revoker must be a SuperAdmin, ticket creator, assigned SuperAdmin, or CompanyAdmin of the tenant
                var isSuperAdmin = await _userManager.IsInRoleAsync(revokerUser, "SuperAdmin") ||
                                   await _userManager.IsInRoleAsync(revokerUser, "PlatformSuperAdmin");

                var isCompanyAdminOfTenant = false;
                if (!isSuperAdmin)
                {
                    isCompanyAdminOfTenant = await _context.TenantMemberships
                        .IgnoreQueryFilters()
                        .AnyAsync(m => m.TenantId == ticket.TenantId &&
                                       m.UserId == revokerUser.Id &&
                                       m.IsActive &&
                                       (m.CompanyRole == CompanyRole.CompanyAdmin || m.CompanyRole.ToString() == "CompanyAdmin" || m.CompanyRole.ToString() == "Admin"));
                }

                var isPartyToTicket = string.Equals(revokerUser.Id, ticket.CreatedByUserId, StringComparison.OrdinalIgnoreCase) ||
                                      string.Equals(revokerUser.Id, ticket.SuperAdminUserId, StringComparison.OrdinalIgnoreCase);

                if (!isSuperAdmin && !isCompanyAdminOfTenant && !isPartyToTicket)
                {
                    _logger.LogWarning("Security Alert: User {UserId} lacking authority attempted to revoke ticket {TicketNumber}.",
                        revokerUser.Id, ticket.TicketNumber);
                    throw new UnauthorizedAccessException($"User '{revokerUser.Id}' is not authorized to revoke ticket '{ticket.TicketNumber}'.");
                }
            }

            var previousStatus = ticket.Status;
            var previousExpiresAt = ticket.ExpiresAt;

            ticket.Status = "Revoked";
            ticket.ExpiresAt = DateTime.UtcNow;

            var auditLog = new AuditLog
            {
                TenantId = ticket.TenantId,
                UserId = revokerId.StartsWith("SYSTEM:") ? null : revokerId,
                EntityType = "SupportAccessTicket",
                EntityId = ticket.TicketId.ToString(),
                Action = "SupportTicketRevoked",
                DetailsJson = JsonSerializer.Serialize(new
                {
                    ticket.TicketNumber,
                    RevokedBy = revokerId,
                    Reason = reason ?? "Revoked by authorized administrator",
                    PreviousStatus = previousStatus
                }),
                CreatedAt = DateTime.UtcNow
            };

            var isRelational = _context.Database.IsRelational();
            IDbContextTransaction? tx = isRelational ? await _context.Database.BeginTransactionAsync() : null;

            try
            {
                _context.IgnoreTenantFilter = true;
                _context.AuditLogs.Add(auditLog);
                await _context.SaveChangesAsync();

                if (tx != null)
                {
                    await tx.CommitAsync();
                }
            }
            catch (Exception ex)
            {
                if (tx != null)
                {
                    await tx.RollbackAsync();
                }

                ticket.Status = previousStatus;
                ticket.ExpiresAt = previousExpiresAt;

                _context.Entry(ticket).State = EntityState.Unchanged;
                _context.Entry(auditLog).State = EntityState.Detached;

                _logger.LogCritical(ex, "Failed to persist atomic ticket revocation for {TicketNumber}. Changes rolled back.", ticket.TicketNumber);
                throw new InvalidOperationException($"Security audit failure: Unable to persist revocation audit for ticket {ticket.TicketNumber}. Changes rolled back.", ex);
            }
            finally
            {
                _context.IgnoreTenantFilter = previousIgnore;
                tx?.Dispose();
            }

            return ticket;
        }

        public async Task<SupportAccessTicket?> GetTicketByIdAsync(Guid ticketId)
        {
            var previousIgnore = _context.IgnoreTenantFilter;
            try
            {
                _context.IgnoreTenantFilter = true;
                return await _context.SupportAccessTickets
                    .Include(t => t.Tenant)
                    .Include(t => t.SuperAdminUser)
                    .Include(t => t.CreatedByUser)
                    .Include(t => t.ApprovedByUser)
                    .FirstOrDefaultAsync(t => t.TicketId == ticketId);
            }
            finally
            {
                _context.IgnoreTenantFilter = previousIgnore;
            }
        }

        public async Task<System.Collections.Generic.List<SupportAccessTicket>> GetTicketsForTenantAsync(Guid tenantId)
        {
            var previousIgnore = _context.IgnoreTenantFilter;
            try
            {
                _context.IgnoreTenantFilter = true;
                return await _context.SupportAccessTickets
                    .Include(t => t.Tenant)
                    .Include(t => t.SuperAdminUser)
                    .Include(t => t.CreatedByUser)
                    .Include(t => t.ApprovedByUser)
                    .Where(t => t.TenantId == tenantId)
                    .OrderByDescending(t => t.CreatedAt)
                    .ToListAsync();
            }
            finally
            {
                _context.IgnoreTenantFilter = previousIgnore;
            }
        }

        public async Task<System.Collections.Generic.List<SupportAccessTicket>> GetAllTicketsAsync()
        {
            var previousIgnore = _context.IgnoreTenantFilter;
            try
            {
                _context.IgnoreTenantFilter = true;
                return await _context.SupportAccessTickets
                    .Include(t => t.Tenant)
                    .Include(t => t.SuperAdminUser)
                    .Include(t => t.CreatedByUser)
                    .Include(t => t.ApprovedByUser)
                    .OrderByDescending(t => t.CreatedAt)
                    .ToListAsync();
            }
            finally
            {
                _context.IgnoreTenantFilter = previousIgnore;
            }
        }
    }
}
