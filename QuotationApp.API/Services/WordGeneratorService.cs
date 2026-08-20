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
            RebuildQuotationToSection(body, request);
            RebuildPriceForImplementationTable(body, request, selectedModules, totalPrice, discountPercentage, discountAmount, finalPrice, moduleList);
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

private static void RebuildQuotationToSection(Body body, QuotationRequest request)
    {
        // Find the "QUOTATION TO" heading paragraph
        var heading = body.Descendants<Paragraph>()
            .FirstOrDefault(p => string.Concat(p.Descendants<Text>().Select(t => t.Text))
                .Contains("QUOTATION TO", StringComparison.OrdinalIgnoreCase));

        if (heading is null) return;

        // Find all paragraphs after the heading until the next heading or table
        var elementsToRemove = new List<OpenXmlElement>();
        var elementsAfterHeading = heading.ElementsAfter().ToList();
        foreach (var elem in elementsAfterHeading)
        {
            if (elem is Paragraph p)
            {
                var text = string.Concat(p.Descendants<Text>().Select(t => t.Text)).Trim();
                if (text.StartsWith("QUOTATION TO") || text.StartsWith("Reference:") || text.StartsWith("Subject:") || text.StartsWith("Dear"))
                {
                    break; // Stop at next section
                }
                // Remove the old paragraphs (Name:, Address:, Contact No.: | Email:)
                if (text.StartsWith("Name:") || text.StartsWith("Address:") || text.StartsWith("Contact No.:") || text.Contains("QUOTATION NO") || text.Contains("DATE"))
                {
                    elementsToRemove.Add(elem);
                }
            }
            else if (elem is Table)
            {
                break; // Stop at next table
            }
        }

        foreach (var elem in elementsToRemove)
        {
            elem.Remove();
        }

        // Capture insertion point BEFORE removing the heading
        var parent = heading.Parent;
        var previousSibling = heading.PreviousSibling();

        // Remove the original "QUOTATION TO" heading paragraph to avoid duplicate in output
        heading.Remove();

        var quotationNo = request.QuotationNo ?? "{{QUOTATION_NO}}";
        var date = request.Date != default ? request.Date.ToString("dd MMM yyyy") : "{{DATE}}";

        // Create a table for QUOTATION TO with two columns - NO borders (like frontend grid)
        var newTable = new Table();

        var tblPr = new TableProperties(
            new TableWidth { Width = "9000", Type = TableWidthUnitValues.Dxa },
            new TableBorders(
                new TopBorder { Val = BorderValues.None },
                new LeftBorder { Val = BorderValues.None },
                new BottomBorder { Val = BorderValues.None },
                new RightBorder { Val = BorderValues.None },
                new InsideHorizontalBorder { Val = BorderValues.None },
                new InsideVerticalBorder { Val = BorderValues.None }
            ),
            new TableLayout { Type = TableLayoutValues.Fixed }
        );
        newTable.Append(tblPr);

        var tblGrid = new TableGrid(
            new GridColumn { Width = "4500" },  // Left column (50%)
            new GridColumn { Width = "4500" }   // Right column (50%)
        );
        newTable.Append(tblGrid);

        // Row 1: QUOTATION TO heading (spans both columns)
        var headingRow = new TableRow();
        var headingCell = new TableCell();
        var headingCellPr = new TableCellProperties(
            new GridSpan { Val = 2 },
            new TableCellMargin
            {
                TopMargin = new TopMargin { Width = "80", Type = TableWidthUnitValues.Dxa },
                LeftMargin = new LeftMargin { Width = "120", Type = TableWidthUnitValues.Dxa },
                BottomMargin = new BottomMargin { Width = "80", Type = TableWidthUnitValues.Dxa },
                RightMargin = new RightMargin { Width = "120", Type = TableWidthUnitValues.Dxa }
            }
        );
        headingCell.Append(headingCellPr);
        var headingPara = new Paragraph();
        var headingRun = new Run();
        var headingRunPr = new RunProperties();
        headingRunPr.Append(new Bold(), new BoldComplexScript());
        headingRunPr.Append(new Color { Val = "65AADB" });
        headingRunPr.Append(new FontSize { Val = "24" }, new FontSizeComplexScript { Val = "24" });
        headingRun.Append(headingRunPr);
        headingRun.Append(new Text("QUOTATION TO") { Space = SpaceProcessingModeValues.Preserve });
        headingPara.Append(headingRun);
        headingCell.Append(headingPara);
        headingRow.Append(headingCell);
        newTable.Append(headingRow);

        // Row 2: Left side (Name, Address, Contact, Email) | Right side (Quotation No, Date)
        var dataRow = new TableRow();

        // Left cell
        var leftCell = new TableCell();
        var leftCellPr = new TableCellProperties(
            new TableCellMargin
            {
                TopMargin = new TopMargin { Width = "80", Type = TableWidthUnitValues.Dxa },
                LeftMargin = new LeftMargin { Width = "120", Type = TableWidthUnitValues.Dxa },
                BottomMargin = new BottomMargin { Width = "80", Type = TableWidthUnitValues.Dxa },
                RightMargin = new RightMargin { Width = "120", Type = TableWidthUnitValues.Dxa }
            }
        );
        leftCell.Append(leftCellPr);

        var leftContent = new List<Paragraph>
        {
            CreateQuotationToParagraph("Name: ", request.QuotationTo.Name ?? "{{CONTACT_NAME}}", true),
            CreateQuotationToParagraph("Address: ", request.QuotationTo.Address ?? "{{CONTACT_ADDRESS}}", true),
            CreateQuotationToParagraph("Contact No.: ", request.QuotationTo.ContactNo ?? "{{CONTACT_PHONE}}", true),
            CreateQuotationToParagraph("Email: ", request.QuotationTo.Email ?? "{{CONTACT_EMAIL}}", true)
        };
        foreach (var p in leftContent) leftCell.Append(p);

        // Right cell
        var rightCell = new TableCell();
        var rightCellPr = new TableCellProperties(
            new TableCellMargin
            {
                TopMargin = new TopMargin { Width = "80", Type = TableWidthUnitValues.Dxa },
                LeftMargin = new LeftMargin { Width = "120", Type = TableWidthUnitValues.Dxa },
                BottomMargin = new BottomMargin { Width = "80", Type = TableWidthUnitValues.Dxa },
                RightMargin = new RightMargin { Width = "120", Type = TableWidthUnitValues.Dxa }
            }
        );
        rightCell.Append(rightCellPr);

        var rightContent = new List<Paragraph>
        {
            CreateQuotationToParagraph("Quotation No.: ", quotationNo, true),
            CreateQuotationToParagraph("Date: ", date, true)
        };
        foreach (var p in rightContent) rightCell.Append(p);

        dataRow.Append(leftCell);
        dataRow.Append(rightCell);
        newTable.Append(dataRow);

        // Insert the new table at the position where the heading was
        if (parent is not null)
        {
            if (previousSibling is not null)
            {
                parent.InsertAfter(newTable, previousSibling);
            }
            else
            {
                // If no previous sibling, insert at the beginning
                parent.PrependChild(newTable);
            }
        }
    }

    private static Paragraph CreateQuotationToParagraph(string label, string value, bool isBold)
    {
        var para = new Paragraph();
        var run = new Run();
        var runPr = new RunProperties();
        if (isBold)
        {
            runPr.Append(new Bold(), new BoldComplexScript());
        }
        runPr.Append(new Color { Val = "65AADB" });
        runPr.Append(new FontSize { Val = "20" }, new FontSizeComplexScript { Val = "20" });
        run.Append(runPr);
        run.Append(new Text(label + value) { Space = SpaceProcessingModeValues.Preserve });
        para.Append(run);
        return para;
    }

    private static void RebuildPriceForImplementationTable(
        Body body,
        QuotationRequest request,
        List<ModuleItem> selectedModules,
        decimal totalPrice,
        decimal discountPercentage,
        decimal discountAmount,
        decimal finalPrice,
        string moduleList)
    {
        // Find the "Price for Implementation" heading paragraph
        var priceHeading = body.Descendants<Paragraph>()
            .FirstOrDefault(p => string.Concat(p.Descendants<Text>().Select(t => t.Text))
                .Contains("Price for Implementation", StringComparison.OrdinalIgnoreCase));

        if (priceHeading is null) return;

        // Find the table immediately after the heading
        Table? priceTable = null;
        var elementsAfterHeading = priceHeading.ElementsAfter().ToList();
        foreach (var elem in elementsAfterHeading)
        {
            if (elem is Table table)
            {
                priceTable = table;
                break;
            }
            else if (elem is Paragraph p && !string.IsNullOrWhiteSpace(p.InnerText))
            {
                // Stop if we hit another heading/section
                break;
            }
        }

        if (priceTable is null) return;

        // Build new table rows matching the frontend QuotationPdfView structure
        var newRows = new List<TableRow>();

        // Header row
        var headerRow = CreatePriceTableRow(new[]
        {
            ("Sr. No.", true),
            ("Particulars", true),
            ("Price in INR", true)
        }, true);
        newRows.Add(headerRow);

        // Row 1: Product License
        var licenseText = $"{moduleList} - Product License applicable for single installation.\nScope – As mentioned above";
        // if (discountPercentage > 0 && totalPrice > 0)
        // {
        //     licenseText += $"\nModule Price: ₹{totalPrice:N0}\nDiscount ({discountPercentage}%): -₹{discountAmount:N0}\nNet Price: ₹{finalPrice:N0}";
        // }
        var price1 = discountPercentage > 0 && totalPrice > 0 ? $"₹{finalPrice:N0}" : (totalPrice > 0 ? $"₹{totalPrice:N0}" : "TBD");
        newRows.Add(CreatePriceTableRow(new[]
        {
            ("1", false),
            (licenseText, false),
            (price1, false)
        }, false));

        // Row 2: Customization
        newRows.Add(CreatePriceTableRow(new[]
        {
            ("2", false),
            ("Customization\nIn case of any additional development required which will be consider 7000 per man day additional to above proposal.", false),
            ("TBD", false)
        }, true));

        // Row 3: Annual License Renewal
        newRows.Add(CreatePriceTableRow(new[]
        {
            ("3", false),
            ("Annual License Renewal\nThe License renewal would be required to be done every Year. These renewal fees will facilitate to have the Product Upgrades, which would cover improvements, bug fixes, and changes in statutory compliances. Included 7 Man days only and above will be chargeable.", false),
            ("TBD", false)
        }, false));

        // Row 4: Support Services
        newRows.Add(CreatePriceTableRow(new[]
        {
            ("4", false),
            ("Support Services\nThese Services start after 1 Month from start of Go Live.", false),
            ("TBD", false)
        }, true));

        // Row 5: Payment Terms
        newRows.Add(CreatePriceTableRow(new[]
        {
            ("5", false),
            ("Payment Terms\n70% in Advance along with Purchase Order\n20% on Implementation\n10% GO LIVE", false),
            ("On Chargeable", false)
        }, false));

        // Row 6: Support Level (header for sub-rows)
        newRows.Add(CreatePriceTableRow(new[]
        {
            ("6", false),
            ("Support Level", false),
            ("", false)
        }, true));

        // Support Level sub-rows
        var supportLevels = new[]
        {
            ("L1: Telephone Support - Queries, To understand a feature, Problem solving etc.", "On Chargeable"),
            ("L2: Bugs - Bugs identified by you or by BlechTek Software Solutions LLP", "Free, Under License Fee Renewal"),
            ("L3: Customer Specific Enhancements - New/Change in Document Printing format, New/Changes in Reports, New/Change in Reports", "On Chargeable"),
            ("L4: Product Upgrade - Product Upgrade as done by BlechTek Software Solutions LLP on their own", "Free, Under License Fee Renewal"),
            ("L5: Implementation - Master updation, new feature implementation", "On Chargeable")
        };

        for (int i = 0; i < supportLevels.Length; i++)
        {
            newRows.Add(CreatePriceTableRow(new[]
            {
                ("", false),
                (supportLevels[i].Item1, false),
                (supportLevels[i].Item2, false)
            }, i % 2 == 1)); // Alternate shading
        }

        // Row 7: Taxes
        newRows.Add(CreatePriceTableRow(new[]
        {
            ("7", false),
            ("Taxes\nAs Applicable", false),
            ("As Applicable", false)
        }, false));

        // Remove the old table and insert the new one
        var parent = priceTable.Parent;
        priceTable.Remove();

        var newTable = new Table();
        // Table properties
        var tblPr = new TableProperties(
            new TableWidth { Width = "9000", Type = TableWidthUnitValues.Dxa },
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Color = "auto", Size = 4 },
                new LeftBorder { Val = BorderValues.Single, Color = "auto", Size = 4 },
                new BottomBorder { Val = BorderValues.Single, Color = "auto", Size = 4 },
                new RightBorder { Val = BorderValues.Single, Color = "auto", Size = 4 },
                new InsideHorizontalBorder { Val = BorderValues.Single, Color = "auto", Size = 4 },
                new InsideVerticalBorder { Val = BorderValues.Single, Color = "auto", Size = 4 }
            )
        );
        newTable.Append(tblPr);

        // Table grid - 3 columns: Sr.No (5%), Particulars (75%), Price (20%)
        var tblGrid = new TableGrid(
            new GridColumn { Width = "450" },   // ~5% of 9000
            new GridColumn { Width = "6750" },  // ~75% of 9000
            new GridColumn { Width = "1800" }   // ~20% of 9000
        );
        newTable.Append(tblGrid);

        foreach (var row in newRows)
        {
            newTable.Append(row);
        }

        if (parent is not null)
        {
            parent.InsertAfter(newTable, priceHeading);
        }
    }

    private static TableRow CreatePriceTableRow((string Text, bool IsBold)[] cells, bool isAlternate)
    {
        var row = new TableRow();
        if (isAlternate)
        {
            var rowPr = new TableRowProperties();
            var shading = new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = "F2F4F7" };
            rowPr.Append(shading);
            row.Append(rowPr);
        }

        foreach (var (text, isBold) in cells)
        {
            var cell = new TableCell();
            var tcPr = new TableCellProperties(
                new TableCellMargin
                {
                    TopMargin = new TopMargin { Width = "80", Type = TableWidthUnitValues.Dxa },
                    LeftMargin = new LeftMargin { Width = "120", Type = TableWidthUnitValues.Dxa },
                    BottomMargin = new BottomMargin { Width = "80", Type = TableWidthUnitValues.Dxa },
                    RightMargin = new RightMargin { Width = "120", Type = TableWidthUnitValues.Dxa }
                },
                new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center }
            );

            if (isBold)
            {
                var shading = new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = "65AADB" };
                tcPr.Append(shading);
            }
            else if (isAlternate)
            {
                var shading = new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = "F2F4F7" };
                tcPr.Append(shading);
            }

            cell.Append(tcPr);

            var para = new Paragraph();
            var run = new Run();
            var runPr = new RunProperties();
            if (isBold)
            {
                runPr.Append(new Bold(), new BoldComplexScript());
                runPr.Append(new Color { Val = "FFFFFF" });
            }
            runPr.Append(new FontSize { Val = "20" }, new FontSizeComplexScript { Val = "20" });
            run.Append(runPr);
            run.Append(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
            para.Append(run);
            cell.Append(para);
            row.Append(cell);
        }

        return row;
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
                if (headerPart.RootElement is null) continue;
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
