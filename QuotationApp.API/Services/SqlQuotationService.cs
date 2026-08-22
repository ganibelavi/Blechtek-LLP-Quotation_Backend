using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuotationApp.API.Data;
using QuotationApp.API.Models;

namespace QuotationApp.API.Services;

/// <summary>
/// SQL-backed implementation of IQuotationService using Entity Framework Core.
/// Replaces the JSON-file-based QuotationService.
/// </summary>
public class SqlQuotationService : IQuotationService
{
    private readonly IWordGeneratorService _wordGenerator;
    private readonly IPdfConverterService _pdfConverter;
    private readonly IModuleService _moduleService;
    private readonly QuotationDbContext _dbContext;
    private readonly string _outputFolder;

    public SqlQuotationService(
        IWordGeneratorService wordGenerator,
        IPdfConverterService pdfConverter,
        IModuleService moduleService,
        QuotationDbContext dbContext,
        IOptions<QuotationSettings> settings,
        IWebHostEnvironment env)
    {
        _wordGenerator = wordGenerator;
        _pdfConverter = pdfConverter;
        _moduleService = moduleService;
        _dbContext = dbContext;
        _outputFolder = Path.Combine(env.ContentRootPath, settings.Value.OutputFolder);
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

        await SaveToDatabaseAsync(result, request);
        return result;
    }

