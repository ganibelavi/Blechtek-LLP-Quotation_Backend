using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.Logging;
using System.IO.Compression;
using Microsoft.Extensions.Options;
using QuotationApp.API.Models;

namespace QuotationApp.API.Services;

/// <summary>
/// Fills the fixed company template (Templates/QuotationTemplate.docx) with the dynamic
/// fields from a QuotationRequest, and trims the "Scope" table down to only the modules
/// the user selected. Everything else in the template - letterhead, terms & conditions,
/// wording, structure - is left completely untouched.
/// </summary>
public class WordGeneratorService : IWordGeneratorService
{
    private readonly ILogger<WordGeneratorService> _logger;
    private const string ModRowPrefix = "MODROW:";

    private readonly string _templatePath;
    private readonly string _outputFolder;
    private readonly string _contentRoot;
    private readonly IModuleService _moduleService;

    public WordGeneratorService(
        IOptions<QuotationSettings> settings,
        IWebHostEnvironment env,
        ILogger<WordGeneratorService> logger,
        IModuleService moduleService)
    {
        _templatePath = Path.Combine(env.ContentRootPath, settings.Value.TemplatePath);
        _outputFolder = Path.Combine(env.ContentRootPath, settings.Value.OutputFolder);
        _contentRoot = env.ContentRootPath;
        _logger = logger;
        _moduleService = moduleService;
        Directory.CreateDirectory(_outputFolder);
    }

