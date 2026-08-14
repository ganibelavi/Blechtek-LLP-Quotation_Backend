using QuotationApp.API.Models;

namespace QuotationApp.API.Services;

public interface IQuotationService
{
    Task<QuotationResult> GenerateQuotationAsync(QuotationRequest request);

    /// <summary>Resolves a previously generated file's absolute path, or null if it doesn't exist.</summary>
    string? ResolveFilePath(string quotationId, string extension);

    /// <summary>Retrieves quotation history from database.</summary>
    Task<List<QuotationHistoryEntry>> GetHistoryAsync(int page = 1, int pageSize = 20);

    /// <summary>Gets a single quotation by ID.</summary>
    Task<QuotationHistoryEntry?> GetQuotationAsync(string quotationId);
}
