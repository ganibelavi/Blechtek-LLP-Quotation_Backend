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
        var financialYear = _settings.FinancialYear;
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
            .Select(g =>
            {
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
                return new MonthlyQuoteData
                {
                    Month = new DateTime(g.Key.Year, g.Key.Month, 1).ToString("MMM yyyy"),
                    Count = g.Count(),
                    Revenue = revenue
                };
            })
            .OrderBy(m => DateTime.ParseExact(m.Month, "MMM yyyy", System.Globalization.CultureInfo.InvariantCulture))
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
            .Take(8)
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

        // Machine utilization fallback: derive from the most-used modules so the chart
        // remains populated even when no dedicated machine dataset exists.
        var maxModuleCount = moduleDistribution.Any() ? moduleDistribution.Max(m => m.Count) : 0;
        var machineUtilization = moduleDistribution
            .Select(m => new MachineUtilizationData
            {
                Machine = m.Module,
                Utilization = maxModuleCount > 0 ? (int)Math.Round((m.Count * 100m) / maxModuleCount) : 0
            })
            .Take(8)
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

        // Build request from stored data with updated validation date and modules
        var request = new QuotationRequest
        {
            ValidationDate = validationDate,
            OrganizationName = quotation.OrganizationName,
            ReferenceBy = quotation.ReferenceBy ?? string.Empty,
            QuotationNo = quotation.QuotationNo ?? string.Empty,
            Date = quotation.Date ?? DateTime.UtcNow,
            SelectedModules = selectedModules,
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
                var modulePrices = modules.ToDictionary(m => m.Module, m => m.Price ?? 0m);

                var totalPrice = request.SelectedModules.Sum(m => modulePrices.GetValueOrDefault(m, 0m));
                var discountPercentage = request.DiscountPercentage > 0 ? request.DiscountPercentage : 0m;
                var discountAmount = totalPrice * discountPercentage / 100m;
                var finalPrice = totalPrice - discountAmount;

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
                    // Template placeholders (from temp_template)
                    ["{{CONTACT_NAME}}"] = request.QuotationTo?.Name ?? "",
                    ["{{CONTACT_ADDRESS}}"] = request.QuotationTo?.Address ?? "",
                    ["{{CONTACT_PHONE}}"] = request.QuotationTo?.ContactNo ?? "",
                    ["{{CONTACT_EMAIL}}"] = request.QuotationTo?.Email ?? "",
                    ["{{ORG_NAME}}"] = request.OrganizationName ?? "",
                    ["{{REQUIRED}}"] = string.Join(", ", request.SelectedModules),
                    ["{{VALIDATION_DATE}}"] = request.ValidationDate.ToString("dd/MM/yyyy"),
                    ["{{TotalPrice}}"] = totalPrice.ToString("N2"),
                    ["{{DiscountPercentage}}"] = discountPercentage.ToString("N2"),
                    ["{{DiscountAmount}}"] = discountAmount.ToString("N2"),
                    ["{{FinalPrice}}"] = finalPrice.ToString("N2")
                };

                PopulateScopeTable(body, modules, request.SelectedModules);

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