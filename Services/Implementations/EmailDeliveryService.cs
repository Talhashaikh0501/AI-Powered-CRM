using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Mail;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AI_Powered_CRM.Services.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AI_Powered_CRM.Services.Implementations
{
    public class EmailDeliveryService : IEmailDeliveryService
    {
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<EmailDeliveryService> _logger;

        public record CapturedEmail(string ToEmail, string Subject, string HtmlBody, DateTime CapturedAt);

        // In-memory capture store for development, manual inspection and automated testing
        public static readonly ConcurrentBag<CapturedEmail> CapturedEmails = new();
        public static string? LastSentConfirmationLink { get; private set; }
        public static string? LastSentInvitationLink { get; private set; }
        public static string? LastRecipientEmail { get; private set; }

        public EmailDeliveryService(
            IConfiguration configuration,
            IWebHostEnvironment environment,
            ILogger<EmailDeliveryService> logger)
        {
            _configuration = configuration;
            _environment = environment;
            _logger = logger;
        }

        public async Task<bool> SendEmailAsync(string toEmail, string subject, string htmlBody)
        {
            if (string.IsNullOrWhiteSpace(toEmail))
                return false;

            LastRecipientEmail = toEmail;

            var host = _configuration["Email:Smtp:Host"];
            var portString = _configuration["Email:Smtp:Port"];
            var username = _configuration["Email:Smtp:Username"];
            var password = _configuration["Email:Smtp:Password"];
            var fromEmail = _configuration["Email:FromEmail"] ?? "no-reply@aipoweredcrm.com";
            var fromName = _configuration["Email:FromName"] ?? "AI-Powered CRM";

            bool smtpConfigured = !string.IsNullOrWhiteSpace(host) && int.TryParse(portString, out _);

            if (smtpConfigured)
            {
                int.TryParse(portString, out var port);
                try
                {
                    using var client = new SmtpClient(host, port)
                    {
                        EnableSsl = _configuration.GetValue<bool>("Email:Smtp:EnableSsl", true),
                        Timeout = 10000
                    };

                    if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
                    {
                        client.Credentials = new NetworkCredential(username, password);
                    }

                    using var message = new MailMessage
                    {
                        From = new MailAddress(fromEmail, fromName),
                        Subject = subject,
                        Body = htmlBody,
                        IsBodyHtml = true
                    };
                    message.To.Add(toEmail);

                    await client.SendMailAsync(message);
                    _logger.LogInformation("Outbound transactional email successfully delivered to {Recipient} (Subject: {Subject}).", toEmail, subject);
                    return true;
                }
                catch (Exception ex)
                {
                    // Accurate status: SMTP delivery failed. Never falsely report true when SMTP fails.
                    _logger.LogError(ex, "Failed to deliver email to {Recipient} via configured SMTP server {Host}:{Port}.", toEmail, host, port);
                    return false;
                }
            }

            // SMTP is unconfigured.
            if (_environment.IsDevelopment())
            {
                // Development-only email capture
                CapturedEmails.Add(new CapturedEmail(toEmail, subject, htmlBody, DateTime.UtcNow));

                // Sanitize body preview to ensure sensitive tokens are not emitted to log streams
                var sanitizedPreview = Regex.Replace(htmlBody, @"token=[^&\s""'<]+", "token=[PROTECTED]");
                if (sanitizedPreview.Length > 160) sanitizedPreview = sanitizedPreview[..160];

                _logger.LogInformation("[DEV EMAIL CAPTURE] Captured email to {Recipient} | Subject: {Subject} | Preview: {Preview}...",
                    toEmail, subject, sanitizedPreview);

                return true;
            }

            // In production/staging, unconfigured SMTP is an error and must report false
            _logger.LogError("Email delivery rejected: No active SMTP server is configured in {Environment} environment. Outbound email to {Recipient} was not delivered.",
                _environment.EnvironmentName, toEmail);
            return false;
        }

        public Task<bool> SendEmailConfirmationAsync(string toEmail, string confirmationLink, string recipientName)
        {
            LastSentConfirmationLink = confirmationLink;
            LastRecipientEmail = toEmail;

            var subject = "Confirm your AI-Powered CRM email address";
            var body = $@"
<!DOCTYPE html>
<html>
<body style=""font-family: Arial, sans-serif; background-color: #f8fafc; padding: 24px;"">
    <div style=""max-width: 580px; margin: 0 auto; background: #ffffff; border-radius: 8px; padding: 32px; border: 1px solid #e2e8f0;"">
        <h2 style=""color: #1e293b; margin-top: 0;"">Verify your email address</h2>
        <p style=""color: #475569;"">Hello {WebUtility.HtmlEncode(recipientName)},</p>
        <p style=""color: #475569;"">Thank you for registering your company workspace with <strong>AI-Powered CRM</strong>. Please verify your email address to activate your account and access your CRM workspace.</p>
        <div style=""margin: 28px 0; text-align: center;"">
            <a href=""{confirmationLink}"" style=""background-color: #2563eb; color: #ffffff; text-decoration: none; padding: 12px 24px; border-radius: 6px; font-weight: bold; display: inline-block;"">Verify Email Address</a>
        </div>
        <p style=""color: #64748b; font-size: 13px;"">If the button above does not work, copy and paste this link into your browser:<br/><a href=""{confirmationLink}"" style=""color: #2563eb;"">{confirmationLink}</a></p>
        <p style=""color: #94a3b8; font-size: 12px; margin-top: 32px; border-top: 1px solid #e2e8f0; padding-top: 16px;"">If you did not create this account, please ignore this email.</p>
    </div>
</body>
</html>";

            return SendEmailAsync(toEmail, subject, body);
        }

        public Task<bool> SendInvitationEmailAsync(string toEmail, string invitationLink, string companyName, string invitedByName)
        {
            LastSentInvitationLink = invitationLink;
            LastRecipientEmail = toEmail;

            var subject = $"Invitation to join {companyName} on AI-Powered CRM";
            var body = $@"
<!DOCTYPE html>
<html>
<body style=""font-family: Arial, sans-serif; background-color: #f8fafc; padding: 24px;"">
    <div style=""max-width: 580px; margin: 0 auto; background: #ffffff; border-radius: 8px; padding: 32px; border: 1px solid #e2e8f0;"">
        <h2 style=""color: #1e293b; margin-top: 0;"">You're invited to join {WebUtility.HtmlEncode(companyName)}</h2>
        <p style=""color: #475569;""><strong>{WebUtility.HtmlEncode(invitedByName)}</strong> has invited you to collaborate in their AI-Powered CRM workspace.</p>
        <div style=""margin: 28px 0; text-align: center;"">
            <a href=""{invitationLink}"" style=""background-color: #059669; color: #ffffff; text-decoration: none; padding: 12px 24px; border-radius: 6px; font-weight: bold; display: inline-block;"">Accept Invitation</a>
        </div>
        <p style=""color: #64748b; font-size: 13px;"">This invitation link will expire in 7 days.<br/><a href=""{invitationLink}"" style=""color: #059669;"">{invitationLink}</a></p>
    </div>
</body>
</html>";

            return SendEmailAsync(toEmail, subject, body);
        }
    }
}