    public string? ResolveFilePath(string quotationId, string extension)
    {
        // Guard against path traversal via the route-supplied id.
        if (quotationId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;

        var path = Path.Combine(_outputFolder, $"{quotationId}.{extension}");
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Retrieves quotation history from database.
    /// </summary>
    public async Task<List<QuotationHistoryEntry>> GetHistoryAsync(int page = 1, int pageSize = 20)
    {
        return await _dbContext.Quotations
            .AsNoTracking()
            .OrderByDescending(q => q.GeneratedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(q => new QuotationHistoryEntry
            {
                QuotationId = q.Id,
                OrganizationName = q.OrganizationName,
                QuotationNo = q.QuotationNo ?? string.Empty,
                Date = q.Date ?? DateTime.MinValue,
                ValidationDate = q.ValidationDate,
                QuotationToName = q.QuotationToName,
                QuotationToAddress = q.QuotationToAddress,
                QuotationToContactNo = q.QuotationToContactNo,
                QuotationToEmail = q.QuotationToEmail,
                ReferenceBy = q.ReferenceBy ?? string.Empty,
                Modules = q.QuotationModules.Select(m => m.ModuleName).ToList(),
                GeneratedAt = q.GeneratedAt,
                DiscountPercentage = q.DiscountPercentage
            })
            .ToListAsync();
    }

    /// <summary>
    /// Gets a single quotation by ID.
    /// </summary>
    public async Task<QuotationHistoryEntry?> GetQuotationAsync(string quotationId)
    {
        return await _dbContext.Quotations
            .AsNoTracking()
            .Where(q => q.Id == quotationId)
            .Select(q => new QuotationHistoryEntry
            {
                QuotationId = q.Id,
                OrganizationName = q.OrganizationName,
                QuotationNo = q.QuotationNo ?? string.Empty,
                Date = q.Date ?? DateTime.MinValue,
                ValidationDate = q.ValidationDate,
                QuotationToName = q.QuotationToName,
                QuotationToAddress = q.QuotationToAddress,
                QuotationToContactNo = q.QuotationToContactNo,
                QuotationToEmail = q.QuotationToEmail,
                ReferenceBy = q.ReferenceBy ?? string.Empty,
                Modules = q.QuotationModules.Select(m => m.ModuleName).ToList(),
                GeneratedAt = q.GeneratedAt,
                DiscountPercentage = q.DiscountPercentage
            })
            .FirstOrDefaultAsync();
    }

    public async Task<DashboardData> GetDashboardDataAsync()
    {
        var allQuotations = await _dbContext.Quotations
            .AsNoTracking()
            .Include(q => q.QuotationModules)
            .OrderByDescending(q => q.GeneratedAt)
            .ToListAsync();

        var modulePrices = await _dbContext.Modules
            .AsNoTracking()
            .ToDictionaryAsync(m => m.ModuleName, m => m.Price ?? 0);

        var totalQuotations = allQuotations.Count;
        var totalOrganizations = allQuotations.Select(q => q.OrganizationName).Distinct().Count();
        var totalModules = await _dbContext.Modules.CountAsync();

        // Calculate monthly quotes for the last 12 months
        var twelveMonthsAgo = DateTime.UtcNow.AddMonths(-12);
        var monthlyQuotes = allQuotations
            .Where(q => q.GeneratedAt >= twelveMonthsAgo)
            .GroupBy(q => new { q.GeneratedAt.Year, q.GeneratedAt.Month })
            .Select(g => new MonthlyQuoteData
            {
                Month = new DateTime(g.Key.Year, g.Key.Month, 1).ToString("MMM yyyy"),
                Count = g.Count()
            })
            .OrderBy(m => DateTime.ParseExact(m.Month, "MMM yyyy", System.Globalization.CultureInfo.InvariantCulture))
            .ToList();

        // Status breakdown - using ValidationDate to determine status
        var now = DateTime.UtcNow;
        var statusBreakdown = new List<StatusBreakdownData>
        {
            new() { Status = "Valid", Count = allQuotations.Count(q => q.ValidationDate >= now) },
            new() { Status = "Expired", Count = allQuotations.Count(q => q.ValidationDate < now) }
        };

        // Module distribution
        var moduleDistribution = await _dbContext.QuotationModules
            .AsNoTracking()
            .GroupBy(qm => qm.ModuleName)
            .Select(g => new ModuleDistributionData
            {
                Module = g.Key,
                Count = g.Count()
            })
            .OrderByDescending(m => m.Count)
            .Take(10)
            .ToListAsync();

        // Top organizations
        var topOrganizations = allQuotations
            .GroupBy(q => q.OrganizationName)
            .Select(g => new TopOrganizationData
            {
                Organization = g.Key,
                QuoteCount = g.Count()
            })
            .OrderByDescending(o => o.QuoteCount)
            .Take(5)
            .ToList();

        // Calculate total quoted amount across all quotations
        var totalQuotedAmount = allQuotations.Sum(q =>
        {
            var totalPrice = q.QuotationModules.Sum(m => modulePrices.GetValueOrDefault(m.ModuleName, 0));
            var discountPercentage = q.DiscountPercentage ?? 0;
            var discountAmount = totalPrice * discountPercentage / 100;
            return totalPrice - discountAmount;
        });

        // Recent quotations (last 10) with valuation calculations
        var recentQuotations = allQuotations
            .Take(10)
            .Select(q =>
            {
                var totalPrice = q.QuotationModules.Sum(m => modulePrices.GetValueOrDefault(m.ModuleName, 0));
                var discountPercentage = q.DiscountPercentage ?? 0;
                var discountAmount = totalPrice * discountPercentage / 100;
                var finalPrice = totalPrice - discountAmount;

                return new RecentQuotationData
                {
                    QuotationId = q.Id,
                    QuotationNo = q.QuotationNo ?? string.Empty,
                    OrganizationName = q.OrganizationName,
                    GeneratedAt = q.GeneratedAt,
                    Modules = q.QuotationModules.Select(m => m.ModuleName).ToList(),
                    Valuation = totalPrice,
                    TotalQuotedAmount = finalPrice,
                    DiscountPercentage = discountPercentage
                };
            })
            .ToList();

        return new DashboardData
        {
            TotalQuotations = totalQuotations,
            TotalOrganizations = totalOrganizations,
            TotalModules = totalModules,
            TotalQuotedAmount = totalQuotedAmount,
            MonthlyQuotes = monthlyQuotes,
            StatusBreakdown = statusBreakdown,
            ModuleDistribution = moduleDistribution,
            TopOrganizations = topOrganizations,
            RecentQuotations = recentQuotations
        };
    }

    private async Task ValidateModulesAsync(List<string> selectedModules)
    {
        var master = await _moduleService.GetModulesAsync();
        var validNames = new HashSet<string>(master.Select(m => m.Module), StringComparer.OrdinalIgnoreCase);

        var unknown = selectedModules.Where(m => !validNames.Contains(m)).ToList();
        if (unknown.Count > 0)
            throw new ArgumentException($"Unknown module(s): {string.Join(", ", unknown)}");
    }

    private async Task SaveToDatabaseAsync(QuotationResult result, QuotationRequest request)
    {
        var quotation = new QuotationEntity
        {
            Id = result.QuotationId,
            OrganizationName = request.OrganizationName,
            ValidationDate = request.ValidationDate,
            QuotationNo = request.QuotationNo,
            Date = request.Date,
            ReferenceBy = request.ReferenceBy,
            QuotationToName = request.QuotationTo.Name,
            QuotationToAddress = request.QuotationTo.Address,
            QuotationToContactNo = request.QuotationTo.ContactNo,
            QuotationToEmail = request.QuotationTo.Email,
            GeneratedAt = result.GeneratedAt,
            DiscountPercentage = request.DiscountPercentage > 0 ? request.DiscountPercentage : (decimal?)null,
            QuotationModules = request.SelectedModules.Select(m => new QuotationModuleEntity
            {
                QuotationId = result.QuotationId,
                ModuleName = m
            }).ToList()
        };

        _dbContext.Quotations.Add(quotation);
        await _dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// Updates the discount percentage for an existing quotation and regenerates the Word/PDF documents.
    /// </summary>
    public async Task<QuotationResult?> UpdateDiscountAsync(string quotationId, decimal discountPercentage)
    {
        var quotation = await _dbContext.Quotations
            .Include(q => q.QuotationModules)
            .FirstOrDefaultAsync(q => q.Id == quotationId);

        if (quotation == null)
            return null;

        // Update discount percentage
        quotation.DiscountPercentage = discountPercentage > 0 ? discountPercentage : (decimal?)null;

        // Build request from stored data
        var request = new QuotationRequest
        {
            ValidationDate = quotation.ValidationDate,
            OrganizationName = quotation.OrganizationName,
            ReferenceBy = quotation.ReferenceBy ?? string.Empty,
            QuotationNo = quotation.QuotationNo ?? string.Empty,
            Date = quotation.Date ?? DateTime.UtcNow,
            SelectedModules = quotation.QuotationModules.Select(m => m.ModuleName).ToList(),
            QuotationTo = new QuotationToInfo
            {
                Name = quotation.QuotationToName,
                Address = quotation.QuotationToAddress,
                ContactNo = quotation.QuotationToContactNo,
                Email = quotation.QuotationToEmail
            },
            DiscountPercentage = discountPercentage
        };

        // Regenerate documents with new discount
        var docxPath = await _wordGenerator.GenerateAsync(request, quotationId);
        await _pdfConverter.ConvertToPdfAsync(docxPath);

        await _dbContext.SaveChangesAsync();

        return new QuotationResult
        {
            QuotationId = quotationId,
            OrganizationName = quotation.OrganizationName,
            QuotationNo = quotation.QuotationNo ?? string.Empty,
            Date = quotation.Date ?? DateTime.UtcNow,
            GeneratedAt = quotation.GeneratedAt,
            WordDownloadUrl = $"/api/quotation/{quotationId}/download/word",
            PdfDownloadUrl = $"/api/quotation/{quotationId}/download/pdf"
        };
    }
}