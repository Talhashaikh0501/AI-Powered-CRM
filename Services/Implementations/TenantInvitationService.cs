using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using AI_Powered_CRM.Data;
using AI_Powered_CRM.Models;
using AI_Powered_CRM.Models.Entities;
using AI_Powered_CRM.Models.Enums;
using AI_Powered_CRM.Services.Interfaces;
using AI_Powered_CRM.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AI_Powered_CRM.Services.Implementations
{
    public class TenantInvitationService : ITenantInvitationService
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly ISubscriptionQuotaService _quotaService;
        private readonly IAuditService _auditService;
        private readonly ILogger<TenantInvitationService> _logger;

        public TenantInvitationService(
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            ISubscriptionQuotaService quotaService,
            IAuditService auditService,
            ILogger<TenantInvitationService> logger)
        {
            _db = db;
            _userManager = userManager;
            _roleManager = roleManager;
            _quotaService = quotaService;
            _auditService = auditService;
            _logger = logger;
        }

        public static string ComputeTokenHash(string rawToken)
        {
            if (string.IsNullOrWhiteSpace(rawToken)) return string.Empty;
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(rawToken.Trim()));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        public async Task<InvitationCreateResult> CreateInvitationAsync(Guid tenantId, string email, CompanyRole role, string invitedByUserId)
        {
            if (tenantId == Guid.Empty) throw new ArgumentException("Invalid tenant id", nameof(tenantId));
            if (string.IsNullOrWhiteSpace(email)) return InvitationCreateResult.Failure("Email address is required.");

            var normalizedEmail = email.Trim().ToLowerInvariant();

            // 1. Verify Tenant exists and is active
            var tenant = await _db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.TenantId == tenantId);
            if (tenant == null || !tenant.IsActive)
            {
                return InvitationCreateResult.Failure("Tenant not found or inactive.");
            }

            // 2. Check Subscription Quota
            if (!await _quotaService.CanInviteUserAsync(tenantId))
            {
                return InvitationCreateResult.Failure("User seat limit reached for your subscription plan. Please upgrade to invite more employees.");
            }

            // 3. Check if user is already an active member of this company
            var isAlreadyMember = await _db.TenantMemberships
                .IgnoreQueryFilters()
                .Include(m => m.User)
                .AnyAsync(m => m.TenantId == tenantId && m.User != null && m.User.NormalizedEmail == normalizedEmail.ToUpperInvariant() && m.IsActive);

            if (isAlreadyMember)
            {
                return InvitationCreateResult.Failure("A user with this email address is already an active member of this workspace.");
            }

            // 4. Check if pending unexpired invitation already exists
            var existingInvite = await _db.TenantInvitations
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(i => i.TenantId == tenantId && i.Email == normalizedEmail && !i.IsAccepted && i.ExpiresAt > DateTime.UtcNow);

            if (existingInvite != null)
            {
                return InvitationCreateResult.Failure("An active invitation has already been sent to this email address.");
            }

            // 5. Generate secure random 32-byte token and hash
            var rawToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            var tokenHash = ComputeTokenHash(rawToken);

            var invitation = new TenantInvitation
            {
                InvitationId = Guid.NewGuid(),
                TenantId = tenantId,
                Email = normalizedEmail,
                CompanyRole = role,
                TokenHash = tokenHash,
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                IsAccepted = false,
                InvitedByUserId = invitedByUserId,
                CreatedAt = DateTime.UtcNow
            };

            _db.TenantInvitations.Add(invitation);
            await _db.SaveChangesAsync();

            // 6. Audit Logging
            await _auditService.LogAsync(invitedByUserId, "TenantInvitation", invitation.InvitationId.ToString(), "EmployeeInvited",
                new { invitation.TenantId, invitation.Email, Role = role.ToString(), invitation.ExpiresAt });

            _logger.LogInformation("Invitation created for {Email} with role {Role} in Tenant {TenantId}",
                normalizedEmail, role, tenantId);

            return InvitationCreateResult.Success(invitation.InvitationId, rawToken);
        }

        public async Task<TenantInvitation?> ValidateInvitationTokenAsync(string rawToken)
        {
            if (string.IsNullOrWhiteSpace(rawToken)) return null;

            var tokenHash = ComputeTokenHash(rawToken);

            var invitation = await _db.TenantInvitations
                .IgnoreQueryFilters()
                .Include(i => i.Tenant)
                .FirstOrDefaultAsync(i => i.TokenHash == tokenHash);

            if (invitation == null) return null;
            if (invitation.IsAccepted) return null;
            if (invitation.ExpiresAt <= DateTime.UtcNow) return null;
            if (invitation.Tenant == null || !invitation.Tenant.IsActive) return null;

            return invitation;
        }

        public async Task<AcceptInvitationResult> AcceptInvitationAsync(string rawToken, string fullName, string password)
        {
            var invitation = await ValidateInvitationTokenAsync(rawToken);
            if (invitation == null)
            {
                return AcceptInvitationResult.Failure("This invitation is invalid, has expired, or has already been accepted.");
            }

            if (string.IsNullOrWhiteSpace(fullName))
            {
                return AcceptInvitationResult.Failure("Full name is required.");
            }

            if (_db.Database.IsRelational())
            {
                var strategy = _db.Database.CreateExecutionStrategy();
                return await strategy.ExecuteAsync(async () =>
                {
                    using var tx = await _db.Database.BeginTransactionAsync();
                    try
                    {
                        var result = await ExecuteAcceptanceStepsAsync(invitation, fullName, password);
                        if (result.Succeeded)
                        {
                            await tx.CommitAsync();
                        }
                        else
                        {
                            await tx.RollbackAsync();
                        }
                        return result;
                    }
                    catch (Exception ex)
                    {
                        await tx.RollbackAsync();
                        _logger.LogError(ex, "Failed to accept invitation {InvitationId}", invitation.InvitationId);
                        return AcceptInvitationResult.Failure("A system error occurred while accepting the invitation.");
                    }
                });
            }
            else
            {
                try
                {
                    return await ExecuteAcceptanceStepsAsync(invitation, fullName, password);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to accept invitation {InvitationId}", invitation.InvitationId);
                    return AcceptInvitationResult.Failure("A system error occurred while accepting the invitation.");
                }
            }
        }

        private async Task<AcceptInvitationResult> ExecuteAcceptanceStepsAsync(TenantInvitation invitation, string fullName, string password)
        {
            // Re-fetch invitation within transaction to prevent race conditions
            var invite = await _db.TenantInvitations
                .IgnoreQueryFilters()
                .Include(i => i.Tenant)
                .FirstOrDefaultAsync(i => i.InvitationId == invitation.InvitationId);

            if (invite == null || invite.IsAccepted || invite.ExpiresAt <= DateTime.UtcNow)
            {
                return AcceptInvitationResult.Failure("Invitation is no longer valid.");
            }

            // 1. Resolve or create ApplicationUser
            var normalizedEmail = invite.Email.ToUpperInvariant();
            var user = await _userManager.FindByEmailAsync(invite.Email);

            if (user == null)
            {
                user = new ApplicationUser
                {
                    UserName = invite.Email,
                    Email = invite.Email,
                    FullName = fullName.Trim(),
                    IsActive = true,
                    EmailConfirmed = true,
                    CreatedAt = DateTime.UtcNow
                };

                var userCreateResult = await _userManager.CreateAsync(user, password);
                if (!userCreateResult.Succeeded)
                {
                    var errors = userCreateResult.Errors.Select(e => e.Description).ToList();
                    return AcceptInvitationResult.Failure(errors.ToArray());
                }
            }
            else
            {
                if (!user.IsActive)
                {
                    return AcceptInvitationResult.Failure("User account is inactive. Please contact your administrator.");
                }

                // Update full name if not previously set
                if (string.IsNullOrWhiteSpace(user.FullName))
                {
                    user.FullName = fullName.Trim();
                    await _userManager.UpdateAsync(user);
                }
            }

            // 2. Assign Identity Role
            var identityRoleName = invite.CompanyRole switch
            {
                CompanyRole.CompanyAdmin => "CompanyAdmin",
                CompanyRole.Manager => "Manager",
                _ => "SalesExecutive"
            };

            if (!await _roleManager.RoleExistsAsync(identityRoleName))
            {
                await _roleManager.CreateAsync(new IdentityRole(identityRoleName));
            }

            if (!await _userManager.IsInRoleAsync(user, identityRoleName))
            {
                await _userManager.AddToRoleAsync(user, identityRoleName);
            }

            // 3. Create TenantMembership
            var existingMembership = await _db.TenantMemberships
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(m => m.TenantId == invite.TenantId && m.UserId == user.Id);

            if (existingMembership == null)
            {
                var membership = new TenantMembership
                {
                    MembershipId = Guid.NewGuid(),
                    TenantId = invite.TenantId,
                    UserId = user.Id,
                    CompanyRole = invite.CompanyRole,
                    IsActive = true,
                    JoinedAt = DateTime.UtcNow,
                    InvitedByUserId = invite.InvitedByUserId
                };
                _db.TenantMemberships.Add(membership);
            }
            else
            {
                existingMembership.IsActive = true;
                existingMembership.CompanyRole = invite.CompanyRole;
            }

            // 4. Mark invitation as accepted
            invite.IsAccepted = true;
            await _db.SaveChangesAsync();

            // 5. Audit Logging
            await _auditService.LogAsync(user.Id, "TenantInvitation", invite.InvitationId.ToString(), "InvitationAccepted",
                new { invite.TenantId, invite.Email, Role = invite.CompanyRole.ToString() });

            _logger.LogInformation("Invitation {InvitationId} accepted by user {UserId} in tenant {TenantId}",
                invite.InvitationId, user.Id, invite.TenantId);

            return AcceptInvitationResult.Success(user.Id, invite.TenantId);
        }

        public async Task<List<TenantInvitationItemDto>> GetTenantInvitationsAsync(Guid tenantId)
        {
            if (tenantId == Guid.Empty) return new List<TenantInvitationItemDto>();

            var invites = await _db.TenantInvitations
                .IgnoreQueryFilters()
                .Include(i => i.InvitedByUser)
                .Where(i => i.TenantId == tenantId)
                .OrderByDescending(i => i.CreatedAt)
                .ToListAsync();

            return invites.Select(i => new TenantInvitationItemDto
            {
                InvitationId = i.InvitationId,
                Email = i.Email,
                CompanyRole = i.CompanyRole,
                IsAccepted = i.IsAccepted,
                ExpiresAt = i.ExpiresAt,
                CreatedAt = i.CreatedAt,
                InvitedByUserName = i.InvitedByUser?.FullName ?? i.InvitedByUser?.UserName ?? "System"
            }).ToList();
        }

        public async Task<bool> RevokeInvitationAsync(Guid tenantId, Guid invitationId, string revokedByUserId)
        {
            if (tenantId == Guid.Empty || invitationId == Guid.Empty) return false;

            var invite = await _db.TenantInvitations
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(i => i.TenantId == tenantId && i.InvitationId == invitationId);

            if (invite == null || invite.IsAccepted) return false;

            _db.TenantInvitations.Remove(invite);
            await _db.SaveChangesAsync();

            await _auditService.LogAsync(revokedByUserId, "TenantInvitation", invitationId.ToString(), "InvitationRevoked",
                new { tenantId, invite.Email });

            _logger.LogInformation("Invitation {InvitationId} revoked by user {UserId}", invitationId, revokedByUserId);
            return true;
        }
    }
}
