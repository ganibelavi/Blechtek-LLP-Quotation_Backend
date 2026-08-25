using System.Threading.Tasks;

namespace QuotationApp.API.Services
{
    public interface IEmailService
    {
        Task SendQuotationEmailAsync(string quotationId, string recipientEmail, string subject, string body, string attachmentPath = null);
    }
}
