using System.IO;
using System.Linq;
using System.Globalization;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuotationApp.API.Data;
using QuotationApp.API.Models;

namespace QuotationApp.API.Services;

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
        await PopulateModuleTimelineDeliverySnapshotsAsync(request);
        await ValidateAdditionalScopesAsync(request.AdditionalScopes);

        request.TimeEstimate = TimeEstimateHelper.PrepareForModules(
            request.TimeEstimate,
            request.SelectedModules);

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
            .Include(q => q.AdditionalScopes)
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

    public async Task<List<QuotationHistoryEntry>> GetHistoryAsync(int page = 1, int pageSize = 20)
    {
        var quotations = await _dbContext.Quotations
            .AsNoTracking()
            .Include(q => q.QuotationModules)
            .Include(q => q.AdditionalScopes)
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

            var totalSubtotal = q.QuotationModules.Sum(m => m.ModuleSubtotal ?? 0m);
            var totalDiscountAmount = q.QuotationModules.Sum(m => m.DiscountAmount ?? 0m);
            var overallDiscountPercentage = totalSubtotal > 0 ? (totalDiscountAmount / totalSubtotal) * 100m : 0m;

            return new QuotationHistoryEntry
            {
                QuotationId = q.Id,
                OrganizationName = q.OrganizationName,
                QuotationNo = q.QuotationNo ?? string.Empty,
                Date = q.Date ?? DateTime.MinValue,
                ValidationDate = q.ValidationDate,
                ExpectedStartDate = q.ExpectedStartDate,
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
                        Price = q.QuotationModules.First(m => m.ModuleName == moduleName).ModulePrice
                            ?? modulePrices.GetValueOrDefault(moduleName, 0m),
                        ModulePrice = q.QuotationModules.First(m => m.ModuleName == moduleName).ModulePrice,
                        ImplementationUnitPrice = q.QuotationModules.First(m => m.ModuleName == moduleName).ImplementationUnitPrice,
                        ImplementationMultiplier = q.QuotationModules.First(m => m.ModuleName == moduleName).ImplementationMultiplier,
                        ImplementationPrice = q.QuotationModules.First(m => m.ModuleName == moduleName).ImplementationPrice,
                        ModuleSubtotal = q.QuotationModules.First(m => m.ModuleName == moduleName).ModuleSubtotal,
                        DiscountPercentage = q.QuotationModules.First(m => m.ModuleName == moduleName).DiscountPercentage,
                        DiscountAmount = q.QuotationModules.First(m => m.ModuleName == moduleName).DiscountAmount,
                        FinalPrice = q.QuotationModules.First(m => m.ModuleName == moduleName).FinalPrice,
                        NoOfUsers = q.QuotationModules
                            .First(m => m.ModuleName == moduleName).NoOfUsers,
                        NoOfInstallations = q.QuotationModules
                            .First(m => m.ModuleName == moduleName).NoOfInstallations,
                        NoOfSites = q.QuotationModules
                            .First(m => m.ModuleName == moduleName).NoOfSites,
                        ImplementationEffortUnit = q.QuotationModules
                            .First(m => m.ModuleName == moduleName).ImplementationEffortUnit,
                        TimelineWeeks = q.QuotationModules
                            .First(m => m.ModuleName == moduleName).TimelineWeeks,
                        DeliveryDays = q.QuotationModules
                            .First(m => m.ModuleName == moduleName).DeliveryDays
                    })
                    .ToList(),
                AdditionalScopes = q.AdditionalScopes.ToList(),
                GeneratedAt = q.GeneratedAt,
                DiscountPercentage = overallDiscountPercentage
            };
        }).ToList();
    }

    public async Task<QuotationHistoryEntry?> GetQuotationAsync(string quotationId)
    {
        var quotation = await _dbContext.Quotations
            .AsNoTracking()
            .Include(q => q.QuotationModules)
            .Include(q => q.AdditionalScopes)
            .FirstOrDefaultAsync(q => q.Id == quotationId);

        if (quotation is null)
            return null;

        // Load time estimates separately to avoid FK dependency issues
        var timeEstimates = await _dbContext.QuotationTimeEstimates
            .AsNoTracking()
            .Where(t => t.QuotationId == quotationId)
            .ToListAsync();

        var modulePrices = await _dbContext.Modules
            .AsNoTracking()
            .ToDictionaryAsync(m => m.ModuleName, m => m.Price ?? 0m, StringComparer.OrdinalIgnoreCase);

        var moduleNames = quotation.QuotationModules
            .Select(m => m.ModuleName)
            .OrderBy(m => m, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var totalSubtotal = quotation.QuotationModules.Sum(m => m.ModuleSubtotal ?? 0m);
        var totalDiscountAmount = quotation.QuotationModules.Sum(m => m.DiscountAmount ?? 0m);

        return new QuotationHistoryEntry
        {
            QuotationId = quotation.Id,
            OrganizationName = quotation.OrganizationName,
            QuotationNo = quotation.QuotationNo ?? string.Empty,
            Date = quotation.Date ?? DateTime.MinValue,
            ValidationDate = quotation.ValidationDate,
            ExpectedStartDate = quotation.ExpectedStartDate,
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
                    Price = quotation.QuotationModules.First(m => m.ModuleName == moduleName).ModulePrice
                        ?? modulePrices.GetValueOrDefault(moduleName, 0m),
                    ModulePrice = quotation.QuotationModules.First(m => m.ModuleName == moduleName).ModulePrice,
                    ImplementationUnitPrice = quotation.QuotationModules.First(m => m.ModuleName == moduleName).ImplementationUnitPrice,
                    ImplementationMultiplier = quotation.QuotationModules.First(m => m.ModuleName == moduleName).ImplementationMultiplier,
                    ImplementationPrice = quotation.QuotationModules.First(m => m.ModuleName == moduleName).ImplementationPrice,
                    ModuleSubtotal = quotation.QuotationModules.First(m => m.ModuleName == moduleName).ModuleSubtotal,
                    DiscountPercentage = quotation.QuotationModules.First(m => m.ModuleName == moduleName).DiscountPercentage,
                    DiscountAmount = quotation.QuotationModules.First(m => m.ModuleName == moduleName).DiscountAmount,
                    FinalPrice = quotation.QuotationModules.First(m => m.ModuleName == moduleName).FinalPrice,
                    NoOfUsers = quotation.QuotationModules
                        .First(m => m.ModuleName == moduleName).NoOfUsers,
                    NoOfInstallations = quotation.QuotationModules
                        .First(m => m.ModuleName == moduleName).NoOfInstallations,
                    NoOfSites = quotation.QuotationModules
                        .First(m => m.ModuleName == moduleName).NoOfSites,
                    ImplementationEffortUnit = quotation.QuotationModules
                        .First(m => m.ModuleName == moduleName).ImplementationEffortUnit,
                    TimelineWeeks = quotation.QuotationModules
                        .First(m => m.ModuleName == moduleName).TimelineWeeks,
                    DeliveryDays = quotation.QuotationModules
                        .First(m => m.ModuleName == moduleName).DeliveryDays
                })
                .ToList(),
            AdditionalScopes = quotation.AdditionalScopes.ToList(),
            GeneratedAt = quotation.GeneratedAt,
            DiscountPercentage = totalSubtotal > 0 ? (totalDiscountAmount / totalSubtotal) * 100m : 0m,
            TimeEstimate = timeEstimates
                .Select(t => new TimeEstimateStageResponse
                {
                    ModuleName = t.ModuleName,
                    StageKey = t.StageKey,
                    StartWeek = t.StartWeek,
                    EndWeek = t.EndWeek
                })
                .ToList()
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
                    var totalSubtotal = q.QuotationModules.Sum(m => m.ModuleSubtotal ?? 0m);
                    var totalDiscountAmount = q.QuotationModules.Sum(m => m.DiscountAmount ?? 0m);
                    var discountPct = totalSubtotal > 0 ? (totalDiscountAmount / totalSubtotal) * 100m : 0m;
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

        // Calculate total quoted amount across all quotations
        var totalQuotedAmount = allQuotations.Sum(q =>
        {
            var totalPrice = q.QuotationModules.Sum(m => modulePrices.GetValueOrDefault(m.ModuleName, 0));
            var totalSubtotal = q.QuotationModules.Sum(m => m.ModuleSubtotal ?? 0m);
            var totalDiscountAmount = q.QuotationModules.Sum(m => m.DiscountAmount ?? 0m);
            var discountPercentage = totalSubtotal > 0 ? (totalDiscountAmount / totalSubtotal) * 100m : 0m;
            var discountAmount = totalPrice * discountPercentage / 100;
            return totalPrice - discountAmount;
        });

        // Recent quotations (last 10) with valuation calculations
        var recentQuotations = allQuotations
            .Take(10)
            .Select(q =>
            {
                var totalPrice = q.QuotationModules.Sum(m => modulePrices.GetValueOrDefault(m.ModuleName, 0));
                var totalSubtotal = q.QuotationModules.Sum(m => m.ModuleSubtotal ?? 0m);
                var totalDiscountAmount = q.QuotationModules.Sum(m => m.DiscountAmount ?? 0m);
                var discountPercentage = totalSubtotal > 0 ? (totalDiscountAmount / totalSubtotal) * 100m : 0m;
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

    private async Task PopulateModuleTimelineDeliverySnapshotsAsync(QuotationRequest request)
    {
        var selectedNames = request.SelectedModules
            .Select(name => name.Trim())
            .ToList();
        var modules = await _dbContext.Modules
            .AsNoTracking()
            .Where(module => selectedNames.Contains(module.ModuleName))
            .ToDictionaryAsync(module => module.ModuleName, StringComparer.OrdinalIgnoreCase);
        var detailsByModule = (request.ModuleDetails ?? new List<QuotationModuleRequest>())
            .ToDictionary(detail => detail.ModuleName.Trim(), StringComparer.OrdinalIgnoreCase);
        request.ModuleDetails ??= new List<QuotationModuleRequest>();

        foreach (var moduleName in selectedNames)
        {
            if (!modules.TryGetValue(moduleName, out var module))
                throw new ArgumentException($"Unknown module '{moduleName}'.");

            ValidateTimelineDeliveryConfiguration(moduleName, module);

            if (!detailsByModule.TryGetValue(moduleName, out var detail))
            {
                detail = new QuotationModuleRequest { ModuleName = moduleName };
                request.ModuleDetails.Add(detail);
                detailsByModule.Add(moduleName, detail);
            }

            detail.TimelineWeeks = module.TimelineWeeks;
            detail.DeliveryDays = module.DeliveryDays;
        }
    }

    private async Task<Dictionary<string, (int? TimelineWeeks, int? DeliveryDays)>>
        ResolveUpdateTimelineDeliverySnapshotsAsync(
            IEnumerable<string> selectedModules,
            IEnumerable<QuotationModuleEntity> existingQuotationModules)
    {
        var selectedNames = selectedModules.Select(name => name.Trim()).ToList();
        var existingByModule = existingQuotationModules.ToDictionary(
            module => module.ModuleName,
            StringComparer.OrdinalIgnoreCase);
        var masterModules = await _dbContext.Modules
            .AsNoTracking()
            .Where(module => selectedNames.Contains(module.ModuleName))
            .ToDictionaryAsync(module => module.ModuleName, StringComparer.OrdinalIgnoreCase);
        var snapshots = new Dictionary<string, (int? TimelineWeeks, int? DeliveryDays)>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var moduleName in selectedNames)
        {
            if (existingByModule.TryGetValue(moduleName, out var existing))
            {
                snapshots[moduleName] = (existing.TimelineWeeks, existing.DeliveryDays);
                continue;
            }

            if (!masterModules.TryGetValue(moduleName, out var module))
                throw new ArgumentException($"Unknown module '{moduleName}'.");

            ValidateTimelineDeliveryConfiguration(moduleName, module);
            snapshots[moduleName] = (module.TimelineWeeks, module.DeliveryDays);
        }

        return snapshots;
    }

    private static void ValidateTimelineDeliveryConfiguration(string moduleName, ModuleEntity module)
    {
        if (module.TimelineWeeks is null || module.DeliveryDays is null)
        {
            throw new ArgumentException(
                $"Timeline and delivery are not set for module '{moduleName}'. Set them on the Modules page.");
        }
    }

    private async Task ValidateAdditionalScopesAsync(IEnumerable<AdditionalScopeRequest>? scopes)
    {
        var additionalScopes = scopes?.ToList() ?? new List<AdditionalScopeRequest>();
        if (additionalScopes.Count == 0) return;

        var modules = await _moduleService.GetModulesAsync();
        var validNames = modules
            .Select(module => module.Module)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknown = additionalScopes
            .Select(scope => scope.Modules?.Trim() ?? string.Empty)
            .Where(moduleName =>
                !string.IsNullOrWhiteSpace(moduleName) &&
                !IsOtherScopeModule(moduleName) &&
                !validNames.Contains(moduleName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (unknown.Count > 0)
            throw new ArgumentException($"Unknown additional scope module(s): {string.Join(", ", unknown)}");
    }

    private static bool IsOtherScopeModule(string moduleName) =>
        string.Equals(moduleName, "Other", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(moduleName, "Others", StringComparison.OrdinalIgnoreCase);

    private async Task SaveToDatabaseAsync(QuotationResult result, QuotationRequest request, string quotationNo)
    {
        var moduleIds = await _dbContext.Modules
            .AsNoTracking()
            .ToDictionaryAsync(m => m.ModuleName, m => m.Id, StringComparer.OrdinalIgnoreCase);
        var detailsByModule = request.ModuleDetails
            .ToDictionary(d => d.ModuleName.Trim(), StringComparer.OrdinalIgnoreCase);
        var pricing = await CalculatePricingAsync(request.SelectedModules, request.ModuleDetails, request.DiscountPercentage);

        var quotation = new QuotationEntity
        {
            Id = result.QuotationId,
            OrganizationName = request.OrganizationName,
            ValidationDate = request.ValidationDate,
            ExpectedStartDate = request.ExpectedStartDate,
            QuotationNo = quotationNo,
            Date = request.Date,
            ReferenceBy = request.ReferenceBy,
            CreatedByUser = string.IsNullOrWhiteSpace(request.CreatedByUser) ? request.ReferenceBy : request.CreatedByUser.Trim(),
            QuotationToName = request.QuotationTo.Name,
            QuotationToAddress = request.QuotationTo.Address,
            QuotationToContactNo = request.QuotationTo.ContactNo,
            QuotationToEmail = request.QuotationTo.Email,
            GeneratedAt = result.GeneratedAt,
            DiscountPercentage = null,
            ModulePriceTotal = pricing.Sum(p => p.ModulePrice),
            ImplementationPriceTotal = pricing.Sum(p => p.ImplementationPrice),
            Subtotal = pricing.Sum(p => p.ModuleSubtotal),
            DiscountAmount = pricing.Sum(p => p.DiscountAmount),
            FinalPrice = pricing.Sum(p => p.FinalPrice),
            QuotationModules = request.SelectedModules.Select(m => new QuotationModuleEntity
            {
                QuotationId = result.QuotationId,
                ModuleName = m,
                NoOfUsers = detailsByModule.TryGetValue(m, out var detail) ? detail.NoOfUsers : null,
                NoOfInstallations = detailsByModule.TryGetValue(m, out detail) ? detail.NoOfInstallations : null,
                NoOfSites = detailsByModule.TryGetValue(m, out detail) ? detail.NoOfSites : null,
                ImplementationEffortUnit = detailsByModule.TryGetValue(m, out detail)
                    ? detail.ImplementationEffortUnit
                    : null,
                ModulePrice = pricing.First(p => string.Equals(p.ModuleName, m, StringComparison.OrdinalIgnoreCase)).ModulePrice,
                ImplementationUnitPrice = pricing.First(p => string.Equals(p.ModuleName, m, StringComparison.OrdinalIgnoreCase)).ImplementationUnitPrice,
                ImplementationMultiplier = pricing.First(p => string.Equals(p.ModuleName, m, StringComparison.OrdinalIgnoreCase)).ImplementationMultiplier,
                ImplementationPrice = pricing.First(p => string.Equals(p.ModuleName, m, StringComparison.OrdinalIgnoreCase)).ImplementationPrice,
                ModuleSubtotal = pricing.First(p => string.Equals(p.ModuleName, m, StringComparison.OrdinalIgnoreCase)).ModuleSubtotal,
                DiscountPercentage = pricing.First(p => string.Equals(p.ModuleName, m, StringComparison.OrdinalIgnoreCase)).DiscountPercentage,
                DiscountAmount = pricing.First(p => string.Equals(p.ModuleName, m, StringComparison.OrdinalIgnoreCase)).DiscountAmount,
                FinalPrice = pricing.First(p => string.Equals(p.ModuleName, m, StringComparison.OrdinalIgnoreCase)).FinalPrice,
                TimelineWeeks = detailsByModule.TryGetValue(m, out var timelineDetail)
                    ? timelineDetail.TimelineWeeks
                    : null,
                DeliveryDays = detailsByModule.TryGetValue(m, out timelineDetail)
                    ? timelineDetail.DeliveryDays
                    : null
            }).ToList(),
            AdditionalScopes = request.AdditionalScopes
                .Where(scope => !string.IsNullOrWhiteSpace(scope.Modules))
                .Select(scope =>
                {
                    var moduleName = scope.Modules.Trim();
                    var isOtherScope = IsOtherScopeModule(moduleName);

                    return new AdditionalScope
                    {
                        QuotationId = result.QuotationId,
                        Requirement = scope.Requirement?.Trim() ?? string.Empty,
                        ModulesId = isOtherScope
                            ? null
                            : scope.ModulesId > 0
                                ? scope.ModulesId
                                : moduleIds.GetValueOrDefault(moduleName),
                        Modules = isOtherScope ? "Others" : moduleName,
                        NoOfManpower = scope.NoOfManpower,
                        NoOfDays = scope.NoOfDays,
                        Rate = scope.Rate,
                        Amount = scope.NoOfManpower * scope.NoOfDays * scope.Rate,
                        Price = scope.NoOfManpower * scope.NoOfDays * scope.Rate
                    };
                })
                .ToList(),
            QuotationTimeEstimates = request.TimeEstimate.Select(t => new QuotationTimeEstimateEntity
            {
                QuotationId = result.QuotationId,
                ModuleName = t.ModuleName,
                StageKey = t.StageKey,
                StartWeek = (byte)t.StartWeek,
                EndWeek = (byte)t.EndWeek
            }).ToList()
        };

        _dbContext.Quotations.Add(quotation);
        AddHistorySnapshot(quotation, "Created");
        await _dbContext.SaveChangesAsync();
    }

    private async Task<List<QuotationModulePricing>> CalculatePricingAsync(
        IEnumerable<string> selectedModules,
        IEnumerable<QuotationModuleRequest>? moduleDetails,
        decimal discountPercentage)
    {
        var modules = await _moduleService.GetModulesAsync();
        var modulePrices = modules.ToDictionary(m => m.Module, StringComparer.OrdinalIgnoreCase);
        var detailsByModule = (moduleDetails ?? Enumerable.Empty<QuotationModuleRequest>())
            .ToDictionary(d => d.ModuleName.Trim(), StringComparer.OrdinalIgnoreCase);
        return selectedModules.Select(moduleName =>
        {
            modulePrices.TryGetValue(moduleName.Trim(), out var module);
            detailsByModule.TryGetValue(moduleName.Trim(), out var detail);

            var modulePrice = detail?.ModulePriceOverride ?? module?.Price ?? 0m;
            if (modulePrice < 0)
                throw new ArgumentException($"Module price cannot be negative for '{moduleName}'.");
            var implementationUnitPrice = module?.ImplementationEffortCost ?? 0m;
            var implementationMultiplier = GetEffortMultiplier(detail?.ImplementationEffortUnit);
            var implementationPrice = implementationUnitPrice * implementationMultiplier;
            var moduleSubtotal = modulePrice + implementationPrice;
            var discount = Math.Clamp(detail?.DiscountPercentage ?? discountPercentage, 0m, 100m);
            var discountAmount = modulePrice * discount / 100m;

            return new QuotationModulePricing
            {
                ModuleName = moduleName,
                ModulePrice = modulePrice,
                ImplementationUnitPrice = implementationUnitPrice,
                ImplementationMultiplier = implementationMultiplier,
                ImplementationPrice = implementationPrice,
                ModuleSubtotal = moduleSubtotal,
                DiscountPercentage = discount,
                DiscountAmount = discountAmount,
                FinalPrice = moduleSubtotal - discountAmount
            };
        }).ToList();
    }

    private sealed class QuotationModulePricing
    {
        public string ModuleName { get; init; } = string.Empty;
        public decimal ModulePrice { get; init; }
        public decimal ImplementationUnitPrice { get; init; }
        public decimal ImplementationMultiplier { get; init; }
        public decimal ImplementationPrice { get; init; }
        public decimal ModuleSubtotal { get; init; }
        public decimal DiscountPercentage { get; init; }
        public decimal DiscountAmount { get; init; }
        public decimal FinalPrice { get; init; }
    }

    private sealed class DeliverySummaryRow
    {
        public string ModuleName { get; init; } = string.Empty;
        public string PriceText { get; init; } = string.Empty;
        public string TimelineText { get; init; } = string.Empty;
        public string DeliveryText { get; init; } = string.Empty;
    }

    private static List<DeliverySummaryRow> BuildDeliverySummary(
        QuotationRequest request,
        IReadOnlyCollection<QuotationModulePricing> pricing,
        IReadOnlyDictionary<string, decimal?>? finalPriceSnapshots = null)
    {
        var pricingByModule = pricing.ToDictionary(
            item => item.ModuleName,
            StringComparer.OrdinalIgnoreCase);
        var detailsByModule = (request.ModuleDetails ?? new List<QuotationModuleRequest>())
            .ToDictionary(
                detail => detail.ModuleName.Trim(),
                StringComparer.OrdinalIgnoreCase);
        var rows = new List<DeliverySummaryRow>();
        var moduleFinalPrices = new List<decimal>();
        var moduleTimelines = new List<int?>();
        var moduleDeliveries = new List<int?>();

        foreach (var moduleName in request.SelectedModules)
        {
            detailsByModule.TryGetValue(moduleName, out var detail);
            var price = finalPriceSnapshots is not null &&
                        finalPriceSnapshots.TryGetValue(moduleName, out var savedPrice)
                ? savedPrice ?? 0m
                : pricingByModule.GetValueOrDefault(moduleName)?.FinalPrice ?? 0m;
            moduleFinalPrices.Add(price);
            moduleTimelines.Add(detail?.TimelineWeeks);
            moduleDeliveries.Add(detail?.DeliveryDays);

            rows.Add(new DeliverySummaryRow
            {
                ModuleName = moduleName,
                PriceText = price.ToString("N2"),
                TimelineText = detail?.TimelineWeeks is int weeks
                    ? $"{weeks} weeks"
                    : "To be confirmed",
                DeliveryText = FormatPlannedDelivery(request.ExpectedStartDate, detail?.DeliveryDays)
            });
        }

        var additionalScopes = request.AdditionalScopes?
            .Where(scope => !string.IsNullOrWhiteSpace(scope.Modules))
            .ToList() ?? new List<AdditionalScopeRequest>();
        var additionalScopeTotal = 0m;
        foreach (var scope in additionalScopes)
        {
            var amount = CalculateAdditionalScopeAmount(scope);
            additionalScopeTotal += amount;
            rows.Add(new DeliverySummaryRow
            {
                ModuleName = string.IsNullOrWhiteSpace(scope.Requirement)
                    ? scope.Modules
                    : scope.Requirement.Trim(),
                PriceText = amount.ToString("N2"),
                TimelineText = "With Go-Live",
                DeliveryText = string.Empty
            });
        }

        var missingTimelineOrDelivery =
            moduleTimelines.Any(value => value is null) ||
            moduleDeliveries.Any(value => value is null);
        var overallTimeline = missingTimelineOrDelivery
            ? "To be confirmed"
            : $"{moduleTimelines.Max(value => value!.Value)} weeks overall";
        var overallDelivery = missingTimelineOrDelivery
            ? "To be confirmed"
            : FormatPlannedDelivery(request.ExpectedStartDate, moduleDeliveries.Max(value => value!.Value));

        for (var index = request.SelectedModules.Count; index < rows.Count; index++)
        {
            rows[index] = new DeliverySummaryRow
            {
                ModuleName = rows[index].ModuleName,
                PriceText = rows[index].PriceText,
                TimelineText = rows[index].TimelineText,
                DeliveryText = overallDelivery
            };
        }

        rows.Add(new DeliverySummaryRow
        {
            ModuleName = "Total",
            PriceText = (moduleFinalPrices.Sum() + additionalScopeTotal).ToString("N2"),
            TimelineText = overallTimeline,
            DeliveryText = overallDelivery
        });

        return rows;
    }

    private static string FormatPlannedDelivery(DateTime? expectedStartDate, int? deliveryDays)
    {
        if (deliveryDays is null)
            return "To be confirmed";

        return expectedStartDate is DateTime startDate
            ? startDate.AddDays(deliveryDays.Value).ToString("dd MMM yyyy", CultureInfo.InvariantCulture)
            : $" {deliveryDays.Value} Days";
    }

    private static void PopulateDeliverySummaryTable(
        Body body,
        IReadOnlyList<DeliverySummaryRow> summaryRows)
    {
        var templateRow = body.Descendants<TableRow>().FirstOrDefault(row =>
        {
            var rowText = string.Concat(row.Descendants<Text>().Select(text => text.Text));
            return rowText.Contains("{{S_MOD}}", StringComparison.Ordinal);
        });
        var totalRow = body.Descendants<TableRow>().FirstOrDefault(row =>
        {
            var rowText = string.Concat(row.Descendants<Text>().Select(text => text.Text));
            return rowText.Contains("{{S_T_LABEL}}", StringComparison.Ordinal);
        });

        if (templateRow is null || totalRow is null)
        {
            throw new InvalidOperationException(
                "Recommended quotation format table is missing its data or total placeholder row.");
        }

        var dataRows = summaryRows.Take(summaryRows.Count - 1);
        foreach (var summaryRow in dataRows)
        {
            var row = (TableRow)templateRow.CloneNode(true);
            ReplaceDeliverySummaryRow(row, summaryRow, false);
            MarkRowCannotSplit(row);
            templateRow.InsertBeforeSelf(row);
        }

        templateRow.Remove();
        ReplaceDeliverySummaryRow(totalRow, summaryRows[^1], true);
        MarkRowCannotSplit(totalRow);
    }

    private static void ReplaceDeliverySummaryRow(
        TableRow row,
        DeliverySummaryRow summary,
        bool isTotal)
    {
        var replacements = isTotal
            ? new Dictionary<string, string>
            {
                ["{{S_T_LABEL}}"] = summary.ModuleName,
                ["{{S_T_PRICE}}"] = summary.PriceText,
                ["{{S_T_TIMELINE}}"] = summary.TimelineText,
                ["{{S_T_DELIVERY}}"] = summary.DeliveryText
            }
            : new Dictionary<string, string>
            {
                ["{{S_MOD}}"] = summary.ModuleName,
                ["{{S_PRICE}}"] = summary.PriceText,
                ["{{S_TIMELINE}}"] = summary.TimelineText,
                ["{{S_DELIVERY}}"] = summary.DeliveryText
            };

        foreach (var paragraph in row.Descendants<Paragraph>())
            ReplaceParagraphText(paragraph, replacements);
    }

    private static void MarkRowCannotSplit(TableRow row)
    {
        var properties = row.GetFirstChild<TableRowProperties>();
        if (properties is null)
        {
            properties = new TableRowProperties();
            row.PrependChild(properties);
        }

        if (properties.GetFirstChild<CantSplit>() is null)
            properties.AppendChild(new CantSplit());
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
                !IsValidEffortUnit(detail.ImplementationEffortUnit.Trim()))
            {
                throw new ArgumentException(
                    $"Invalid implementation effort for '{detail.ModuleName}'.");
            }
        }
    }

    private static bool IsValidEffortUnit(string effortUnit)
    {
        if (AllowedEffortUnits.Contains(effortUnit)) return true;

        return TryParseEffortDays(effortUnit, out var days) && days > 0;
    }

    private void AddHistorySnapshot(QuotationEntity quotation, string changeType)
    {
        var totalSubtotal = quotation.QuotationModules.Sum(m => m.ModuleSubtotal ?? 0m);
        var totalDiscountAmount = quotation.QuotationModules.Sum(m => m.DiscountAmount ?? 0m);
        var overallDiscountPercentage = totalSubtotal > 0 ? (totalDiscountAmount / totalSubtotal) * 100m : (decimal?)null;

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
            DiscountPercentage = overallDiscountPercentage,
            ChangedAt = DateTime.Now,
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
            .Include(q => q.AdditionalScopes)
            .FirstOrDefaultAsync(q => q.Id == quotationId);

        if (quotation == null)
            return null;

        var timeEstimates = await _dbContext.QuotationTimeEstimates
            .Where(t => t.QuotationId == quotationId)
            .ToListAsync();

        // Update discount percentage on module level
        await ApplyPricingSnapshotAsync(quotation, quotation.QuotationModules, discountPercentage);
        AddHistorySnapshot(quotation, "DiscountUpdated");

        // Build request from stored data
        var request = new QuotationRequest
        {
            ValidationDate = quotation.ValidationDate,
            ExpectedStartDate = quotation.ExpectedStartDate,
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
                ImplementationEffortUnit = m.ImplementationEffortUnit,
                DiscountPercentage = m.DiscountPercentage,
                TimelineWeeks = m.TimelineWeeks,
                DeliveryDays = m.DeliveryDays
            }).ToList(),
            AdditionalScopes = quotation.AdditionalScopes
                .Select(s => new AdditionalScopeRequest
                {
                    Requirement = s.Requirement,
                    Modules = s.Modules,
                    ModulesId = s.ModulesId ?? 0,
                    NoOfManpower = s.NoOfManpower,
                    NoOfDays = s.NoOfDays,
                    Rate = s.Rate
                }).ToList(),
            QuotationTo = new QuotationToInfo
            {
                Name = quotation.QuotationToName,
                Address = quotation.QuotationToAddress,
                ContactNo = quotation.QuotationToContactNo,
                Email = quotation.QuotationToEmail
            },
            DiscountPercentage = discountPercentage,
            TimeEstimate = timeEstimates.Any()
                ? timeEstimates.Select(t => new TimeEstimateStageRequest
                {
                    ModuleName = t.ModuleName,
                    StageKey = t.StageKey,
                    StartWeek = t.StartWeek,
                    EndWeek = t.EndWeek
                }).ToList()
                : TimeEstimateHelper.Defaults(
                    quotation.QuotationModules.Select(m => m.ModuleName).ToList())
        };

        // Regenerate documents with new discount
        var finalPriceSnapshots = quotation.QuotationModules.ToDictionary(
            module => module.ModuleName,
            module => module.FinalPrice,
            StringComparer.OrdinalIgnoreCase);
        var docxPath = await GenerateWordDocumentAsync(request, quotationId, finalPriceSnapshots);
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
    /// Updates quotation details (validation date, modules, additional scopes) and regenerates documents.
    /// </summary>
    public async Task<QuotationResult?> UpdateQuotationAsync(string quotationId, DateTime validationDate, DateTime? expectedStartDate, List<string> selectedModules, List<QuotationModuleRequest> moduleDetails, List<AdditionalScopeRequest> additionalScopes, List<TimeEstimateStageRequest> timeEstimate)
    {
        var quotation = await _dbContext.Quotations
            .Include(q => q.QuotationModules)
            .Include(q => q.AdditionalScopes)
            .FirstOrDefaultAsync(q => q.Id == quotationId);

        if (quotation == null)
            return null;

        // Load existing time estimates for deletion
        var existingTimeEstimates = await _dbContext.QuotationTimeEstimates
            .Where(t => t.QuotationId == quotationId)
            .ToListAsync();

        await ValidateModulesAsync(selectedModules);
        var timelineSnapshots = await ResolveUpdateTimelineDeliverySnapshotsAsync(
            selectedModules,
            quotation.QuotationModules);

        timeEstimate = TimeEstimateHelper.PrepareForModules(timeEstimate, selectedModules);

        quotation.ValidationDate = validationDate;
        quotation.ExpectedStartDate = expectedStartDate;

        var detailsByModule = (moduleDetails ?? new List<QuotationModuleRequest>())
            .ToDictionary(d => d.ModuleName.Trim(), StringComparer.OrdinalIgnoreCase);

        _dbContext.QuotationModules.RemoveRange(quotation.QuotationModules);
        quotation.QuotationModules = selectedModules.Select(m => new QuotationModuleEntity
        {
            QuotationId = quotationId,
            ModuleName = m,
            NoOfUsers = detailsByModule.TryGetValue(m, out var detail) ? detail.NoOfUsers : null,
            NoOfInstallations = detailsByModule.TryGetValue(m, out detail) ? detail.NoOfInstallations : null,
            NoOfSites = detailsByModule.TryGetValue(m, out detail) ? detail.NoOfSites : null,
            ImplementationEffortUnit = detailsByModule.TryGetValue(m, out detail)
                ? detail.ImplementationEffortUnit
                : null,
            DiscountPercentage = detailsByModule.TryGetValue(m, out detail)
                ? detail.DiscountPercentage
                : null,
            TimelineWeeks = timelineSnapshots[m].TimelineWeeks,
            DeliveryDays = timelineSnapshots[m].DeliveryDays
        }).ToList();

        // Update Additional Scopes
        var moduleIds = await _dbContext.Modules
            .AsNoTracking()
            .ToDictionaryAsync(m => m.ModuleName, m => m.Id, StringComparer.OrdinalIgnoreCase);

        _dbContext.AdditionalScopes.RemoveRange(quotation.AdditionalScopes);
        quotation.AdditionalScopes = (additionalScopes ?? new List<AdditionalScopeRequest>())
            .Where(scope => !string.IsNullOrWhiteSpace(scope.Modules))
            .Select(scope =>
            {
                var moduleName = scope.Modules.Trim();
                var isOtherScope = IsOtherScopeModule(moduleName);

                return new AdditionalScope
                {
                    QuotationId = quotationId,
                    Requirement = scope.Requirement?.Trim() ?? string.Empty,
                    ModulesId = isOtherScope
                        ? null
                        : scope.ModulesId > 0
                            ? scope.ModulesId
                            : moduleIds.GetValueOrDefault(moduleName),
                    Modules = isOtherScope ? "Others" : moduleName,
                    NoOfManpower = scope.NoOfManpower,
                    NoOfDays = scope.NoOfDays,
                    Rate = scope.Rate,
                    Amount = scope.NoOfManpower * scope.NoOfDays * scope.Rate,
                    Price = scope.NoOfManpower * scope.NoOfDays * scope.Rate
                };
            })
            .ToList();

        // Update Time Estimates - delete existing, insert new
        _dbContext.QuotationTimeEstimates.RemoveRange(existingTimeEstimates);
        quotation.QuotationTimeEstimates = timeEstimate.Select(t => new QuotationTimeEstimateEntity
        {
            QuotationId = quotationId,
            ModuleName = t.ModuleName,
            StageKey = t.StageKey,
            StartWeek = (byte)t.StartWeek,
            EndWeek = (byte)t.EndWeek
        }).ToList();

        // Calculate discount percentage from module-level discounts
        var totalSubtotal = quotation.QuotationModules.Sum(m => m.ModuleSubtotal ?? 0m);
        var totalDiscountAmount = quotation.QuotationModules.Sum(m => m.DiscountAmount ?? 0m);
        var discountPercentage = totalSubtotal > 0 ? (totalDiscountAmount / totalSubtotal) * 100m : 0m;

        await ApplyPricingSnapshotAsync(quotation, quotation.QuotationModules, discountPercentage);
        AddHistorySnapshot(quotation, "DetailsUpdated");

        var request = new QuotationRequest
        {
            ValidationDate = validationDate,
            ExpectedStartDate = expectedStartDate,
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
                    ImplementationEffortUnit = m.ImplementationEffortUnit,
                    DiscountPercentage = m.DiscountPercentage,
                    TimelineWeeks = m.TimelineWeeks,
                    DeliveryDays = m.DeliveryDays
                }).ToList(),
            AdditionalScopes = quotation.AdditionalScopes
                .Select(s => new AdditionalScopeRequest
                {
                    Requirement = s.Requirement,
                    Modules = s.Modules,
                    ModulesId = s.ModulesId ?? 0,
                    NoOfManpower = s.NoOfManpower,
                    NoOfDays = s.NoOfDays,
                    Rate = s.Rate
                }).ToList(),
            QuotationTo = new QuotationToInfo
            {
                Name = quotation.QuotationToName,
                Address = quotation.QuotationToAddress,
                ContactNo = quotation.QuotationToContactNo,
                Email = quotation.QuotationToEmail
            },
            DiscountPercentage = discountPercentage,
            TimeEstimate = timeEstimate
        };

        var finalPriceSnapshots = quotation.QuotationModules.ToDictionary(
            module => module.ModuleName,
            module => module.FinalPrice,
            StringComparer.OrdinalIgnoreCase);
        var docxPath = await GenerateWordDocumentAsync(request, quotationId, finalPriceSnapshots);
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

    private async Task ApplyPricingSnapshotAsync(
        QuotationEntity quotation,
        IEnumerable<QuotationModuleEntity> quotationModules,
        decimal discountPercentage)
    {
        var requestDetails = quotationModules.Select(m => new QuotationModuleRequest
        {
            ModuleName = m.ModuleName,
            NoOfUsers = m.NoOfUsers,
            NoOfInstallations = m.NoOfInstallations,
            NoOfSites = m.NoOfSites,
            ImplementationEffortUnit = m.ImplementationEffortUnit,
            DiscountPercentage = m.DiscountPercentage
        }).ToList();
        var pricing = await CalculatePricingAsync(
            quotationModules.Select(m => m.ModuleName),
            requestDetails,
            discountPercentage);

        foreach (var item in quotationModules)
        {
            var values = pricing.First(p => string.Equals(p.ModuleName, item.ModuleName, StringComparison.OrdinalIgnoreCase));
            item.ModulePrice = values.ModulePrice;
            item.ImplementationUnitPrice = values.ImplementationUnitPrice;
            item.ImplementationMultiplier = values.ImplementationMultiplier;
            item.ImplementationPrice = values.ImplementationPrice;
            item.ModuleSubtotal = values.ModuleSubtotal;
            item.DiscountPercentage = values.DiscountPercentage;
            item.DiscountAmount = values.DiscountAmount;
            item.FinalPrice = values.FinalPrice;
        }

        quotation.ModulePriceTotal = pricing.Sum(p => p.ModulePrice);
        quotation.ImplementationPriceTotal = pricing.Sum(p => p.ImplementationPrice);
        quotation.Subtotal = pricing.Sum(p => p.ModuleSubtotal);
        quotation.DiscountAmount = pricing.Sum(p => p.DiscountAmount);
        quotation.FinalPrice = pricing.Sum(p => p.FinalPrice);
    }

    private async Task<string> GenerateWordDocumentAsync(
        QuotationRequest request,
        string quotationId,
        IReadOnlyDictionary<string, decimal?>? finalPriceSnapshots = null)
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

                var pricing = await CalculatePricingAsync(request.SelectedModules, request.ModuleDetails, request.DiscountPercentage);
                var deliverySummary = BuildDeliverySummary(
                    request,
                    pricing,
                    finalPriceSnapshots);
                var modulePriceTotal = pricing.Sum(p => p.ModulePrice);
                var implementationPriceTotal = pricing.Sum(p => p.ImplementationPrice);
                var subtotal = pricing.Sum(p => p.ModuleSubtotal);
                var discountAmount = pricing.Sum(p => p.DiscountAmount);
                var finalPrice = pricing.Sum(p => p.FinalPrice);
                var additionalScopeSubtotal =
                    request.AdditionalScopes?.Sum(CalculateAdditionalScopeAmount) ?? 0m;
                var overallFinalPrice = finalPrice + additionalScopeSubtotal;
                var overallDiscountPct = subtotal > 0 ? (discountAmount / subtotal) * 100m : 0m;
                var anyModuleHasDiscount = pricing.Any(p => p.DiscountPercentage > 0);
                var moduleParticularsText = FormatModuleParticulars(
                    request.SelectedModules,
                    request.ModuleDetails,
                    pricing);
                var modulePricingValuesText = FormatModulePricingValues(
                    request.SelectedModules,
                    request.ModuleDetails,
                    modulePrices,
                    pricing);
                var overallPricingParticularsText = FormatOverallPricingParticulars(
                    overallDiscountPct, anyModuleHasDiscount);
                var overallPricingValuesText = FormatOverallPricingValues(
                    modulePriceTotal,
                    implementationPriceTotal,
                    subtotal,
                    overallDiscountPct,
                    discountAmount,
                    overallFinalPrice,
                    anyModuleHasDiscount);
                var pricingParticularsText = string.Join(
                    Environment.NewLine,
                    "CQUAL {{M_LIST}}",
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
                    ["{{M_LIST}}"] = string.Join(", ", request.SelectedModules),
                    ["{{M_REQUIREMENTS}}"] = FormatModuleRequirements(
                                        request.SelectedModules,
                                        request.ModuleDetails),
                    ["{{M_DETAILS}}"] = string.Empty,
                    ["{{M_PRICING}}"] = string.Empty,
                    ["{{OVERALL_PRICING}}"] = string.Empty,
                    ["{{A_S_SUBTOTAL}}"] = $"{additionalScopeSubtotal:N2}",
                    // Template placeholders (from temp_template)
                    ["{{CONTACT_NAME}}"] = request.QuotationTo?.Name ?? "",
                    ["{{CONTACT_ADDRESS}}"] = request.QuotationTo?.Address ?? "",
                    ["{{CONTACT_PHONE}}"] = request.QuotationTo?.ContactNo ?? "",
                    ["{{CONTACT_EMAIL}}"] = request.QuotationTo?.Email ?? "",
                    ["{{ORG_NAME}}"] = request.OrganizationName ?? "",
                    ["{{REQUIRED}}"] = string.Join(", ", request.SelectedModules),
                    ["{{VALIDATION_DATE}}"] = request.ValidationDate.ToString("dd/MM/yyyy"),
                    ["{{TotalPrice}}"] = $"Total Price: {subtotal:N2}",
                    ["{{M_PRICE}}"] = $"Module Price: {modulePriceTotal:N2}",
                    ["{{IMPLEMENTATION_TOTAL}}"] = $"Implementation Total: {implementationPriceTotal:N2}",
                    ["{{SUBTOTAL}}"] = $"Subtotal: {subtotal:N2}",
                    ["{{FinalPrice}}"] = $"Final Price: {overallFinalPrice:N2}",
                    ["{{IMPLEMENTATION_PRICE}}"] = $"Implementation Price: {implementationPriceTotal:N2}"
                };

                if (anyModuleHasDiscount && discountAmount > 0)
                {
                    replacements["{{DiscountPercentage}}"] = $"Discount Percentage: {overallDiscountPct:N2}%";
                    replacements["{{DiscountAmount}}"] = $"Discount Amount: {discountAmount:N2}";
                }
                else
                {
                    replacements["{{DiscountPercentage}}"] = string.Empty;
                    replacements["{{DiscountAmount}}"] = string.Empty;
                }

                PopulateScopeTable(body, modules, request.SelectedModules);
                PopulatePricingTableFromTemplate(body, request, modulePrices, pricing, subtotal, implementationPriceTotal, modulePriceTotal, discountAmount, overallFinalPrice);
                PopulateLicenseRenewalModuleRows(body, request.SelectedModules);
                NormalizeStandardPricingRows(body);

                PopulateAdditionalScopeTable(body, request.AdditionalScopes);
                RemoveAdditionalScopeSubtotalRowWhenEmpty(
                    body,
                    request.AdditionalScopes);

                PopulatePriceSummaryTable(body, request.SelectedModules, request.ModuleDetails, modulePrices, request.RenewalPercentage, request.AnnualEscalationPercentage);

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

                PopulateDeliverySummaryTable(body, deliverySummary);
                PopulateTimeEstimateTable(doc, request);
            }
        }

        return outputPath;
    }

    private static void PopulateTimeEstimateTable(
        WordprocessingDocument doc,
        QuotationRequest request)
    {
        var body = doc.MainDocumentPart?.Document.Body;
        if (body == null) return;

        Table? timeEstimateTable = null;
        TableRow? moduleHeaderTemplate = null;
        List<TableRow>? templateRows = null;
        foreach (var table in body.Descendants<Table>())
        {
            var rows = table.Elements<TableRow>().ToList();
            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                var firstCell = rows[rowIndex].Elements<TableCell>().FirstOrDefault();
                var cellText = firstCell == null
                    ? string.Empty
                    : string.Concat(firstCell.Descendants<Text>().Select(text => text.Text));
                if (cellText.Trim().StartsWith("Pre-Implementation Visits", StringComparison.OrdinalIgnoreCase))
                {
                    timeEstimateTable = table;
                    templateRows = rows.Skip(rowIndex).Take(TimeEstimateHelper.Stages.Length).ToList();
                    if (rowIndex > 0)
                    {
                        var previousRow = rows[rowIndex - 1];
                        var previousRowText = string.Concat(
                            previousRow.Descendants<Text>().Select(text => text.Text));
                        if (previousRowText.Contains("{{M_NAME}}", StringComparison.Ordinal))
                            moduleHeaderTemplate = previousRow;
                    }
                    break;
                }
            }

            if (timeEstimateTable != null)
                break;
        }

        if (timeEstimateTable == null || templateRows == null)
        {
            throw new InvalidOperationException("Time Estimate table not found in template. Expected a row starting with 'Pre-Implementation Visits'.");
        }

        var stageKeys = TimeEstimateHelper.Stages;
        if (templateRows.Count != stageKeys.Length)
            throw new InvalidOperationException($"Time Estimate table has {templateRows.Count} stage rows, expected {stageKeys.Length}.");

        var estimatesByModule = request.TimeEstimate
            .GroupBy(estimate => estimate.ModuleName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.ToDictionary(
                    estimate => estimate.StageKey,
                    StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);

        var insertionPoint = templateRows[^1];
        for (var moduleIndex = 0; moduleIndex < request.SelectedModules.Count; moduleIndex++)
        {
            var moduleName = request.SelectedModules[moduleIndex];
            if (!estimatesByModule.TryGetValue(moduleName, out var moduleEstimates))
                throw new InvalidOperationException($"Missing time estimate for module '{moduleName}'.");

            if (moduleHeaderTemplate != null)
            {
                var moduleHeader = moduleIndex == 0
                    ? moduleHeaderTemplate
                    : (TableRow)moduleHeaderTemplate.CloneNode(true);
                if (moduleIndex > 0)
                {
                    timeEstimateTable.InsertAfter(moduleHeader, insertionPoint);
                    insertionPoint = moduleHeader;
                }

                var moduleHeaderCell = moduleHeader.Elements<TableCell>().FirstOrDefault()
                    ?? throw new InvalidOperationException("Module heading row in the Time Estimate table has no cells.");
                SetTableCellText(moduleHeaderCell, moduleName);
            }

            for (var stageIndex = 0; stageIndex < stageKeys.Length; stageIndex++)
            {
                var stageKey = stageKeys[stageIndex];
                if (!moduleEstimates.TryGetValue(stageKey, out var stage))
                    throw new InvalidOperationException($"Missing time estimate for module '{moduleName}', stage '{stageKey}'.");

                var row = moduleIndex == 0
                    ? templateRows[stageIndex]
                    : (TableRow)templateRows[stageIndex].CloneNode(true);
                if (moduleIndex > 0)
                {
                    timeEstimateTable.InsertAfter(row, insertionPoint);
                    insertionPoint = row;
                }

                var cells = row.Elements<TableCell>().ToList();
                if (cells.Count < TimeEstimateHelper.TotalWeeks + 1)
                    throw new InvalidOperationException($"Time Estimate table row for stage '{stageKey}' has only {cells.Count} cells, expected at least {TimeEstimateHelper.TotalWeeks + 1} (template problem).");

                var stageLabel = TimeEstimateHelper.GetStageLabel(stageKey);
                SetTableCellText(
                    cells[0],
                    moduleHeaderTemplate == null ? $"{moduleName} - {stageLabel}" : stageLabel);

                for (var week = 1; week <= TimeEstimateHelper.TotalWeeks; week++)
                {
                    var cell = cells[week];
                    var tcPr = cell.TableCellProperties;
                    if (tcPr == null)
                    {
                        tcPr = new TableCellProperties();
                        cell.PrependChild(tcPr);
                    }

                    var existingShading = tcPr.GetFirstChild<Shading>();
                    existingShading?.Remove();

                    if (stage.StartWeek <= week && week <= stage.EndWeek)
                    {
                        var shading = new Shading
                        {
                            Val = ShadingPatternValues.Clear,
                            Color = "auto",
                            Fill = "4A90D9"
                        };
                        tcPr.PrependChild(shading);
                    }
                }
            }
        }
    }

    private static void SetTableCellText(TableCell cell, string value)
    {
        var textNodes = cell.Descendants<Text>().ToList();
        if (textNodes.Count == 0)
        {
            cell.AppendChild(new Paragraph(new Run(new Text(value))));
            return;
        }

        textNodes[0].Text = value;
        foreach (var textNode in textNodes.Skip(1))
            textNode.Text = string.Empty;
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
            _ when TryParseEffortDays(effortUnit, out var days) => days,
            _ => 0m
        };
    }

    private static bool TryParseEffortDays(string? effortUnit, out decimal days)
    {
        if (effortUnit is not null)
        {
            foreach (var suffix in new[] { " Man Days", " Man Day", " Days" })
            {
                if (effortUnit.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) &&
                    decimal.TryParse(
                        effortUnit[..^suffix.Length].Trim(),
                        NumberStyles.Number,
                        CultureInfo.InvariantCulture,
                        out days))
                {
                    return true;
                }
            }
        }

        days = 0m;
        return false;
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
        List<QuotationModulePricing> pricing)
    {
        var detailsByModule = (moduleDetails ?? Enumerable.Empty<QuotationModuleRequest>())
            .ToDictionary(
                detail => detail.ModuleName.Trim(),
                StringComparer.OrdinalIgnoreCase);
        var pricingByModule = pricing.ToDictionary(p => p.ModuleName.Trim(), StringComparer.OrdinalIgnoreCase);

        const string licenseRenewalText = "The License renewal would be required to be done every Year These renewal fees will facilitate to have the Product Upgrades, which would cover improvements, bug fixes, and changes in AIAG VDA compliances. Support of 7 Man days is included in this price.";

        return string.Join(
            Environment.NewLine + Environment.NewLine,
            selectedModules.Select(moduleName =>
            {
                detailsByModule.TryGetValue(moduleName.Trim(), out var detail);
                pricingByModule.TryGetValue(moduleName.Trim(), out var modulePricing);
                var moduleDiscountPct = modulePricing?.DiscountPercentage ?? 0m;
                var discountLine = moduleDiscountPct > 0 ? $"Discount ({moduleDiscountPct:N2}%):" : string.Empty;

                var lines = new List<string>
                {
                    $"{moduleName}:",
                    licenseRenewalText,
                    "Module Price:",
                    "Implementation Total:",
                    "Module Subtotal:"
                };
                if (!string.IsNullOrEmpty(discountLine))
                {
                    lines.Add(discountLine);
                }
                lines.Add("Module Final Price:");
                return string.Join(Environment.NewLine, lines);
            }));
    }

    private static string FormatModulePricingValues(
        IEnumerable<string> selectedModules,
        IEnumerable<QuotationModuleRequest>? moduleDetails,
        IReadOnlyDictionary<string, ModuleItem> modulePrices,
        List<QuotationModulePricing> pricing)
    {
        var detailsByModule = (moduleDetails ?? Enumerable.Empty<QuotationModuleRequest>())
            .ToDictionary(
                detail => detail.ModuleName.Trim(),
                StringComparer.OrdinalIgnoreCase);
        var pricingByModule = pricing.ToDictionary(p => p.ModuleName.Trim(), StringComparer.OrdinalIgnoreCase);

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
                pricingByModule.TryGetValue(moduleName.Trim(), out var modulePricing);

                var modulePrice = modulePricing?.ModulePrice ?? module?.Price ?? 0m;
                var implementationTotal = modulePricing?.ImplementationPrice ??
                    (module?.ImplementationEffortCost ?? 0m) *
                    GetEffortMultiplier(detail?.ImplementationEffortUnit);
                var moduleSubtotal = modulePricing?.ModuleSubtotal ??
                    modulePrice + implementationTotal;
                var moduleDiscountPct = modulePricing?.DiscountPercentage ?? 0m;
                var moduleDiscount = modulePricing?.DiscountAmount ??
                    modulePrice * moduleDiscountPct / 100m;
                var moduleFinalPrice = modulePricing?.FinalPrice ??
                    moduleSubtotal - moduleDiscount;

                lines.AddRange(Enumerable.Repeat(string.Empty, 5));
                lines.Add($"{modulePrice:N2}");
                lines.Add($"{implementationTotal:N2}");
                lines.Add($"{moduleSubtotal:N2}");
                if (moduleDiscountPct > 0)
                {
                    lines.Add($"{moduleDiscount:N2}");
                }
                lines.Add($"{moduleFinalPrice:N2}");
                if (index < moduleNames.Count - 1)
                {
                    lines.Add(string.Empty);
                }
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatOverallPricingParticulars(decimal overallDiscountPct, bool anyModuleHasDiscount)
    {
        var lines = new List<string>
        {
            "Overall Calculation:",
            "Module Price:",
            "Implementation Total:",
            "Subtotal:"
        };

        if (anyModuleHasDiscount && overallDiscountPct > 0)
        {
            lines.Add($"Discount ({overallDiscountPct:N2}%):");
        }

        lines.Add("Final Price:");

        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatOverallPricingValues(
        decimal modulePriceTotal,
        decimal implementationPriceTotal,
        decimal subtotal,
        decimal overallDiscountPct,
        decimal discountAmount,
        decimal finalPrice,
        bool anyModuleHasDiscount)
    {
        var lines = new List<string>
        {
            "",
            $"{modulePriceTotal:N2}",
            $"{implementationPriceTotal:N2}",
            $"{subtotal:N2}"
        };

        if (anyModuleHasDiscount && overallDiscountPct > 0)
        {
            lines.Add($"{discountAmount:N2}");
        }

        lines.Add($"{finalPrice:N2}");

        return string.Join(Environment.NewLine, lines);
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

        foreach (var module in modules)
        {
            var row = (TableRow)scopeRow.CloneNode(true);
            var rowReplacements = new Dictionary<string, string>
            {
                ["{{PILLAR}}"] = module.Pillar,
                ["{{MODULE}}"] = module.Module,
                ["{{SELECTED}}"] = selectedModules.Contains(module.Module) ? "Yes" : "No"
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
                return rowText.Contains("{{M_PRICING}}", StringComparison.Ordinal) &&
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

    private static void PopulatePricingTableFromTemplate(
        Body body,
        QuotationRequest request,
        IReadOnlyDictionary<string, ModuleItem> modulePrices,
        List<QuotationModulePricing> pricing,
        decimal subtotal,
        decimal implementationPriceTotal,
        decimal modulePriceTotal,
        decimal discountAmount,
        decimal overallFinalPrice)
    {
        const string licenseRenewalText = "The License renewal would be required to be done every Year These renewal fees will facilitate to have the Product Upgrades, which would cover improvements, bug fixes, and changes in AIAG VDA compliances. Support of 7 Man days is included in this price.";

        // Find the first template row (Product License row)
        var allRows = body.Descendants<TableRow>().ToList();
        var templateRowIndex = -1;

        for (int i = 0; i < allRows.Count; i++)
        {
            var rowText = string.Concat(allRows[i].Descendants<Text>().Select(t => t.Text));
            if (rowText.Contains("{{NO_OF_USERS}}", StringComparison.Ordinal) &&
                rowText.Contains("{{M_PRICE}}", StringComparison.Ordinal))
            {
                templateRowIndex = i;
                break;
            }
        }

        if (templateRowIndex == -1) return;

        const int maxTemplateBlockRows = 5;
        var templateRows = new List<TableRow>();
        var offset = 0;
        while (templateRows.Count < maxTemplateBlockRows && templateRowIndex + offset < allRows.Count)
        {
            var row = allRows[templateRowIndex + offset];
            var rowText = string.Concat(row.Descendants<Text>().Select(t => t.Text));
            var isCustomizationTbdRow =
                rowText.Contains("Customization", StringComparison.OrdinalIgnoreCase) &&
                rowText.Contains("TBD", StringComparison.OrdinalIgnoreCase);
            var isOverallValueRow =
                rowText.Contains("{{OVERALL_VALUE}}", StringComparison.Ordinal);
            var isAdditionalScopeSubtotalRow =
                rowText.Contains("Additional Scope:", StringComparison.OrdinalIgnoreCase);
            if (isCustomizationTbdRow || isOverallValueRow || isAdditionalScopeSubtotalRow)
            {
                break;
            }

            templateRows.Add(row);
            offset++;
        }

        if (templateRows.Count == 0) return;

        // Inject {{M_FINAL}} placeholder into the template row that contains {{M_SUBTOTAL}}
        // Replace {{M_SUBTOTAL}} with {{M_FINAL}} when there's a discount, otherwise keep subtotal
        foreach (var templateRow in templateRows)
        {
            foreach (var cell in templateRow.Elements<TableCell>())
            {
                var cellText = string.Concat(cell.Descendants<Text>().Select(t => t.Text));
                if (cellText.Contains("{{M_SUBTOTAL}}", StringComparison.Ordinal))
                {
                    // Replace {{M_SUBTOTAL}} with {{M_FINAL}} in the cell text
                    foreach (var text in cell.Descendants<Text>())
                    {
                        if (text.Text.Contains("{{M_SUBTOTAL}}", StringComparison.Ordinal))
                        {
                            text.Text = text.Text.Replace("{{M_SUBTOTAL}}", "{{M_FINAL}}");
                        }
                    }
                    break;
                }
            }
        }

        var moduleNames = request.SelectedModules.ToList();
        int rowNum = 1;

        // Get the parent table for insertion (once, before the loop)
        var parentTable = templateRows[0].Ancestors<Table>().FirstOrDefault();
        if (parentTable == null) return;

        var pricingByModule = pricing.ToDictionary(p => p.ModuleName.Trim(), StringComparer.OrdinalIgnoreCase);

        foreach (var moduleName in moduleNames)
        {
            modulePrices.TryGetValue(moduleName, out var module);
            var detail = request.ModuleDetails?.FirstOrDefault(d => string.Equals(d.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase));
            pricingByModule.TryGetValue(moduleName.Trim(), out var modulePricing);

            var modulePrice = modulePricing?.ModulePrice ?? module?.Price ?? 0m;
            var implementationEffort =
                modulePricing?.ImplementationMultiplier ??
                GetEffortMultiplier(detail?.ImplementationEffortUnit);
            var implementationRate =
                modulePricing?.ImplementationUnitPrice ??
                module?.ImplementationEffortCost ??
                0m;
            var noOfUsers = detail?.NoOfUsers ?? 0;
            var implementationTotal = modulePricing?.ImplementationPrice ??
                implementationEffort * implementationRate;
            var moduleSubtotal = modulePricing?.ModuleSubtotal ??
                modulePrice + implementationTotal;
            var moduleDiscountPct = modulePricing?.DiscountPercentage ?? 0m;
            var moduleDiscount = modulePricing?.DiscountAmount ??
                modulePrice * moduleDiscountPct / 100m;
            var moduleFinalPrice = modulePricing?.FinalPrice ??
                moduleSubtotal - moduleDiscount;

            var moduleReplacements = new Dictionary<string, string>
            {
                ["{{M_NAME}}"] = moduleName,
                ["{{LICENSE_RENEWAL}}"] = licenseRenewalText,
                ["{{NO_OF_USERS}}"] = noOfUsers.ToString(),
                ["{{M_PRICE}}"] = $"{modulePrice:N2}",
                ["{{IMPL_EFFORT}}"] = implementationEffort.ToString("0.##", CultureInfo.InvariantCulture),
                ["{{IMPL_RATE}}"] = $"{implementationRate:N2}",
                ["{{IMPL_TOTAL}}"] = $"{implementationTotal:N2}",
                ["{{M_SUBTOTAL}}"] = $"{moduleSubtotal:N2}",
                ["{{M_DISCOUNT_PCT}}"] = moduleDiscountPct > 0 ? $"{moduleDiscountPct:N2}" : string.Empty,
                ["{{M_DISCOUNT}}"] = moduleDiscountPct > 0 ? $"{moduleDiscount:N2}" : string.Empty,
                ["{{M_FINAL}}"] = $"{moduleFinalPrice:N2}"
            };

            // Clone all 3 rows for this module
            foreach (var templateRow in templateRows)
            {
                var clonedRow = (TableRow)templateRow.CloneNode(true);
                if (rowNum > 1 || templateRow != templateRows[0])
                {
                    var serialNumberCell = clonedRow.Elements<TableCell>().FirstOrDefault();
                    var serialNumber = serialNumberCell is null
                        ? string.Empty
                        : string.Concat(serialNumberCell.Descendants<Text>().Select(t => t.Text)).Trim();

                    if (serialNumberCell is not null && (serialNumber is "1" or "1.1"))
                    {
                        foreach (var text in serialNumberCell.Descendants<Text>())
                        {
                            text.Text = string.Empty;
                        }
                    }
                }

                ReplaceRowPlaceholders(clonedRow, moduleReplacements);
                templateRows[0].InsertBeforeSelf(clonedRow);
            }

            rowNum++;
        }

        // Remove the template block
        foreach (var templateRow in templateRows)
        {
            templateRow.Remove();
        }

        // If no discount, remove only the discount-specific rows (not rows that also contain module info)
        var anyModuleHasDiscount = pricing.Any(p => p.DiscountPercentage > 0);
        if (!anyModuleHasDiscount && parentTable != null)
        {
            var discountRowsToRemove = parentTable.Elements<TableRow>()
                .Where(row =>
                {
                    var rowText = string.Concat(row.Descendants<Text>().Select(t => t.Text));
                    // Only remove rows that are primarily discount rows (contain Discount but NOT module info)
                    var hasDiscount = rowText.Contains("Discount", StringComparison.OrdinalIgnoreCase);
                    var hasModuleInfo = rowText.Contains("M_NAME", StringComparison.OrdinalIgnoreCase) ||
                                        rowText.Contains("NO_OF_USERS", StringComparison.OrdinalIgnoreCase) ||
                                        rowText.Contains("M_PRICE", StringComparison.OrdinalIgnoreCase) ||
                                        rowText.Contains("M_SUBTOTAL", StringComparison.OrdinalIgnoreCase) ||
                                        rowText.Contains("IMPL_TOTAL", StringComparison.OrdinalIgnoreCase) ||
                                        rowText.Contains("IMPL_RATE", StringComparison.OrdinalIgnoreCase) ||
                                        rowText.Contains("IMPL_EFFORT", StringComparison.OrdinalIgnoreCase) ||
                                        rowText.Contains("LICENSE_RENEWAL", StringComparison.OrdinalIgnoreCase);
                    return hasDiscount && !hasModuleInfo;
                })
                .ToList();
            foreach (var row in discountRowsToRemove)
            {
                row.Remove();
            }
        }

        var overallValueRow = body
            .Descendants<TableRow>()
            .FirstOrDefault(row =>
            {
                var rowText = string.Concat(row.Descendants<Text>().Select(t => t.Text));
                return rowText.Contains("{{OVERALL_VALUE}}", StringComparison.Ordinal);
            });

        if (overallValueRow != null)
        {

            var overallValueReplacements = new Dictionary<string, string>
            {
                ["{{OVERALL_VALUE}}"] = $"{overallFinalPrice:N2}"
            };
            ReplaceRowPlaceholders(overallValueRow, overallValueReplacements);
        }

        // Handle overall summary rows - find rows with static labels and {{OVERALL_VALUE}} placeholder
        var overallSummaryLabels = new Dictionary<string, string>
        {
            ["Module Price:"] = $"{modulePriceTotal:N2}",
            ["Implementation Total:"] = $"{implementationPriceTotal:N2}",
            ["Subtotal:"] = $"{subtotal:N2}"
        };

        var overallDiscountPct = subtotal > 0 ? (discountAmount / subtotal) * 100m : 0m;
        if (anyModuleHasDiscount && discountAmount > 0)
        {
            overallSummaryLabels[$"Discount ({overallDiscountPct:N2}%):"] = $"{discountAmount:N2}";
        }
        overallSummaryLabels["Total of All Modules Final Price:"] = $"{overallFinalPrice:N2}";

        var allRowsForSummary = body.Descendants<TableRow>().ToList();
        foreach (var row in allRowsForSummary)
        {
            var rowText = string.Concat(row.Descendants<Text>().Select(t => t.Text));
            foreach (var kvp in overallSummaryLabels)
            {
                if (rowText.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase) && rowText.Contains("{{OVERALL_VALUE}}", StringComparison.Ordinal))
                {
                    var replacements = new Dictionary<string, string>
                    {
                        ["{{OVERALL_VALUE}}"] = kvp.Value,
                        ["{{OVERALL_LABEL}}"] = string.Empty
                    };
                    ReplaceRowPlaceholders(row, replacements);
                    break;
                }
            }
        }

        // Remove any remaining template row with both placeholders
        var overallTemplateRow = body
            .Descendants<TableRow>()
            .FirstOrDefault(row =>
            {
                var rowText = string.Concat(row.Descendants<Text>().Select(t => t.Text));
                return rowText.Contains("{{OVERALL_LABEL}}", StringComparison.Ordinal) &&
                       rowText.Contains("{{OVERALL_VALUE}}", StringComparison.Ordinal);
            });

        if (overallTemplateRow != null)
        {
            overallTemplateRow.Remove();
        }
    }

    private static void PopulateLicenseRenewalModuleRows(
        Body body,
        IEnumerable<string> selectedModules)
    {
        var moduleNames = selectedModules?.ToList() ?? new List<string>();
        if (moduleNames.Count == 0) return;

        var templateRow = body
            .Descendants<TableRow>()
            .FirstOrDefault(row =>
            {
                var rowText = string.Concat(row.Descendants<Text>().Select(t => t.Text));
                return rowText.Contains("{{M_NAME}}", StringComparison.Ordinal);
            });

        if (templateRow is null) return;

        foreach (var moduleName in moduleNames)
        {
            var clonedRow = (TableRow)templateRow.CloneNode(true);
            var rowReplacements = new Dictionary<string, string>
            {
                ["{{M_NAME}}"] = moduleName
            };
            foreach (var paragraph in clonedRow.Descendants<Paragraph>())
            {
                ReplaceParagraphText(paragraph, rowReplacements);
            }
            templateRow.InsertBeforeSelf(clonedRow);
        }

        templateRow.Remove();
    }

    private static void ReplaceRowPlaceholders(TableRow row, Dictionary<string, string> replacements)
    {
        foreach (var paragraph in row.Descendants<Paragraph>())
        {
            ReplaceParagraphText(paragraph, replacements);
        }
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
                "The License renewal",
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

    private static void PopulateAdditionalScopeTable(
        Body body,
        IEnumerable<AdditionalScopeRequest> additionalScopes)
    {
        var scopes = additionalScopes?.ToList() ?? new List<AdditionalScopeRequest>();

        var templateRow = body
            .Descendants<TableRow>()
            .FirstOrDefault(row =>
            {
                var rowText = string.Concat(row.Descendants<Text>().Select(text => text.Text));
                return rowText.Contains("{{A_S_REQ}}", StringComparison.Ordinal) &&
                       rowText.Contains("{{A_S_MOD}}", StringComparison.Ordinal) &&
                       rowText.Contains("{{A_S_MP}}", StringComparison.Ordinal) &&
                       rowText.Contains("{{A_S_DAYS}}", StringComparison.Ordinal) &&
                       rowText.Contains("{{A_S_RATE}}", StringComparison.Ordinal) &&
                       rowText.Contains("{{A_S_AMT}}", StringComparison.Ordinal);
            });

        if (templateRow is null) return;

        var table = templateRow.Ancestors<Table>().FirstOrDefault();

        if (scopes.Count == 0)
        {
            // Remove the entire Additional Scope table when no data (including header row)
            table?.Remove();

            // Also remove the "Additional Scope:" heading paragraph if present
            var heading = body
                .Descendants<Paragraph>()
                .FirstOrDefault(p => p.InnerText.Trim().Equals("Additional Scope:", StringComparison.OrdinalIgnoreCase));
            heading?.Remove();

            return;
        }

        for (var index = 0; index < scopes.Count; index++)
        {
            var scope = scopes[index];
            var row = (TableRow)templateRow.CloneNode(true);
            var rowReplacements = new Dictionary<string, string>
            {
                ["{{A_S_REQ}}"] = scope.Requirement ?? string.Empty,
                ["{{A_S_MOD}}"] = scope.Modules ?? string.Empty,
                ["{{A_S_MP}}"] = scope.NoOfManpower.ToString(),
                ["{{A_S_DAYS}}"] = scope.NoOfDays.ToString(),
                ["{{A_S_RATE}}"] = scope.Rate.ToString("N2"),
                ["{{A_S_AMT}}"] = CalculateAdditionalScopeAmount(scope).ToString("N2")
            };
            foreach (var paragraph in row.Descendants<Paragraph>())
            {
                ReplaceParagraphText(paragraph, rowReplacements);
            }
            templateRow.InsertBeforeSelf(row);
        }

        templateRow.Remove();
    }

    private static decimal CalculateAdditionalScopeAmount(
        AdditionalScopeRequest scope) =>
        scope.NoOfManpower * scope.NoOfDays * scope.Rate;

    private static void RemoveAdditionalScopeSubtotalRowWhenEmpty(
        Body body,
        IEnumerable<AdditionalScopeRequest> additionalScopes)
    {
        var hasAdditionalScopes = additionalScopes?.Any() == true;
        if (hasAdditionalScopes) return;

        var subtotalRow = body
            .Descendants<TableRow>()
            .FirstOrDefault(row =>
                row.Elements<TableCell>().Any(cell =>
                    cell.InnerText.Trim().Equals(
                        "Additional Scope:",
                        StringComparison.OrdinalIgnoreCase)));

        if (subtotalRow is null) return;
        subtotalRow.Remove();
    }

    private static void PopulatePriceSummaryTable(
        Body body,
        IEnumerable<string> selectedModules,
        IEnumerable<QuotationModuleRequest>? moduleDetails,
        IReadOnlyDictionary<string, ModuleItem> modulePrices,
        decimal renewalPercentage = 20m,
        decimal annualEscalationPercentage = 7m)
    {
        var moduleNames = selectedModules?.ToList() ?? new List<string>();
        if (moduleNames.Count == 0) return;

        var detailsByModule = (moduleDetails ?? Enumerable.Empty<QuotationModuleRequest>())
            .ToDictionary(d => d.ModuleName.Trim(), StringComparer.OrdinalIgnoreCase);

        // Find the price summary table by looking for the header row
        var priceSummaryTable = body
            .Descendants<Table>()
            .FirstOrDefault(table =>
            {
                var headerRow = table.Elements<TableRow>().FirstOrDefault();
                if (headerRow == null) return false;
                var headerText = string.Concat(headerRow.Descendants<Text>().Select(t => t.Text));
                return headerText.Contains("CQUAL Product Platform", StringComparison.OrdinalIgnoreCase) &&
                       headerText.Contains("No of Users", StringComparison.OrdinalIgnoreCase) &&
                       headerText.Contains("Y1", StringComparison.OrdinalIgnoreCase) &&
                       headerText.Contains("Y2", StringComparison.OrdinalIgnoreCase) &&
                       headerText.Contains("Y3", StringComparison.OrdinalIgnoreCase) &&
                       headerText.Contains("Y4", StringComparison.OrdinalIgnoreCase) &&
                       headerText.Contains("Y5", StringComparison.OrdinalIgnoreCase) &&
                       headerText.Contains("Avg Cost/ Year", StringComparison.OrdinalIgnoreCase) &&
                       headerText.Contains("Cost/User /Year", StringComparison.OrdinalIgnoreCase);
            });

        if (priceSummaryTable == null) return;

        // Get all rows in the table
        var rows = priceSummaryTable.Elements<TableRow>().ToList();
        if (rows.Count < 2) return; // Need at least header + 1 data row

        // The template has header row (index 0) and 3 placeholder data rows (index 1, 2, 3)
        // We'll use the first data row as template and clone for each module
        var templateRow = rows[1]; // First data row after header

        // Remove existing placeholder data rows EXCEPT the template row (keep rows[1] as template)
        for (int i = rows.Count - 1; i >= 2; i--)
        {
            rows[i].Remove();
        }

        // Use defaults if renewal or annual escalation percentages are not provided.
        var effectiveRenewalPercentage = renewalPercentage > 0 ? renewalPercentage : 20m;
        var effectiveEscalationPercentage = annualEscalationPercentage > 0 ? annualEscalationPercentage : 7m;

        // Convert percentages to decimal rates (e.g., 20 -> 0.20, 7 -> 0.07).
        var renewalRate = effectiveRenewalPercentage / 100m;
        var escalationRate = effectiveEscalationPercentage / 100m;

        foreach (var moduleName in moduleNames)
        {
            modulePrices.TryGetValue(moduleName, out var module);
            detailsByModule.TryGetValue(moduleName.Trim(), out var detail);

            var modulePrice = module?.Price ?? 0m;
            var noOfUsers = detail?.NoOfUsers ?? 1;

            // Y1 shows the full module price. Y2 starts with the renewal percentage,
            // without adding the Y1 module price; later years escalate the renewal.
            var y1 = modulePrice;
            var y2 = modulePrice * renewalRate;
            // Y3 = Y2 + AnnualEscalationPercentage% increment (3rd year).
            var y3 = y2 * (1 + escalationRate);
            // Y4 = Y3 + AnnualEscalationPercentage% increment (4th year).
            var y4 = y3 * (1 + escalationRate);
            // Y5 = Y4 + AnnualEscalationPercentage% increment (5th year).
            var y5 = y4 * (1 + escalationRate);

            // Avg Cost/Year = Average of Y1-Y5
            var avgCostPerYear = (y1 + y2 + y3 + y4 + y5) / 5m;

            // Cost/User/Year = Avg Cost/Year / No of Users
            var costPerUserPerYear = noOfUsers > 0 ? avgCostPerYear / noOfUsers : 0m;

            var clonedRow = (TableRow)templateRow.CloneNode(true);

            // Keep each data cell's own formatting rather than borrowing the bold header style.
            var cells = clonedRow.Elements<TableCell>().ToList();
            if (cells.Count >= 9)
            {
                var numericFontProperties = cells[2]
                    .Descendants<RunProperties>()
                    .FirstOrDefault();

                // Cell 0: Product Platform - Format as "CQUAL {{M_NAME}}"
                var moduleDisplayName = $"CQUAL {moduleName}";
                ReplaceCellTextPreservingFormat(cells[0], moduleDisplayName);

                // Cell 1: No of Users
                ReplaceCellTextPreservingFormat(cells[1], noOfUsers.ToString());
                // Cell 2: Y1
                ReplaceCellTextPreservingFormat(cells[2], $"{y1:N2}");
                // Cell 3: Y2
                ReplaceCellTextPreservingFormat(cells[3], $"{y2:N2}");
                // Cell 4: Y3
                ReplaceCellTextPreservingFormat(cells[4], $"{y3:N2}", numericFontProperties);
                // Cell 5: Y4
                ReplaceCellTextPreservingFormat(cells[5], $"{y4:N2}", numericFontProperties);
                // Cell 6: Y5
                ReplaceCellTextPreservingFormat(cells[6], $"{y5:N2}", numericFontProperties);
                // Cell 7: Avg Cost/Year
                ReplaceCellTextPreservingFormat(cells[7], $"{avgCostPerYear:N2}", numericFontProperties);
                // Cell 8: Cost/User/Year
                ReplaceCellTextPreservingFormat(cells[8], $"{costPerUserPerYear:N2}", numericFontProperties);
            }

            // Insert before the template row (which will be removed after loop)
            templateRow.InsertBeforeSelf(clonedRow);
        }

        // Remove the template row
        templateRow.Remove();
    }

    private static void ReplaceCellTextPreservingFormat(
        TableCell cell,
        string newText,
        RunProperties? fontProperties = null)
    {
        // Replace existing text in the cell so its own formatting remains intact.
        foreach (var paragraph in cell.Elements<Paragraph>())
        {
            foreach (var run in paragraph.Elements<Run>())
            {
                foreach (var text in run.Elements<Text>())
                {
                    text.Text = newText;
                    return;
                }
            }
        }

        // Empty cells still inherit their paragraph and cell formatting from the cloned row.
        var firstParagraph = cell.Elements<Paragraph>().FirstOrDefault();
        if (firstParagraph == null)
        {
            firstParagraph = new Paragraph();
            cell.AppendChild(firstParagraph);
        }

        var newRun = new Run(new Text(newText));
        if (fontProperties is not null)
        {
            var copiedFontProperties = new RunProperties();
            foreach (var property in fontProperties.ChildElements.Where(property =>
                         property is RunFonts or FontSize or FontSizeComplexScript))
            {
                copiedFontProperties.AppendChild(property.CloneNode(true));
            }
            newRun.RunProperties = copiedFontProperties;
        }

        firstParagraph.AppendChild(newRun);
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
            var nodeIndexes = new int[originalText.Length];
            var offset = 0;
            for (var nodeIndex = 0; nodeIndex < textGroup.Count; nodeIndex++)
            {
                var nodeLength = textGroup[nodeIndex].Text?.Length ?? 0;
                Array.Fill(nodeIndexes, nodeIndex, offset, nodeLength);
                offset += nodeLength;
            }

            var outputByNode = textGroup
                .Select(_ => new System.Text.StringBuilder())
                .ToList();
            for (var index = 0; index < originalText.Length;)
            {
                var matchingReplacement = replacements.FirstOrDefault(replacement =>
                    !string.IsNullOrEmpty(replacement.Key) &&
                    index + replacement.Key.Length <= originalText.Length &&
                    string.CompareOrdinal(
                        originalText,
                        index,
                        replacement.Key,
                        0,
                        replacement.Key.Length) == 0);

                if (!string.IsNullOrEmpty(matchingReplacement.Key))
                {
                    outputByNode[nodeIndexes[index]].Append(matchingReplacement.Value);
                    index += matchingReplacement.Key.Length;
                    continue;
                }

                outputByNode[nodeIndexes[index]].Append(originalText[index]);
                index++;
            }

            for (var nodeIndex = 0; nodeIndex < textGroup.Count; nodeIndex++)
                textGroup[nodeIndex].Text = outputByNode[nodeIndex].ToString();

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