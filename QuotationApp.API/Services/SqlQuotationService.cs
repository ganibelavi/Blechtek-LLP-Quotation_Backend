using System.IO;
using System.Linq;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
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
    private static readonly HashSet<string> AllowedEffortUnits = new(StringComparer.OrdinalIgnoreCase)
    {
        "1 Man Month",
        "0.5 Man Month",
        "2 Man Month",
        "1 Day",
        "2 Days",
        "1 Week"
    };

    private readonly IPdfConverterService _pdfConverter;
    private readonly IModuleService _moduleService;
    private readonly QuotationDbContext _dbContext;
    private readonly string _outputFolder;
    private readonly QuotationSettings _settings;
    private readonly string _templatePath;

    public SqlQuotationService(
        IPdfConverterService pdfConverter,
        IModuleService moduleService,
        QuotationDbContext dbContext,
        IOptions<QuotationSettings> settings,
        IWebHostEnvironment env)
    {
        _pdfConverter = pdfConverter;
        _moduleService = moduleService;
        _dbContext = dbContext;
        _settings = settings.Value;
        _outputFolder = Path.Combine(env.ContentRootPath, _settings.OutputFolder);
        _templatePath = Path.Combine(env.ContentRootPath, "Templates", "QuotationTemplate_Updated.docx");
        Directory.CreateDirectory(_outputFolder);
    }

    public async Task<QuotationResult> GenerateQuotationAsync(QuotationRequest request)
    {
        await ValidateModulesAsync(request.SelectedModules);
        ValidateModuleDetails(request);

        var quotationId = $"Q-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8]}";

        // Auto-generate QuotationNo if not provided
        var quotationNo = string.IsNullOrWhiteSpace(request.QuotationNo)
            ? await GenerateNextQuotationNoAsync()
            : request.QuotationNo;

        // Update request with the generated quotationNo so it appears in the document
        request.QuotationNo = quotationNo;

        var docxPath = await GenerateWordDocumentAsync(request, quotationId);
        await _pdfConverter.ConvertToPdfAsync(docxPath);

        var result = new QuotationResult
        {
            QuotationId = quotationId,
            OrganizationName = request.OrganizationName,
            QuotationNo = quotationNo,
            Date = request.Date,
            GeneratedAt = DateTime.UtcNow,
            WordDownloadUrl = $"/api/quotation/{quotationId}/download/word",
            PdfDownloadUrl = $"/api/quotation/{quotationId}/download/pdf"
        };

        await SaveToDatabaseAsync(result, request, quotationNo);
        return result;
    }

    private async Task<string> GenerateNextQuotationNoAsync()
    {
        var prefix = _settings.QuotationNoPrefix;
        var currentDate = DateTime.UtcNow.AddHours(5.5);
        var financialYear = $"FY{currentDate.Year}-{(currentDate.Year + 1) % 100:00}";
        var sequencePrefix = _settings.SequencePrefix;
        var sequenceDigits = _settings.SequenceDigits;

        // Find the maximum sequence number for the current financial year and prefix
        var pattern = $"{prefix}/{financialYear}/{sequencePrefix}-%";
        var maxSequence = await _dbContext.Quotations
            .Where(q => q.QuotationNo != null && q.QuotationNo.StartsWith($"{prefix}/{financialYear}/{sequencePrefix}-"))
            .Select(q => q.QuotationNo!)
            .ToListAsync();

        int nextSequence = 1;
        if (maxSequence.Count > 0)
        {
            var sequences = maxSequence
                .Select(q =>
                {
                    var parts = q.Split('-');
                    if (parts.Length >= 2 && int.TryParse(parts[^1], out var seq))
                        return seq;
                    return 0;
                })
                .Where(s => s > 0)
                .ToList();

            if (sequences.Count > 0)
                nextSequence = sequences.Max() + 1;
        }

        var sequenceStr = nextSequence.ToString($"D{sequenceDigits}");
        return $"{prefix}/{financialYear}/{sequencePrefix}-{sequenceStr}";
    }

    public async Task<string> GetNextQuotationNoAsync()
    {
        return await GenerateNextQuotationNoAsync();
    }

    public async Task<List<QuotationRevisionEntry>> GetRevisionsAsync(string quotationId)
    {
        var quotation = await _dbContext.Quotations
            .AsNoTracking()
            .Include(q => q.QuotationModules)
            .FirstOrDefaultAsync(q => q.Id == quotationId);
        if (quotation is null) return new List<QuotationRevisionEntry>();

        var modulesJson = SerializeModules(quotation.QuotationModules.Select(m => m.ModuleName));
        var history = await _dbContext.QuotationHistory
            .AsNoTracking()
            .Where(h => h.OrganizationName == quotation.OrganizationName && h.ModulesJson == modulesJson)
            .OrderByDescending(h => h.ChangedAt)
            .ToListAsync();

        return history.Select(h => new QuotationRevisionEntry
        {
            Id = h.Id,
            QuotationId = h.QuotationId,
            OrganizationName = h.OrganizationName,
            QuotationNo = h.QuotationNo ?? string.Empty,
            Date = h.Date,
            ValidationDate = h.ValidationDate,
            ReferenceBy = h.ReferenceBy ?? string.Empty,
            QuotationToName = h.QuotationToName,
            QuotationToAddress = h.QuotationToAddress,
            QuotationToContactNo = h.QuotationToContactNo,
            QuotationToEmail = h.QuotationToEmail,
            Modules = JsonSerializer.Deserialize<List<string>>(h.ModulesJson) ?? new List<string>(),
            DiscountPercentage = h.DiscountPercentage,
            ChangedAt = h.ChangedAt,
            ChangeType = h.ChangeType
        })
            .ToList();
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
        var quotations = await _dbContext.Quotations
            .AsNoTracking()
            .Include(q => q.QuotationModules)
            .OrderByDescending(q => q.GeneratedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var modulePrices = await _dbContext.Modules
            .AsNoTracking()
            .ToDictionaryAsync(m => m.ModuleName, m => m.Price ?? 0m, StringComparer.OrdinalIgnoreCase);

        return quotations.Select(q =>
        {
            var moduleNames = q.QuotationModules
                .Select(m => m.ModuleName)
                .OrderBy(m => m, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new QuotationHistoryEntry
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
                Modules = moduleNames,
                ModuleDetails = moduleNames
                    .Select(moduleName => new QuotationModuleDetail
                    {
                        ModuleName = moduleName,
                        Price = modulePrices.GetValueOrDefault(moduleName, 0m),
                        NoOfUsers = q.QuotationModules
                            .First(m => m.ModuleName == moduleName).NoOfUsers,
                        NoOfInstallations = q.QuotationModules
                            .First(m => m.ModuleName == moduleName).NoOfInstallations,
                        NoOfSites = q.QuotationModules
                            .First(m => m.ModuleName == moduleName).NoOfSites,
                        ImplementationEffortUnit = q.QuotationModules
                            .First(m => m.ModuleName == moduleName).ImplementationEffortUnit
                    })
                    .ToList(),
                GeneratedAt = q.GeneratedAt,
                DiscountPercentage = q.DiscountPercentage
            };
        }).ToList();
    }

    /// <summary>
    /// Gets a single quotation by ID.
    /// </summary>
    public async Task<QuotationHistoryEntry?> GetQuotationAsync(string quotationId)
    {
        var quotation = await _dbContext.Quotations
            .AsNoTracking()
            .Include(q => q.QuotationModules)
            .FirstOrDefaultAsync(q => q.Id == quotationId);

        if (quotation is null)
            return null;

        var modulePrices = await _dbContext.Modules
            .AsNoTracking()
            .ToDictionaryAsync(m => m.ModuleName, m => m.Price ?? 0m, StringComparer.OrdinalIgnoreCase);

        var moduleNames = quotation.QuotationModules
            .Select(m => m.ModuleName)
            .OrderBy(m => m, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new QuotationHistoryEntry
        {
            QuotationId = quotation.Id,
            OrganizationName = quotation.OrganizationName,
            QuotationNo = quotation.QuotationNo ?? string.Empty,
            Date = quotation.Date ?? DateTime.MinValue,
            ValidationDate = quotation.ValidationDate,
            QuotationToName = quotation.QuotationToName,
            QuotationToAddress = quotation.QuotationToAddress,
            QuotationToContactNo = quotation.QuotationToContactNo,
            QuotationToEmail = quotation.QuotationToEmail,
            ReferenceBy = quotation.ReferenceBy ?? string.Empty,
            Modules = moduleNames,
            ModuleDetails = moduleNames
                .Select(moduleName => new QuotationModuleDetail
                {
                    ModuleName = moduleName,
                    Price = modulePrices.GetValueOrDefault(moduleName, 0m),
                    NoOfUsers = quotation.QuotationModules
                        .First(m => m.ModuleName == moduleName).NoOfUsers,
                    NoOfInstallations = quotation.QuotationModules
                        .First(m => m.ModuleName == moduleName).NoOfInstallations,
                    NoOfSites = quotation.QuotationModules
                        .First(m => m.ModuleName == moduleName).NoOfSites,
                    ImplementationEffortUnit = quotation.QuotationModules
                        .First(m => m.ModuleName == moduleName).ImplementationEffortUnit
                })
                .ToList(),
            GeneratedAt = quotation.GeneratedAt,
            DiscountPercentage = quotation.DiscountPercentage
        };
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
            .Select(g =>
            {
                var monthDate = new DateTime(g.Key.Year, g.Key.Month, 1);
                var monthQuotes = g.ToList();
                var totalPrice = monthQuotes.Sum(q =>
                    q.QuotationModules.Sum(m => modulePrices.GetValueOrDefault(m.ModuleName, 0)));
                var totalDiscount = monthQuotes.Sum(q =>
                {
                    var price = q.QuotationModules.Sum(m => modulePrices.GetValueOrDefault(m.ModuleName, 0));
                    var discountPct = q.DiscountPercentage ?? 0;
                    return price * discountPct / 100;
                });
                var revenue = totalPrice - totalDiscount;
                return new
                {
                    SortKey = monthDate,
                    Month = monthDate.ToString("MMM yyyy", System.Globalization.CultureInfo.InvariantCulture),
                    Count = g.Count(),
                    Revenue = revenue
                };
            })
            .OrderBy(m => m.SortKey)
            .Select(m => new MonthlyQuoteData
            {
                Month = m.Month,
                Count = m.Count,
                Revenue = m.Revenue
            })
            .ToList();

        var userQuotationStats = allQuotations
            .GroupBy(q => string.IsNullOrWhiteSpace(q.CreatedByUser)
                ? (string.IsNullOrWhiteSpace(q.ReferenceBy) ? "Unknown" : q.ReferenceBy.Trim())
                : q.CreatedByUser.Trim())
            .Select(g => new UserQuotationStatsData
            {
                User = g.Key,
                QuoteCount = g.Count()
            })
            .OrderByDescending(u => u.QuoteCount)
            .Take(5)
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
            .Take(5)
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

        // Machine utilization fallback: derive from the most-used modules so the chart
        // remains populated even when no dedicated machine dataset exists.
        var maxModuleCount = moduleDistribution.Any() ? moduleDistribution.Max(m => m.Count) : 0;
        var machineUtilization = moduleDistribution
            .Select(m => new MachineUtilizationData
            {
                Machine = m.Module,
                Utilization = maxModuleCount > 0 ? (int)Math.Round((m.Count * 100m) / maxModuleCount) : 0
            })
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
            UserQuotationStats = userQuotationStats,
            MonthlyQuotes = monthlyQuotes,
            StatusBreakdown = statusBreakdown,
            ModuleDistribution = moduleDistribution,
            TopOrganizations = topOrganizations,
            RecentQuotations = recentQuotations,
            MachineUtilization = machineUtilization
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

    private async Task SaveToDatabaseAsync(QuotationResult result, QuotationRequest request, string quotationNo)
    {
        var detailsByModule = request.ModuleDetails
            .ToDictionary(d => d.ModuleName.Trim(), StringComparer.OrdinalIgnoreCase);

        var quotation = new QuotationEntity
        {
            Id = result.QuotationId,
            OrganizationName = request.OrganizationName,
            ValidationDate = request.ValidationDate,
            QuotationNo = quotationNo,
            Date = request.Date,
            ReferenceBy = request.ReferenceBy,
            CreatedByUser = string.IsNullOrWhiteSpace(request.CreatedByUser) ? request.ReferenceBy : request.CreatedByUser.Trim(),
            QuotationToName = request.QuotationTo.Name,
            QuotationToAddress = request.QuotationTo.Address,
            QuotationToContactNo = request.QuotationTo.ContactNo,
            QuotationToEmail = request.QuotationTo.Email,
            GeneratedAt = result.GeneratedAt,
            DiscountPercentage = request.DiscountPercentage > 0 ? request.DiscountPercentage : (decimal?)null,
            QuotationModules = request.SelectedModules.Select(m => new QuotationModuleEntity
            {
                QuotationId = result.QuotationId,
                ModuleName = m,
                NoOfUsers = detailsByModule.TryGetValue(m, out var detail) ? detail.NoOfUsers : null,
                NoOfInstallations = detailsByModule.TryGetValue(m, out detail) ? detail.NoOfInstallations : null,
                NoOfSites = detailsByModule.TryGetValue(m, out detail) ? detail.NoOfSites : null,
                ImplementationEffortUnit = detailsByModule.TryGetValue(m, out detail)
                    ? detail.ImplementationEffortUnit
                    : null
            }).ToList()
        };

        _dbContext.Quotations.Add(quotation);
        AddHistorySnapshot(quotation, "Created");
        await _dbContext.SaveChangesAsync();
    }

    private static string SerializeModules(IEnumerable<string> modules) =>
        JsonSerializer.Serialize(modules.OrderBy(m => m, StringComparer.OrdinalIgnoreCase).ToList());

    private static void ValidateModuleDetails(QuotationRequest request)
    {
        var selected = new HashSet<string>(
            request.SelectedModules.Select(m => m.Trim()),
            StringComparer.OrdinalIgnoreCase);

        var details = request.ModuleDetails ?? new List<QuotationModuleRequest>();
        var duplicate = details
            .GroupBy(d => d.ModuleName.Trim(), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicate is not null)
            throw new ArgumentException($"Module details contain duplicate module '{duplicate.Key}'.");

        var unknown = details
            .Where(d => !selected.Contains(d.ModuleName.Trim()))
            .Select(d => d.ModuleName)
            .ToList();

        if (unknown.Count > 0)
            throw new ArgumentException($"Module details contain unselected module(s): {string.Join(", ", unknown)}.");

        foreach (var detail in details)
        {
            if (!string.IsNullOrWhiteSpace(detail.ImplementationEffortUnit) &&
                !AllowedEffortUnits.Contains(detail.ImplementationEffortUnit.Trim()))
            {
                throw new ArgumentException(
                    $"Invalid implementation effort for '{detail.ModuleName}'.");
            }
        }
    }

    private void AddHistorySnapshot(QuotationEntity quotation, string changeType)
    {
        _dbContext.QuotationHistory.Add(new QuotationHistoryEntity
        {
            QuotationId = quotation.Id,
            OrganizationName = quotation.OrganizationName,
            QuotationNo = quotation.QuotationNo,
            Date = quotation.Date,
            ValidationDate = quotation.ValidationDate,
            ReferenceBy = quotation.ReferenceBy,
            QuotationToName = quotation.QuotationToName,
            QuotationToAddress = quotation.QuotationToAddress,
            QuotationToContactNo = quotation.QuotationToContactNo,
            QuotationToEmail = quotation.QuotationToEmail,
            ModulesJson = SerializeModules(quotation.QuotationModules.Select(m => m.ModuleName)),
            DiscountPercentage = quotation.DiscountPercentage,
            ChangedAt = DateTime.UtcNow,
            ChangeType = changeType
        });
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
        AddHistorySnapshot(quotation, "DiscountUpdated");

        // Build request from stored data
        var request = new QuotationRequest
        {
            ValidationDate = quotation.ValidationDate,
            OrganizationName = quotation.OrganizationName,
            ReferenceBy = quotation.ReferenceBy ?? string.Empty,
            QuotationNo = quotation.QuotationNo ?? string.Empty,
            Date = quotation.Date ?? DateTime.UtcNow,
            SelectedModules = quotation.QuotationModules.Select(m => m.ModuleName).ToList(),
            ModuleDetails = quotation.QuotationModules.Select(m => new QuotationModuleRequest
            {
                ModuleName = m.ModuleName,
                NoOfUsers = m.NoOfUsers,
                NoOfInstallations = m.NoOfInstallations,
                NoOfSites = m.NoOfSites,
                ImplementationEffortUnit = m.ImplementationEffortUnit
            }).ToList(),
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
        var docxPath = await GenerateWordDocumentAsync(request, quotationId);
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

    /// <summary>
    /// Updates quotation details (validation date, modules) and regenerates documents.
    /// </summary>
    public async Task<QuotationResult?> UpdateQuotationAsync(string quotationId, DateTime validationDate, List<string> selectedModules)
    {
        var quotation = await _dbContext.Quotations
            .Include(q => q.QuotationModules)
            .FirstOrDefaultAsync(q => q.Id == quotationId);

        if (quotation == null)
            return null;

        // Validate modules
        await ValidateModulesAsync(selectedModules);

        // Update validation date
        quotation.ValidationDate = validationDate;

        // Update modules - remove existing and add new
        _dbContext.QuotationModules.RemoveRange(quotation.QuotationModules);
        quotation.QuotationModules = selectedModules.Select(m => new QuotationModuleEntity
        {
            QuotationId = quotationId,
            ModuleName = m
        }).ToList();
        AddHistorySnapshot(quotation, "DetailsUpdated");

        // Build request from stored data with updated validation date and modules
        var request = new QuotationRequest
        {
            ValidationDate = validationDate,
            OrganizationName = quotation.OrganizationName,
            ReferenceBy = quotation.ReferenceBy ?? string.Empty,
            QuotationNo = quotation.QuotationNo ?? string.Empty,
            Date = quotation.Date ?? DateTime.UtcNow,
            SelectedModules = selectedModules,
            ModuleDetails = quotation.QuotationModules
                .Where(m => selectedModules.Contains(m.ModuleName, StringComparer.OrdinalIgnoreCase))
                .Select(m => new QuotationModuleRequest
                {
                    ModuleName = m.ModuleName,
                    NoOfUsers = m.NoOfUsers,
                    NoOfInstallations = m.NoOfInstallations,
                    NoOfSites = m.NoOfSites,
                    ImplementationEffortUnit = m.ImplementationEffortUnit
                }).ToList(),
            QuotationTo = new QuotationToInfo
            {
                Name = quotation.QuotationToName,
                Address = quotation.QuotationToAddress,
                ContactNo = quotation.QuotationToContactNo,
                Email = quotation.QuotationToEmail
            },
            DiscountPercentage = quotation.DiscountPercentage ?? 0
        };

        // Regenerate documents with updated data
        var docxPath = await GenerateWordDocumentAsync(request, quotationId);
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

    private async Task<string> GenerateWordDocumentAsync(QuotationRequest request, string quotationId)
    {
        var outputPath = Path.Combine(_outputFolder, $"{quotationId}.docx");

        // Copy template to output location
        if (!File.Exists(_templatePath))
        {
            throw new FileNotFoundException($"Template not found: {_templatePath}");
        }

        File.Copy(_templatePath, outputPath, true);

        // Open the document and replace placeholders
        using (var doc = WordprocessingDocument.Open(outputPath, true))
        {
            var body = doc.MainDocumentPart?.Document.Body;
            if (body != null)
            {
                var moduleService = _moduleService;
                var modules = await moduleService.GetModulesAsync();
                var modulePrices = modules.ToDictionary(m => m.Module, StringComparer.OrdinalIgnoreCase);

                var modulePriceTotal = request.SelectedModules.Sum(moduleName =>
                {
                    var module = modulePrices.GetValueOrDefault(moduleName);
                    return module?.Price ?? 0m;
                });
                var implementationPriceTotal = request.SelectedModules.Sum(moduleName =>
                {
                    var module = modulePrices.GetValueOrDefault(moduleName);
                    var detail = request.ModuleDetails
                        .FirstOrDefault(d => string.Equals(d.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase));
                    return (module?.ImplementationEffortCost ?? 0m) *
                        GetEffortMultiplier(detail?.ImplementationEffortUnit);
                });
                var subtotal = modulePriceTotal + implementationPriceTotal;
                var discountPercentage = request.DiscountPercentage > 0 ? request.DiscountPercentage : 0m;
                var discountAmount = subtotal * discountPercentage / 100m;
                var finalPrice = subtotal - discountAmount;
                var moduleParticularsText = FormatModuleParticulars(
                    request.SelectedModules,
                    request.ModuleDetails,
                    discountPercentage);
                var modulePricingValuesText = FormatModulePricingValues(
                    request.SelectedModules,
                    request.ModuleDetails,
                    modulePrices,
                    discountPercentage,
                    subtotal);
                var overallPricingParticularsText = FormatOverallPricingParticulars(
                    discountPercentage);
                var overallPricingValuesText = FormatOverallPricingValues(
                    modulePriceTotal,
                    implementationPriceTotal,
                    subtotal,
                    discountPercentage,
                    discountAmount,
                    finalPrice);
                var pricingParticularsText = string.Join(
                    Environment.NewLine,
                    "Product License - {{MODULE_LIST}} (single installation). Scope as listed above.",
                    moduleParticularsText,
                    string.Empty,
                    overallPricingParticularsText);
                var pricingValuesText = string.Join(
                    Environment.NewLine,
                    modulePricingValuesText,
                    string.Empty,
                    overallPricingValuesText);

                var replacements = new Dictionary<string, string>
                {
                    ["{{QuotationNo}}"] = request.QuotationNo ?? "",
                    ["{{Date}}"] = request.Date.ToString("dd/MM/yyyy"),
                    ["{{OrganizationName}}"] = request.OrganizationName ?? "",
                    ["{{ReferenceBy}}"] = request.ReferenceBy ?? "",
                    ["{{ValidationDate}}"] = request.ValidationDate.ToString("dd/MM/yyyy"),
                    ["{{QuotationTo.Name}}"] = request.QuotationTo?.Name ?? "",
                    ["{{QuotationTo.Address}}"] = request.QuotationTo?.Address ?? "",
                    ["{{QuotationTo.ContactNo}}"] = request.QuotationTo?.ContactNo ?? "",
                    ["{{QuotationTo.Email}}"] = request.QuotationTo?.Email ?? "",
                    ["{{SelectedModules}}"] = string.Join(", ", request.SelectedModules),
                    ["{{MODULE_LIST}}"] = string.Join(", ", request.SelectedModules),
                    ["{{MODULE_REQUIREMENTS}}"] = FormatModuleRequirements(
                        request.SelectedModules,
                        request.ModuleDetails),
                    ["{{MODULE_DETAILS}}"] = string.Empty,
                    ["{{MODULE_PRICING}}"] = string.Empty,
                    ["{{OVERALL_PRICING}}"] = string.Empty,
                    // Template placeholders (from temp_template)
                    ["{{CONTACT_NAME}}"] = request.QuotationTo?.Name ?? "",
                    ["{{CONTACT_ADDRESS}}"] = request.QuotationTo?.Address ?? "",
                    ["{{CONTACT_PHONE}}"] = request.QuotationTo?.ContactNo ?? "",
                    ["{{CONTACT_EMAIL}}"] = request.QuotationTo?.Email ?? "",
                    ["{{ORG_NAME}}"] = request.OrganizationName ?? "",
                    ["{{REQUIRED}}"] = string.Join(", ", request.SelectedModules),
                    ["{{VALIDATION_DATE}}"] = request.ValidationDate.ToString("dd/MM/yyyy"),
                    ["{{TotalPrice}}"] = $"Total Price: {subtotal:N2}",
                    ["{{MODULE_PRICE}}"] = $"Module Price: {modulePriceTotal:N2}",
                    ["{{IMPLEMENTATION_TOTAL}}"] = $"Implementation Total: {implementationPriceTotal:N2}",
                    ["{{SUBTOTAL}}"] = $"Subtotal: {subtotal:N2}",
                    ["{{DiscountPercentage}}"] = $"Discount Percentage: {discountPercentage:N2}%",
                    ["{{DiscountAmount}}"] = $"Discount Amount: {discountAmount:N2}",
                    ["{{FinalPrice}}"] = $"Final Price: {finalPrice:N2}",
                    ["{{IMPLEMENTATION_PRICE}}"] = $"Implementation Price: {implementationPriceTotal:N2}"
                };

                PopulateScopeTable(body, modules, request.SelectedModules);
                PopulatePricingTable(
                    body,
                    pricingParticularsText,
                    pricingValuesText);
                NormalizeStandardPricingRows(body);

                foreach (var paragraph in body.Descendants<Paragraph>())
                {
                    ReplaceParagraphText(paragraph, replacements);

                    if (string.Equals(
                            paragraph.InnerText.Trim(),
                            "QUOTATION TO",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        ReplaceParagraphText(
                            paragraph,
                            new Dictionary<string, string>
                            {
                                ["QUOTATION TO"] = $"QUOTATION TO - {request.OrganizationName}"
                            });
                    }

                }
            }
        }

        return outputPath;
    }

    private static decimal GetEffortMultiplier(string? effortUnit)
    {
        return effortUnit?.Trim() switch
        {
            // ImplementationEffortCost is a per-man-day rate.
            "1 Man Month" => 30m,
            "0.5 Man Month" => 15m,
            "2 Man Month" => 60m,
            "1 Day" => 1m,
            "2 Days" => 2m,
            "1 Week" => 7m,
            _ => 0m
        };
    }

    private static string FormatModuleRequirements(
        IEnumerable<string> selectedModules,
        IEnumerable<QuotationModuleRequest>? moduleDetails)
    {
        var detailsByModule = (moduleDetails ?? Enumerable.Empty<QuotationModuleRequest>())
            .ToDictionary(
                detail => detail.ModuleName.Trim(),
                StringComparer.OrdinalIgnoreCase);

        return string.Join(
            Environment.NewLine + Environment.NewLine,
            selectedModules.Select(moduleName =>
            {
                detailsByModule.TryGetValue(moduleName.Trim(), out var detail);
                return string.Join(
                    Environment.NewLine,
                    $"{moduleName}:",
                    $"No. of Users: {detail?.NoOfUsers?.ToString() ?? "—"}",
                    $"No. of Installations: {detail?.NoOfInstallations?.ToString() ?? "—"}",
                    $"No. of Sites: {detail?.NoOfSites?.ToString() ?? "—"}");
            }));
    }

    private static string FormatModuleDetails(
        IEnumerable<string> selectedModules,
        IEnumerable<QuotationModuleRequest>? moduleDetails)
    {
        var detailsByModule = (moduleDetails ?? Enumerable.Empty<QuotationModuleRequest>())
            .ToDictionary(
                detail => detail.ModuleName.Trim(),
                StringComparer.OrdinalIgnoreCase);

        return string.Join(
            Environment.NewLine + Environment.NewLine,
            selectedModules.Select(moduleName =>
            {
                detailsByModule.TryGetValue(moduleName.Trim(), out var detail);
                return string.Join(
                    Environment.NewLine,
                    $"{moduleName}:",
                    $"No. of Users: {detail?.NoOfUsers?.ToString() ?? "—"}",
                    $"No. of Installations: {detail?.NoOfInstallations?.ToString() ?? "—"}",
                    $"No. of Sites: {detail?.NoOfSites?.ToString() ?? "—"}",
                    $"Implementation Effort: {detail?.ImplementationEffortUnit ?? "—"}");
            }));
    }

    private static string FormatModuleParticulars(
        IEnumerable<string> selectedModules,
        IEnumerable<QuotationModuleRequest>? moduleDetails,
        decimal discountPercentage)
    {
        var detailsByModule = (moduleDetails ?? Enumerable.Empty<QuotationModuleRequest>())
            .ToDictionary(
                detail => detail.ModuleName.Trim(),
                StringComparer.OrdinalIgnoreCase);

        return string.Join(
            Environment.NewLine + Environment.NewLine,
            selectedModules.Select(moduleName =>
            {
                detailsByModule.TryGetValue(moduleName.Trim(), out var detail);
                return string.Join(
                    Environment.NewLine,
                    $"{moduleName}:",
                    $"No. of Users: {detail?.NoOfUsers?.ToString() ?? "—"}",
                    $"No. of Installations: {detail?.NoOfInstallations?.ToString() ?? "—"}",
                    $"No. of Sites: {detail?.NoOfSites?.ToString() ?? "—"}",
                    $"Implementation Effort: {detail?.ImplementationEffortUnit ?? "—"}",
                    "Module Price:",
                    "Implementation Total:",
                    "Module Subtotal:",
                    $"Discount ({discountPercentage:N2}%):",
                    "Module Final Price:");
            }));
    }

    private static string FormatModulePricingValues(
        IEnumerable<string> selectedModules,
        IEnumerable<QuotationModuleRequest>? moduleDetails,
        IReadOnlyDictionary<string, ModuleItem> modulePrices,
        decimal discountPercentage,
        decimal quotationSubtotal)
    {
        var detailsByModule = (moduleDetails ?? Enumerable.Empty<QuotationModuleRequest>())
            .ToDictionary(
                detail => detail.ModuleName.Trim(),
                StringComparer.OrdinalIgnoreCase);

        var lines = new List<string>
        {
            string.Empty
        };

        var moduleNames = selectedModules.ToList();
        for (var index = 0; index < moduleNames.Count; index++)
        {
            var moduleName = moduleNames[index];
            {
                modulePrices.TryGetValue(moduleName, out var module);
                detailsByModule.TryGetValue(moduleName.Trim(), out var detail);

                var modulePrice = module?.Price ?? 0m;
                var implementationTotal = (module?.ImplementationEffortCost ?? 0m) *
                    GetEffortMultiplier(detail?.ImplementationEffortUnit);
                var moduleSubtotal = modulePrice + implementationTotal;
                var moduleDiscount = quotationSubtotal == 0m
                    ? 0m
                    : moduleSubtotal * discountPercentage / 100m;
                var moduleFinalPrice = moduleSubtotal - moduleDiscount;

                lines.AddRange(Enumerable.Repeat(string.Empty, 5));
                lines.Add($"{modulePrice:N2}");
                lines.Add($"{implementationTotal:N2}");
                lines.Add($"{moduleSubtotal:N2}");
                lines.Add($"{moduleDiscount:N2}");
                lines.Add($"{moduleFinalPrice:N2}");
                if (index < moduleNames.Count - 1)
                {
                    lines.Add(string.Empty);
                }
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatOverallPricingParticulars(decimal discountPercentage)
    {
        return string.Join(
            Environment.NewLine,
            "Overall Calculation:",
            "Module Price:",
            "Implementation Total:",
            "Subtotal:",
            $"Discount ({discountPercentage:N2}%):",
            "Final Price:");
    }

    private static string FormatOverallPricingValues(
        decimal modulePriceTotal,
        decimal implementationPriceTotal,
        decimal subtotal,
        decimal discountPercentage,
        decimal discountAmount,
        decimal finalPrice)
    {
        return string.Join(
            Environment.NewLine,
            "",
            $"{modulePriceTotal:N2}",
            $"{implementationPriceTotal:N2}",
            $"{subtotal:N2}",
            $"{discountAmount:N2}",
            $"{finalPrice:N2}");
    }

    private static void PopulateScopeTable(
        Body body,
        IReadOnlyCollection<ModuleItem> modules,
        IEnumerable<string> selectedModuleNames)
    {
        var selectedModules = selectedModuleNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var scopeRow = body
            .Descendants<TableRow>()
            .FirstOrDefault(row =>
            {
                var rowText = string.Concat(row.Descendants<Text>().Select(text => text.Text));
                return rowText.Contains("{{PILLAR}}", StringComparison.Ordinal) &&
                       rowText.Contains("{{MODULE}}", StringComparison.Ordinal) &&
                       rowText.Contains("{{SELECTED}}", StringComparison.Ordinal);
            });

        if (scopeRow is null) return;

        foreach (var module in modules.Where(module => selectedModules.Contains(module.Module)))
        {
            var row = (TableRow)scopeRow.CloneNode(true);
            var rowReplacements = new Dictionary<string, string>
            {
                ["{{PILLAR}}"] = module.Pillar,
                ["{{MODULE}}"] = module.Module,
                ["{{SELECTED}}"] = "Yes"
            };
            foreach (var paragraph in row.Descendants<Paragraph>())
            {
                ReplaceParagraphText(paragraph, rowReplacements);
            }
            scopeRow.InsertBeforeSelf(row);
        }

        scopeRow.Remove();
    }

    private static void PopulatePricingTable(
        Body body,
        string particularsText,
        string valuesText)
    {
        var pricingRow = body
            .Descendants<TableRow>()
            .FirstOrDefault(row =>
            {
                var rowText = string.Concat(row.Descendants<Text>().Select(text => text.Text));
                return rowText.Contains("{{MODULE_PRICING}}", StringComparison.Ordinal) &&
                       rowText.Contains("{{OVERALL_PRICING}}", StringComparison.Ordinal);
            });

        if (pricingRow is null) return;

        var particularsLines = SplitTextLines(particularsText);
        var valueLines = SplitTextLines(valuesText);
        var lineCount = Math.Max(particularsLines.Count, valueLines.Count);

        for (var index = 0; index < lineCount; index++)
        {
            var row = (TableRow)pricingRow.CloneNode(true);
            var cells = row.Elements<TableCell>().ToList();
            if (cells.Count < 3) continue;

            ReplaceTableCellText(
                cells[0],
                index == 0 ? "1" : string.Empty,
                false);
            var particularsLine = index < particularsLines.Count
                ? particularsLines[index]
                : string.Empty;
            var priceLine = index < valueLines.Count
                ? valueLines[index]
                : string.Empty;
            ReplaceTableCellText(
                cells[1],
                particularsLine,
                IsBoldParticularsLine(particularsLines, index));
            ReplaceTableCellText(
                cells[2],
                priceLine,
                IsBoldPriceLine(particularsLine));

            pricingRow.InsertBeforeSelf(row);
        }

        pricingRow.Remove();
    }

    private static List<string> SplitTextLines(string text)
    {
        return text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .ToList();
    }

    private static bool IsBoldParticularsLine(
        IReadOnlyList<string> particularsLines,
        int index)
    {
        var line = particularsLines[index].Trim();
        var isModuleName = line.EndsWith(":", StringComparison.Ordinal) &&
            index + 1 < particularsLines.Count &&
            particularsLines[index + 1].Trim().StartsWith(
                "No. of Users:",
                StringComparison.OrdinalIgnoreCase);

        return isModuleName ||
            line.Equals("Overall Calculation:", StringComparison.OrdinalIgnoreCase) ||
            line.Equals("Final Price:", StringComparison.OrdinalIgnoreCase) ||
            line.Equals("Module Final Price:", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBoldPriceLine(string particularsLine)
    {
        var line = particularsLine.Trim();
        return line.Equals("Module Final Price:", StringComparison.OrdinalIgnoreCase) ||
            line.Equals("Final Price:", StringComparison.OrdinalIgnoreCase);
    }

    private static void ReplaceTableCellText(
        TableCell cell,
        string value,
        bool isBold)
    {
        cell.RemoveAllChildren<Paragraph>();
        var run = new Run(new Text(value ?? string.Empty));
        if (isBold)
        {
            run.RunProperties = new RunProperties(new Bold());
        }

        cell.AppendChild(
            new Paragraph(run));
    }

    private static void NormalizeStandardPricingRows(Body body)
    {
        var pricingTable = body
            .Descendants<Table>()
            .FirstOrDefault(table =>
                table.Descendants<Text>().Any(text =>
                    text.Text.Trim().Equals("Price (INR)", StringComparison.OrdinalIgnoreCase)));

        if (pricingTable is null) return;

        foreach (var row in pricingTable.Elements<TableRow>())
        {
            var rowText = string.Concat(row.Descendants<Text>().Select(text => text.Text));
            var rowNumber = row.Elements<TableCell>()
                .FirstOrDefault()?
                .InnerText
                .Trim();

            var isStandardPricingRow = rowNumber is "2" or "3" or "4" or "5" or "6";
            var isSupportLevelRow = rowText.Contains(
                "Support Level",
                StringComparison.OrdinalIgnoreCase);
            if (!isStandardPricingRow && !isSupportLevelRow) continue;

            foreach (var runProperties in row.Descendants<RunProperties>())
            {
                runProperties.Bold = null;
                runProperties.BoldComplexScript = null;
            }
        }
    }

    private static void ReplaceParagraphText(
        Paragraph paragraph,
        IReadOnlyDictionary<string, string> replacements)
    {
        var textGroup = new List<Text>();

        void ReplaceCurrentTextGroup()
        {
            if (textGroup.Count == 0) return;

            var originalText = string.Concat(textGroup.Select(text => text.Text));
            var replacedText = originalText;
            foreach (var replacement in replacements)
            {
                replacedText = replacedText.Replace(
                    replacement.Key,
                    replacement.Value,
                    StringComparison.Ordinal);
            }

            if (!string.Equals(originalText, replacedText, StringComparison.Ordinal))
            {
                textGroup[0].Text = replacedText;
                foreach (var textNode in textGroup.Skip(1))
                {
                    textNode.Text = string.Empty;
                }
            }

            textGroup.Clear();
        }

        foreach (var run in paragraph.Elements<Run>())
        {
            foreach (var element in run.Elements())
            {
                if (element is Text text)
                {
                    textGroup.Add(text);
                }
                else if (element.LocalName == "tab")
                {
                    // Do not move text across tabs in the metadata layout.
                    ReplaceCurrentTextGroup();
                }
            }
        }

        ReplaceCurrentTextGroup();
    }
}