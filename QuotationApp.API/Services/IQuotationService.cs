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

    /// <summary>Gets dashboard analytics data.</summary>
    Task<DashboardData> GetDashboardDataAsync();

    /// <summary>Updates the discount percentage for an existing quotation and regenerates documents.</summary>
    Task<QuotationResult?> UpdateDiscountAsync(string quotationId, decimal discountPercentage);

    /// <summary>Gets the next auto-generated quotation number without creating a quotation.</summary>
    Task<string> GetNextQuotationNoAsync();
}

public class DashboardData
{
    public int TotalQuotations { get; set; }
    public int TotalOrganizations { get; set; }
    public int TotalModules { get; set; }
    public decimal TotalQuotedAmount { get; set; }
    public List<MonthlyQuoteData> MonthlyQuotes { get; set; } = new();
    public List<StatusBreakdownData> StatusBreakdown { get; set; } = new();
    public List<ModuleDistributionData> ModuleDistribution { get; set; } = new();
    public List<TopOrganizationData> TopOrganizations { get; set; } = new();
    public List<RecentQuotationData> RecentQuotations { get; set; } = new();
}

public class MonthlyQuoteData
{
    public string Month { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class StatusBreakdownData
{
    public string Status { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class ModuleDistributionData
{
    public string Module { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class TopOrganizationData
{
    public string Organization { get; set; } = string.Empty;
    public int QuoteCount { get; set; }
}

public class RecentQuotationData
{
    public string QuotationId { get; set; } = string.Empty;
    public string QuotationNo { get; set; } = string.Empty;
    public string OrganizationName { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public List<string> Modules { get; set; } = new();
    public decimal Valuation { get; set; }
    public decimal TotalQuotedAmount { get; set; }
    public decimal DiscountPercentage { get; set; }
}
