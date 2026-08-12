using QuotationApp.API.Models;

namespace QuotationApp.API.Services;

public interface IWordGeneratorService
{
    /// <summary>
    /// Merges the request into Templates/QuotationTemplate.docx and writes the result to
    /// outputFolder/{quotationId}.docx. Returns the full path of the generated file.
    /// </summary>
    Task<string> GenerateAsync(QuotationRequest request, string quotationId);
}