    private bool TryRepairPackage(string templatePath)
    {
        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "quote_template_repair_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            ZipFile.ExtractToDirectory(templatePath, tempDir);

            var docXml = Path.Combine(tempDir, "word", "document.xml");
            if (!File.Exists(docXml))
            {
                Directory.Delete(tempDir, true);
                return false;
            }

            var tempZip = Path.Combine(Path.GetTempPath(), "repaired_" + Guid.NewGuid().ToString("N") + ".zip");
            if (File.Exists(tempZip)) File.Delete(tempZip);

            ZipFile.CreateFromDirectory(tempDir, tempZip, CompressionLevel.Optimal, includeBaseDirectory: false);

            // Validate the repaired package
            try
            {
                using var doc = WordprocessingDocument.Open(tempZip, isEditable: false);
                if (doc.MainDocumentPart?.Document?.Body is null)
                {
                    File.Delete(tempZip);
                    Directory.Delete(tempDir, true);
                    return false;
                }
            }
            catch
            {
                File.Delete(tempZip);
                Directory.Delete(tempDir, true);
                return false;
            }

            // Replace original with repaired package
            File.Copy(tempZip, templatePath, overwrite: true);
            File.Delete(tempZip);
            Directory.Delete(tempDir, true);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Automatic template repair failed for {Path}", templatePath);
            return false;
        }
    }

    public async Task<string> GenerateAsync(QuotationRequest request, string quotationId)
    {
        if (!File.Exists(_templatePath))
            throw new FileNotFoundException($"Quotation template not found at '{_templatePath}'.");

        // Validate the SOURCE template before doing anything else. This catches a
        // corrupted template immediately, with an error that points at the template
        // file itself rather than the generated output file.
        ValidateTemplate(_templatePath);

        var outputPath = Path.Combine(_outputFolder, $"{quotationId}.docx");
        File.Copy(_templatePath, outputPath, overwrite: true);

        var selectedNames = new HashSet<string>(request.SelectedModules, StringComparer.OrdinalIgnoreCase);
        var selectedModules = (await _moduleService.GetModulesAsync())
            .Where(module => selectedNames.Contains(module.ModuleName))
            .ToList();

        // OpenXML SDK APIs are synchronous; run on a background thread so the controller stays async.
        await Task.Run(() => FillTemplate(outputPath, request, selectedModules));

        return outputPath;
    }

    private void ValidateTemplate(string templatePath)
    {
        var fileInfo = new FileInfo(templatePath);
        _logger.LogInformation("Validating template at {Path} ({Size} bytes)", templatePath, fileInfo.Length);

        if (fileInfo.Length == 0)
            throw new InvalidOperationException(
                $"Quotation template at '{templatePath}' is empty (0 bytes). Replace it with a valid .docx file.");

        try
        {
            using var doc = WordprocessingDocument.Open(templatePath, isEditable: false);

            if (doc.MainDocumentPart?.Document?.Body is null)
            {
                _logger.LogWarning("Template at {Path} has no document body — attempting automatic repackage repair.", templatePath);

                // Try to repair by repackaging the .docx contents and retrying. This can fix packages
                // that were created/updated by tooling that produced a slightly different ZIP layout.
                if (TryRepairPackage(templatePath))
                {
                    // Re-open and verify
                    using var repaired = WordprocessingDocument.Open(templatePath, isEditable: false);
                    if (repaired.MainDocumentPart?.Document?.Body is not null)
                    {
                        _logger.LogInformation("Template at {Path} repaired successfully.", templatePath);
                        return;
                    }
                }

                _logger.LogError("Template at {Path} has no document body — the .docx package is corrupted.", templatePath);
                throw new InvalidOperationException(
                    $"Quotation template at '{templatePath}' is corrupted (missing document body). " +
                    "Open it in Word, use File > Save As to write a fresh copy, and replace the file. " +
                    "If this file is stored in git, make sure it is tracked as binary (see .gitattributes).");
            }
        }
        catch (InvalidOperationException)
        {
            throw; // already a clear, actionable message — bubble it up as-is
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Template at {Path} could not be opened as a .docx package.", templatePath);
            throw new InvalidOperationException(
                $"Quotation template at '{templatePath}' could not be opened as a .docx file " +
                "(it may not be a valid Word document, or it's corrupted). " +
                "Re-save it in Word and replace the file.", ex);
        }
    }

    private void FillTemplate(string path, QuotationRequest request, List<ModuleItem> selectedModules)
    {
        _logger.LogInformation("Opening generated file: {Path}", path);

        if (!File.Exists(path))
        {
            _logger.LogError("Generated file does not exist at path: {Path}", path);
            throw new FileNotFoundException($"Generated file not found at '{path}'.");
        }

        try
        {
            using var doc = WordprocessingDocument.Open(path, isEditable: true);

            var body = doc.MainDocumentPart?.Document?.Body
                ?? throw new InvalidOperationException(
                    $"Generated file at '{path}' has no document body. This should not happen " +
                    "if the source template passed validation — check for a race condition or a " +
                    "concurrent write to this file.");

            var moduleList = request.SelectedModules.Count == 1
                ? request.SelectedModules[0]
                : string.Join(", ", request.SelectedModules.Take(request.SelectedModules.Count - 1))
                  + " & " + request.SelectedModules[^1];

            // Calculate total module price and apply discount
            var totalPrice = selectedModules.Sum(m => m.Price ?? 0);
            var discountPercentage = request.DiscountPercentage;
            var discountAmount = totalPrice * discountPercentage / 100;
            var finalPrice = totalPrice - discountAmount;

            var replacements = new Dictionary<string, string>
            {
                ["{{CONTACT_NAME}}"] = request.QuotationTo.Name,
                ["{{CONTACT_ADDRESS}}"] = request.QuotationTo.Address,
                ["{{CONTACT_PHONE}}"] = request.QuotationTo.ContactNo,
                ["{{CONTACT_EMAIL}}"] = request.QuotationTo.Email,
                ["{{ORG_NAME}}"] = request.OrganizationName,
                ["{{MODULE_LIST}}"] = moduleList,
                ["{{VALIDATION_DATE}}"] = request.ValidationDate.ToString("dd MMM yyyy"),
                ["{{QUOTATION_NO}}"] = request.QuotationNo,
                ["{{DATE}}"] = request.Date.ToString("dd MMM yyyy"),
                ["{{TOTAL_MODULE_PRICE}}"] = totalPrice.ToString("N2"),
                ["{{DISCOUNT_PERCENTAGE}}"] = discountPercentage.ToString("N2"),
                ["{{DISCOUNT_AMOUNT}}"] = discountAmount.ToString("N2"),
                ["{{FINAL_PRICE}}"] = finalPrice.ToString("N2"),
            };

            FilterScopeTable(body, selectedModules);
            ReplacePlaceholders(body, replacements);

            // Attempt to replace the header text with a provided logo image (logo.png or logo.jpg)
            TryReplaceHeaderTextWithLogo(doc, "BlechTek Software Solutions LLP");

            doc.MainDocumentPart!.Document.Save();
            _logger.LogInformation("Template filling completed successfully for {Path}.", path);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "IO error while accessing file. Path: {Path}", path);
            throw new InvalidOperationException(
                $"Cannot access generated file. It might be locked or in use by another process. Path: {path}", ex);
        }
    }

    /// <summary>
    /// Keeps only selected module rows in the "Scope" table.
    /// It also removes the MODROW marker token and drops the hidden "Key" column if present.
    /// </summary>
    private static void FilterScopeTable(Body body, List<ModuleItem> selectedModules)
    {
        foreach (var table in body.Descendants<Table>().ToList())
        {
            var rows = table.Elements<TableRow>().ToList();

            // Detect scope table by searching the concatenated text of each row (handles run splits)
            var isScopeTable = rows.Any(r => string.Concat(r.Descendants<Text>().Select(t => t.Text)).Contains(ModRowPrefix, StringComparison.OrdinalIgnoreCase));
            if (!isScopeTable) continue;

            var keyIndex = -1;
            try
            {
                var headerRow = rows.FirstOrDefault(r => r.Elements<TableCell>()
                    .Any(tc => string.Concat(tc.Descendants<Text>().Select(t => t.Text)).Trim().Equals("Key", StringComparison.OrdinalIgnoreCase)));
                if (headerRow is not null)
                {
                    var headerCells = headerRow.Elements<TableCell>().ToList();
                    keyIndex = headerCells.FindIndex(tc => string.Concat(tc.Descendants<Text>().Select(t => t.Text)).Trim().Equals("Key", StringComparison.OrdinalIgnoreCase));
                }
            }
            catch
            {
                // swallow non-fatal errors
            }

            var templateRow = rows.FirstOrDefault(row =>
                string.Concat(row.Descendants<Text>().Select(text => text.Text))
                    .Contains(ModRowPrefix, StringComparison.OrdinalIgnoreCase));
            if (templateRow is null) continue;

            // Remove all template rows. The selected scope rows are rebuilt from the
            // live Modules master data, so newly-created modules also appear in quotes.
            foreach (var row in rows.Where(row =>
                         string.Concat(row.Descendants<Text>().Select(text => text.Text))
                             .Contains(ModRowPrefix, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                row.Remove();
            }

            if (keyIndex >= 0)
            {
                var tblGrid = table.Elements<TableGrid>().FirstOrDefault();
                if (tblGrid is not null)
                {
                    var gridCols = tblGrid.Elements<GridColumn>().ToList();
                    if (keyIndex < gridCols.Count)
                        gridCols[keyIndex].Remove();
                }
                // fasfae
                foreach (var r in table.Elements<TableRow>().ToList())
                {
                    var cells = r.Elements<TableCell>().ToList();
                    if (keyIndex < cells.Count)
                        cells[keyIndex].Remove();
                }
            }

            foreach (var module in selectedModules)
            {
                var selectedRow = (TableRow)templateRow.CloneNode(true);
                var cells = selectedRow.Elements<TableCell>().ToList();

                if (cells.Count > 0) SetCellText(cells[0], module.Pillar);
                if (cells.Count > 1) SetCellText(cells[1], module.ModuleName);
                if (cells.Count > 2) SetCellText(cells[2], "Yes");
                if (keyIndex >= 0 && keyIndex < cells.Count) cells[keyIndex].Remove();

                // Remove the internal marker if it is embedded in an unexpected layout.
                foreach (var text in selectedRow.Descendants<Text>()
                             .Where(text => text.Text.Contains(ModRowPrefix, StringComparison.OrdinalIgnoreCase)))
                    text.Text = string.Empty;

                table.Append(selectedRow);
            }
        }
    }

    private static void SetCellText(TableCell cell, string value)
    {
        var textNodes = cell.Descendants<Text>().ToList();
        if (textNodes.Count == 0)
        {
            cell.Append(new Paragraph(new Run(new Text(value))));
            return;
        }

        textNodes[0].Text = value;
        textNodes[0].Space = SpaceProcessingModeValues.Preserve;
        for (var index = 1; index < textNodes.Count; index++)
            textNodes[index].Text = string.Empty;
    }

    private static void ReplacePlaceholderInCell(TableCell cell, string placeholder, string replacement)
    {
        foreach (var paragraph in cell.Elements<Paragraph>())
        {
            var textNodes = paragraph.Descendants<Text>().ToList();
            if (textNodes.Count == 0) continue;

            var combined = string.Concat(textNodes.Select(t => t.Text));
            if (!combined.Contains(placeholder, StringComparison.OrdinalIgnoreCase)) continue;

            combined = combined.Replace(placeholder, replacement, StringComparison.OrdinalIgnoreCase);
            textNodes[0].Text = combined;
            textNodes[0].Space = SpaceProcessingModeValues.Preserve;
            for (var i = 1; i < textNodes.Count; i++)
                textNodes[i].Text = string.Empty;
        }
    }

    /// <summary>
    /// Replaces {{PLACEHOLDER}} tokens anywhere in the document. Word frequently splits a
    /// single visible phrase across multiple &lt;w:r&gt; runs (spell-check boundaries, etc.),
    /// so replacement is done per-paragraph: concatenate all Text nodes, substitute, then
    /// write the merged result back into the first node and clear the rest.
    /// </summary>
    private static void ReplacePlaceholders(Body body, Dictionary<string, string> replacements)
    {
        foreach (var paragraph in body.Descendants<Paragraph>())
        {
            var textNodes = paragraph.Descendants<Text>().ToList();
            if (textNodes.Count == 0) continue;

            var combined = string.Concat(textNodes.Select(t => t.Text));
            if (!replacements.Keys.Any(combined.Contains)) continue;

            foreach (var (placeholder, value) in replacements)
                combined = combined.Replace(placeholder, value);

            textNodes[0].Text = combined;
            textNodes[0].Space = SpaceProcessingModeValues.Preserve;
            for (var i = 1; i < textNodes.Count; i++)
                textNodes[i].Text = string.Empty;
        }
    }

    // Attempts to replace the specified header text with an inline image named `logo.png` or `logo.jpg`
    // located in the same folder as the template. This keeps the template file intact and allows
    // users to provide a logo image separately.
    private void TryReplaceHeaderTextWithLogo(WordprocessingDocument doc, string headerText)
    {
        try
        {
            var templatesDir = Path.GetDirectoryName(_templatePath) ?? string.Empty;
            var logoPathPng = Path.Combine(templatesDir, "logo.png");
            var logoPathJpg = Path.Combine(templatesDir, "logo.jpg");

            // Also check frontend/logo directory (project workspace sibling)
            var frontendLogoPng = Path.GetFullPath(Path.Combine(_contentRoot, "..", "..", "frontend", "logo", "logo.png"));
            var frontendLogoJpg = Path.GetFullPath(Path.Combine(_contentRoot, "..", "..", "frontend", "logo", "logo.jpg"));

            string? logoPath = null;
            if (File.Exists(logoPathPng)) logoPath = logoPathPng;
            else if (File.Exists(logoPathJpg)) logoPath = logoPathJpg;
            else if (File.Exists(frontendLogoPng)) logoPath = frontendLogoPng;
            else if (File.Exists(frontendLogoJpg)) logoPath = frontendLogoJpg;

            if (logoPath is null) return; // no logo provided in either location

            foreach (var headerPart in doc.MainDocumentPart!.HeaderParts)
            {
                var texts = headerPart.RootElement.Descendants<Text>().Where(t => !string.IsNullOrEmpty(t.Text) && t.Text.Contains(headerText)).ToList();
                if (!texts.Any()) continue;

                var imagePartType = logoPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                    ? ImagePartType.Png
                    : ImagePartType.Jpeg;

                var imagePart = headerPart.AddImagePart(imagePartType);
                using (var stream = File.OpenRead(logoPath))
                {
                    imagePart.FeedData(stream);
                }

                var rId = headerPart.GetIdOfPart(imagePart);

                const long pxToEmu = 9525;
                const int desiredWidthPx = 150;
                var cx = desiredWidthPx * pxToEmu;
                // Decrease height to 40px to make logo slimmer in header
                var cy = 40 * pxToEmu;

                var drawing = CreateImageDrawing(rId, cx, cy);

                foreach (var text in texts)
                {
                    var run = text.Ancestors<Run>().FirstOrDefault();
                    if (run == null) continue;

                    foreach (var t in run.Descendants<Text>().ToList())
                        t.Remove();

                    var drawingRun = new Run(drawing);
                    run.AppendChild(drawingRun);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to replace header text with logo image.");
        }
    }

    private static Drawing CreateImageDrawing(string relationshipId, long cx, long cy)
    {
        return new Drawing(
            new DocumentFormat.OpenXml.Drawing.Wordprocessing.Inline(
                new DocumentFormat.OpenXml.Drawing.Wordprocessing.Extent() { Cx = cx, Cy = cy },
                new DocumentFormat.OpenXml.Drawing.Wordprocessing.EffectExtent() { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                new DocumentFormat.OpenXml.Drawing.Wordprocessing.DocProperties() { Id = (UInt32Value)1U, Name = "Logo" },
                new DocumentFormat.OpenXml.Drawing.Wordprocessing.NonVisualGraphicFrameDrawingProperties(
                    new DocumentFormat.OpenXml.Drawing.GraphicFrameLocks() { NoChangeAspect = true }),
                new DocumentFormat.OpenXml.Drawing.Graphic(
                    new DocumentFormat.OpenXml.Drawing.GraphicData(
                        new DocumentFormat.OpenXml.Drawing.Pictures.Picture(
                            new DocumentFormat.OpenXml.Drawing.Pictures.NonVisualPictureProperties(
                                new DocumentFormat.OpenXml.Drawing.Pictures.NonVisualDrawingProperties() { Id = (UInt32Value)0U, Name = "Logo" },
                                new DocumentFormat.OpenXml.Drawing.Pictures.NonVisualPictureDrawingProperties()),
                            new DocumentFormat.OpenXml.Drawing.Pictures.BlipFill(
                                new DocumentFormat.OpenXml.Drawing.Blip() { Embed = relationshipId },
                                new DocumentFormat.OpenXml.Drawing.Stretch(new DocumentFormat.OpenXml.Drawing.FillRectangle())),
                            new DocumentFormat.OpenXml.Drawing.Pictures.ShapeProperties(
                                new DocumentFormat.OpenXml.Drawing.Transform2D(
                                    new DocumentFormat.OpenXml.Drawing.Offset() { X = 0L, Y = 0L },
                                    new DocumentFormat.OpenXml.Drawing.Extents() { Cx = cx, Cy = cy }),
                                new DocumentFormat.OpenXml.Drawing.PresetGeometry(new DocumentFormat.OpenXml.Drawing.AdjustValueList()) { Preset = DocumentFormat.OpenXml.Drawing.ShapeTypeValues.Rectangle })
                        )
                    )
                    { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }
                )
            )
        );
    }
}
