using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AI_Powered_CRM.Models.Entities;
using AI_Powered_CRM.Models.Enums;
using AI_Powered_CRM.ViewModels;

namespace AI_Powered_CRM.Services.Interfaces
{
    public class InvitationCreateResult
    {
        public bool Succeeded { get; set; }
        public Guid? InvitationId { get; set; }
        public string? RawToken { get; set; }
        public List<string> Errors { get; set; } = new List<string>();

        public static InvitationCreateResult Success(Guid invitationId, string rawToken) =>
            new InvitationCreateResult { Succeeded = true, InvitationId = invitationId, RawToken = rawToken };

        public static InvitationCreateResult Failure(params string[] errors) =>
            new InvitationCreateResult { Succeeded = false, Errors = new List<string>(errors) };
    }

    public class AcceptInvitationResult
    {
        public bool Succeeded { get; set; }
        public string? UserId { get; set; }
        public Guid? TenantId { get; set; }
        public List<string> Errors { get; set; } = new List<string>();

        public static AcceptInvitationResult Success(string userId, Guid tenantId) =>
            new AcceptInvitationResult { Succeeded = true, UserId = userId, TenantId = tenantId };

        public static AcceptInvitationResult Failure(params string[] errors) =>
            new AcceptInvitationResult { Succeeded = false, Errors = new List<string>(errors) };
    }

    public interface ITenantInvitationService
    {
        Task<InvitationCreateResult> CreateInvitationAsync(Guid tenantId, string email, CompanyRole role, string invitedByUserId);
        Task<TenantInvitation?> ValidateInvitationTokenAsync(string rawToken);
        Task<AcceptInvitationResult> AcceptInvitationAsync(string rawToken, string fullName, string password);
        Task<List<TenantInvitationItemDto>> GetTenantInvitationsAsync(Guid tenantId);
        Task<bool> RevokeInvitationAsync(Guid tenantId, Guid invitationId, string revokedByUserId);
    }
}
