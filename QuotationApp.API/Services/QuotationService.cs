using System.Text.Json;
using Microsoft.Extensions.Options;
using QuotationApp.API.Models;

namespace QuotationApp.API.Services;

/// <summary>
/// Coordinates: validate modules -> merge into Word template -> convert to PDF -> log history.
/// This is the single entry point the controller talks to.
/// </summary>
public class QuotationService : IQuotationService
{
    private readonly IWordGeneratorService _wordGenerator;
    private readonly IPdfConverterService _pdfConverter;
    private readonly IModuleService _moduleService;
    private readonly string _outputFolder;
    private readonly string _historyFile;
    private static readonly SemaphoreSlim HistoryLock = new(1, 1);

    public QuotationService(
        IWordGeneratorService wordGenerator,
        IPdfConverterService pdfConverter,
        IModuleService moduleService,
        IOptions<QuotationSettings> settings,
        IWebHostEnvironment env)
    {
        _wordGenerator = wordGenerator;
        _pdfConverter = pdfConverter;
        _moduleService = moduleService;
        _outputFolder = Path.Combine(env.ContentRootPath, settings.Value.OutputFolder);
        _historyFile = Path.Combine(_outputFolder, "history.json");
        Directory.CreateDirectory(_outputFolder);
    }

    public async Task<QuotationResult> GenerateQuotationAsync(QuotationRequest request)
    {
        await ValidateModulesAsync(request.SelectedModules);

        var quotationId = $"Q-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8]}";

        var docxPath = await _wordGenerator.GenerateAsync(request, quotationId);
        await _pdfConverter.ConvertToPdfAsync(docxPath);

        var result = new QuotationResult
        {
            QuotationId = quotationId,
            OrganizationName = request.OrganizationName,
            QuotationNo = request.QuotationNo,
            Date = request.Date,
            GeneratedAt = DateTime.UtcNow,
            WordDownloadUrl = $"/api/quotation/{quotationId}/download/word",
            PdfDownloadUrl = $"/api/quotation/{quotationId}/download/pdf"
        };

        await AppendHistoryAsync(result, request.SelectedModules);
        return result;
    }

    public string? ResolveFilePath(string quotationId, string extension)
    {
        // Guard against path traversal via the route-supplied id.
        if (quotationId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;

        var path = Path.Combine(_outputFolder, $"{quotationId}.{extension}");
        return File.Exists(path) ? path : null;
    }

    public Task<List<QuotationHistoryEntry>> GetHistoryAsync(int page = 1, int pageSize = 20)
    {
        // This service is deprecated (JSON-based). Use SqlQuotationService instead.
        throw new NotImplementedException("Use SqlQuotationService for history queries.");
    }

    public Task<QuotationHistoryEntry?> GetQuotationAsync(string quotationId)
    {
        // This service is deprecated (JSON-based). Use SqlQuotationService instead.
        throw new NotImplementedException("Use SqlQuotationService for history queries.");
    }

    public Task<DashboardData> GetDashboardDataAsync()
    {
        // This service is deprecated (JSON-based). Use SqlQuotationService instead.
        throw new NotImplementedException("Use SqlQuotationService for dashboard data.");
    }

    public Task<QuotationResult?> UpdateDiscountAsync(string quotationId, decimal discountPercentage)
    {
        // This service is deprecated (JSON-based). Use SqlQuotationService instead.
        throw new NotImplementedException("Use SqlQuotationService for discount updates.");
    }

    private async Task ValidateModulesAsync(List<string> selectedModules)
    {
        var master = await _moduleService.GetModulesAsync();
        var validNames = new HashSet<string>(master.Select(m => m.Module), StringComparer.OrdinalIgnoreCase);

        var unknown = selectedModules.Where(m => !validNames.Contains(m)).ToList();
        if (unknown.Count > 0)
            throw new ArgumentException($"Unknown module(s): {string.Join(", ", unknown)}");
    }

    private async Task AppendHistoryAsync(QuotationResult result, List<string> modules)
    {
        await HistoryLock.WaitAsync();
        try
        {
            var history = File.Exists(_historyFile)
                ? JsonSerializer.Deserialize<List<QuotationHistoryEntry>>(await File.ReadAllTextAsync(_historyFile)) ?? new()
                : new List<QuotationHistoryEntry>();

            history.Insert(0, new QuotationHistoryEntry
            {
                QuotationId = result.QuotationId,
                OrganizationName = result.OrganizationName,
                Modules = modules,
                GeneratedAt = result.GeneratedAt
            });

            await File.WriteAllTextAsync(_historyFile, JsonSerializer.Serialize(history, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            HistoryLock.Release();
        }
    }
}
