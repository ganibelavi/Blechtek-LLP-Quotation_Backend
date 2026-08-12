using QuotationApp.API.Models;

namespace QuotationApp.API.Services;

public interface IQuotationService
{
    Task<QuotationResult> GenerateQuotationAsync(QuotationRequest request);

    /// <summary>Resolves a previously generated file's absolute path, or null if it doesn't exist.</summary>
    string? ResolveFilePath(string quotationId, string extension);
}
