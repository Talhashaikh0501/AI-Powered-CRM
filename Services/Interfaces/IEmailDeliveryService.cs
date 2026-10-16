using System.Threading.Tasks;

namespace AI_Powered_CRM.Services.Interfaces
{
    public interface IEmailDeliveryService
    {
        Task<bool> SendEmailAsync(string toEmail, string subject, string htmlBody);
        Task<bool> SendEmailConfirmationAsync(string toEmail, string confirmationLink, string recipientName);
        Task<bool> SendInvitationEmailAsync(string toEmail, string invitationLink, string companyName, string invitedByName);
    }
}
