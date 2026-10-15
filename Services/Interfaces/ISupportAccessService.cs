using System;
using System.Security.Claims;
using System.Threading.Tasks;
using AI_Powered_CRM.Models.Entities;

namespace AI_Powered_CRM.Services.Interfaces
{
    public interface ISupportAccessService
    {
        Task<SupportAccessTicket> CreateTicketAsync(Guid tenantId, string superAdminUserId, string reason, ClaimsPrincipal? creatorPrincipal = null);
        Task<SupportAccessTicket> CreateTicketAsync(Guid tenantId, string superAdminUserId, string reason, string assertedCreatorUserId);
        
        Task<SupportAccessTicket> ApproveTicketAsync(Guid ticketId, TimeSpan? duration = null, ClaimsPrincipal? approverPrincipal = null);
        Task<SupportAccessTicket> ApproveTicketAsync(Guid ticketId, string assertedApproverUserId, TimeSpan duration);
        
        Task<SupportAccessTicket> RevokeTicketAsync(Guid ticketId, string reason, ClaimsPrincipal? revokerPrincipal = null);
        Task<SupportAccessTicket> RevokeTicketAsync(Guid ticketId, string assertedRevokerUserId, string reason);
        
        Task<SupportAccessTicket?> GetTicketByIdAsync(Guid ticketId);
    }
}
