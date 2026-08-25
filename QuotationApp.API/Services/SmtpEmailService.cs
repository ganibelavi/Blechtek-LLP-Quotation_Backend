using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;

namespace QuotationApp.API.Services
{
    public class SmtpOptions
    {
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 25;
        public bool EnableSsl { get; set; } = false;
        public string Username { get; set; }
        public string Password { get; set; }
        public string From { get; set; }
    }

    public class SmtpEmailService : IEmailService
    {
        private readonly SmtpOptions _options;
        private readonly ILogger<SmtpEmailService> _logger;

        public SmtpEmailService(IOptions<SmtpOptions> options, ILogger<SmtpEmailService> logger)
        {
            _options = options.Value;
            _logger = logger;
        }

        public Task SendQuotationEmailAsync(string quotationId, string recipientEmail, string subject, string body, string attachmentPath = null)
        {
            // Basic validations
            if (string.IsNullOrWhiteSpace(recipientEmail)) throw new ArgumentException("Recipient email is required", nameof(recipientEmail));
            if (string.IsNullOrWhiteSpace(subject)) subject = "Quotation from BlechTek";

            using var msg = new MailMessage();
            msg.From = new MailAddress(string.IsNullOrWhiteSpace(_options.From) ? "no-reply@blechtek.local" : _options.From);
            msg.To.Add(new MailAddress(recipientEmail));
            msg.Subject = subject;
            msg.Body = body ?? "Please find attached the quotation.";
            msg.IsBodyHtml = false;

            if (!string.IsNullOrWhiteSpace(attachmentPath) && File.Exists(attachmentPath))
            {
                msg.Attachments.Add(new Attachment(attachmentPath));
            }

            var client = new SmtpClient(_options.Host, _options.Port)
            {
                EnableSsl = _options.EnableSsl,
            };

            if (!string.IsNullOrWhiteSpace(_options.Username))
            {
                client.Credentials = new NetworkCredential(_options.Username, _options.Password);
            }

            try
            {
                client.Send(msg);
                _logger.LogInformation("Quotation {QuotationId} email sent to {Recipient}", quotationId, recipientEmail);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send email for quotation {QuotationId} to {Recipient}", quotationId, recipientEmail);
                throw;
            }

            return Task.CompletedTask;
        }
    }
}
