using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using QuestPDFColor = QuestPDF.Infrastructure.Color;
using QuestPDFColors = QuestPDF.Helpers.Colors;
using OpenXmlColor = DocumentFormat.OpenXml.Wordprocessing.Color;

namespace QuotationApp.API.Services;

/// <summary>
/// Converts the generated .docx to .pdf using QuestPDF.
/// Faithfully replicates the Word template design including colors, tables, borders, and styling.
/// Matches the frontend QuotationPdfView design exactly.
/// </summary>
public class PdfConverterService : IPdfConverterService
{
    // Color constants matching the frontend design and Word template
    private static readonly QuestPDFColor PrimaryBlue = QuestPDFColor.FromHex("#65aadb");
    private static readonly QuestPDFColor White = QuestPDFColor.FromHex("#FFFFFF");
    private static readonly QuestPDFColor LightGray = QuestPDFColor.FromHex("#F2F4F7");
    private static readonly QuestPDFColor TableBorder = QuestPDFColor.FromHex("#CCCCCC");
    private static readonly QuestPDFColor TextBlack = QuestPDFColor.FromHex("#000000");
    private static readonly QuestPDFColor DarkText = QuestPDFColor.FromHex("#333333");
    private static readonly QuestPDFColor MediumText = QuestPDFColor.FromHex("#555555");
    private static readonly QuestPDFColor LightText = QuestPDFColor.FromHex("#888888");
    private static readonly QuestPDFColor NoteBackground = QuestPDFColor.FromHex("#F8FBFA");

    // Text labels that must render with ONLY the label portion bold (e.g. "Name:" bold, "XYZ Corp" normal)
    private static readonly string[] BoldLabelPrefixes = new[]
    {
        "Name:",
        "Address:",
        "Contact No.:",
        "Email:",
        "Quotation No.:",
        "Date:",
        "Reference:",
        "Subject:",
        "Dear Sir / Madam,",
        "Definition:",
        "Installation pre-requisites (in case of on-premise Server):",
        "For BlechTek Software Solutions LLP",
        "Sushama Inamdar"
    };

    /// <summary>
    /// Checks whether the given text starts with one of the known bold label prefixes.
    /// If it does, returns the label portion (to be bolded) and the remaining text (normal weight).
    /// </summary>
    private static bool TryGetBoldLabelPrefix(string text, out string label, out string rest)
    {
        label = null;
        rest = null;
        if (string.IsNullOrEmpty(text)) return false;

        foreach (var prefix in BoldLabelPrefixes)
        {
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                label = text.Substring(0, prefix.Length);
                rest = text.Substring(prefix.Length);
                return true;
            }
        }
        return false;
    }

    private readonly string _contentRoot;

    public PdfConverterService(IOptions<QuotationSettings> settings, IWebHostEnvironment env)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        _contentRoot = env.ContentRootPath;
    }

    private string GetLogoPath()
    {
        // Check multiple locations for the logo, similar to WordGeneratorService
        var backendLogoPng = Path.Combine(_contentRoot, "logo", "logo.png");
        var backendLogoJpg = Path.Combine(_contentRoot, "logo", "logo.jpg");

        // Check frontend/logo directory (project workspace sibling)
        var frontendLogoPng = Path.GetFullPath(Path.Combine(_contentRoot, "..", "..", "frontend", "logo", "logo.png"));
        var frontendLogoJpg = Path.GetFullPath(Path.Combine(_contentRoot, "..", "..", "frontend", "logo", "logo.jpg"));

        // Also check frontend/public/logo (where it's served from)
        var frontendPublicLogoPng = Path.GetFullPath(Path.Combine(_contentRoot, "..", "..", "frontend", "public", "logo", "logo.png"));
        var frontendPublicLogoJpg = Path.GetFullPath(Path.Combine(_contentRoot, "..", "..", "frontend", "public", "logo", "logo.jpg"));

        if (File.Exists(backendLogoPng)) return backendLogoPng;
        if (File.Exists(backendLogoJpg)) return backendLogoJpg;
        if (File.Exists(frontendLogoPng)) return frontendLogoPng;
        if (File.Exists(frontendLogoJpg)) return frontendLogoJpg;
        if (File.Exists(frontendPublicLogoPng)) return frontendPublicLogoPng;
        if (File.Exists(frontendPublicLogoJpg)) return frontendPublicLogoJpg;

        return null;
    }

    private string GetWatermarkPath()
    {
        // Check multiple locations for the watermark
        var backendWatermarkPng = Path.Combine(_contentRoot, "logo", "watermark.png");
        var backendWatermarkJpg = Path.Combine(_contentRoot, "logo", "watermark.jpg");

        // Check frontend/logo directory (project workspace sibling)
        var frontendWatermarkPng = Path.GetFullPath(Path.Combine(_contentRoot, "..", "..", "frontend", "logo", "watermark.png"));
        var frontendWatermarkJpg = Path.GetFullPath(Path.Combine(_contentRoot, "..", "..", "frontend", "logo", "watermark.jpg"));

        // Also check frontend/public/logo (where it's served from)
        var frontendPublicWatermarkPng = Path.GetFullPath(Path.Combine(_contentRoot, "..", "..", "frontend", "public", "logo", "watermark.png"));
        var frontendPublicWatermarkJpg = Path.GetFullPath(Path.Combine(_contentRoot, "..", "..", "frontend", "public", "logo", "watermark.jpg"));

        if (File.Exists(backendWatermarkPng)) return backendWatermarkPng;
        if (File.Exists(backendWatermarkJpg)) return backendWatermarkJpg;
        if (File.Exists(frontendWatermarkPng)) return frontendWatermarkPng;
        if (File.Exists(frontendWatermarkJpg)) return frontendWatermarkJpg;
        if (File.Exists(frontendPublicWatermarkPng)) return frontendPublicWatermarkPng;
        if (File.Exists(frontendPublicWatermarkJpg)) return frontendPublicWatermarkJpg;

        return null;
    }

    public async Task<string> ConvertToPdfAsync(string docxPath)
    {
        var outputFolder = Path.GetDirectoryName(docxPath)!;
        var expectedPdfPath = Path.Combine(outputFolder, Path.GetFileNameWithoutExtension(docxPath) + ".pdf");

        var documentContent = ExtractDocumentStructure(docxPath);
        await GeneratePdfAsync(documentContent, expectedPdfPath);

        return expectedPdfPath;
    }

    private DocumentContent ExtractDocumentStructure(string docxPath)
    {
        using var document = WordprocessingDocument.Open(docxPath, false);
        var body = document.MainDocumentPart?.Document?.Body;
        if (body is null) return new DocumentContent();

        var content = new DocumentContent();

        foreach (var element in body.Elements())
        {
            if (element is Paragraph paragraph)
            {
                var paraContent = ExtractParagraphContent(paragraph);
                if (!string.IsNullOrWhiteSpace(paraContent.Text))
                {
                    content.Elements.Add(paraContent);
                }
            }
            else if (element is Table table)
            {
                var tableContent = ExtractTableContent(table);
                if (tableContent.Rows.Count > 0)
                {
                    content.Elements.Add(tableContent);
                }
            }
        }

        return content;
    }

    private ParagraphContent ExtractParagraphContent(Paragraph paragraph)
    {
        var text = paragraph.InnerText.Trim();
        if (string.IsNullOrWhiteSpace(text)) return new ParagraphContent();

        var properties = paragraph.ParagraphProperties;
        bool isBold = false;
        bool isCentered = false;
        bool isAllCaps = false;
        bool isNumbered = false;
        int fontSize = 11;
        string fontFamily = "Calibri";
        QuestPDFColor? textColor = null;
        QuestPDFColor? backgroundColor = null;
        float spacingAfter = 0;
        float spacingBefore = 0;
        bool hasBottomBorder = false;
        QuestPDFColor? borderColor = null;

        // Check run properties
        var runs = paragraph.Elements<Run>();
        foreach (var run in runs)
        {
            var runProps = run.RunProperties;
            if (runProps?.Bold != null || runProps?.BoldComplexScript != null)
            {
                isBold = true;
            }
            if (runProps?.Caps != null)
            {
                isAllCaps = true;
            }
            var fontSizeVal = runProps?.FontSize?.Val?.Value;
            if (!string.IsNullOrWhiteSpace(fontSizeVal))
            {
                fontSize = int.Parse(fontSizeVal) / 2; // Convert half-points to points
            }
        }

        // Check paragraph properties
        var justification = properties?.Justification;
        if (justification?.Val != null && justification.Val == JustificationValues.Center)
        {
            isCentered = true;
        }

        var numbering = properties?.NumberingProperties;
        if (numbering != null)
        {
            isNumbered = true;
        }

        var spacing = properties?.SpacingBetweenLines;
        if (spacing?.After?.Value != null)
        {
            spacingAfter = int.Parse(spacing.After.Value) / 20f; // Convert twips to points
        }
        if (spacing?.Before?.Value != null)
        {
            spacingBefore = int.Parse(spacing.Before.Value) / 20f;
        }

        // Check for bottom border
        var pBdr = properties?.ParagraphBorders?.GetFirstChild<BottomBorder>();
        if (pBdr != null)
        {
            hasBottomBorder = true;
            var borderColorVal = pBdr.Color?.Value;
            if (!string.IsNullOrWhiteSpace(borderColorVal) && TryParseColor(borderColorVal, out var qc))
                borderColor = qc;
        }

        // Check style ID for headings
        var styleId = properties?.ParagraphStyleId?.Val?.Value;
        if (!string.IsNullOrEmpty(styleId))
        {
            if (styleId.StartsWith("Heading1"))
            {
                fontSize = 16;
                isBold = true;
            }
            else if (styleId.StartsWith("Heading2"))
            {
                fontSize = 13;
                isBold = true;
            }
            else if (styleId.StartsWith("Heading3"))
            {
                fontSize = 12;
                isBold = true;
            }
        }

        return new ParagraphContent
        {
            Text = text,
            IsBold = isBold,
            IsCentered = isCentered,
            IsAllCaps = isAllCaps,
            IsNumbered = isNumbered,
            FontSize = fontSize,
            FontFamily = fontFamily,
            TextColor = textColor,
            BackgroundColor = backgroundColor,
            SpacingAfter = spacingAfter,
            SpacingBefore = spacingBefore,
            HasBottomBorder = hasBottomBorder,
            BorderColor = borderColor
        };
    }

    private TableContent ExtractTableContent(Table table)
    {
        var tableContent = new TableContent();
        var rows = table.Elements<TableRow>().ToList();

        // Get table borders
        var tblBorders = table.TableProperties?.TableBorders;

        // Check if borders are actually visible (not None)
        bool hasVisibleBorders = false;
        if (tblBorders != null)
        {
            var borderTypes = new List<BorderType>
            {
                tblBorders.GetFirstChild<TopBorder>(),
                tblBorders.GetFirstChild<LeftBorder>(),
                tblBorders.GetFirstChild<BottomBorder>(),
                tblBorders.GetFirstChild<RightBorder>(),
                tblBorders.GetFirstChild<InsideHorizontalBorder>(),
                tblBorders.GetFirstChild<InsideVerticalBorder>()
            };

            foreach (var border in borderTypes)
            {
                if (border != null && border.Val != null && border.Val != BorderValues.None)
                {
                    hasVisibleBorders = true;
                    break;
                }
            }

            var topBorder = tblBorders.GetFirstChild<TopBorder>();
            var topBorderColorVal = topBorder?.Color?.Value;
            if (!string.IsNullOrWhiteSpace(topBorderColorVal) && TryParseColor(topBorderColorVal, out var qc))
                tableContent.BorderColor = qc;
        }

        tableContent.HasBorders = hasVisibleBorders;

        // Get column widths
        var tblGrid = table.TableProperties?.GetFirstChild<TableGrid>();
        if (tblGrid != null)
        {
            foreach (var gridCol in tblGrid.Elements<GridColumn>())
            {
                if (gridCol.Width?.Value != null)
                {
                    tableContent.ColumnWidths.Add(int.Parse(gridCol.Width.Value));
                }
            }
        }

        foreach (var row in rows)
        {
            var cells = row.Elements<TableCell>().ToList();
            var rowContent = new List<CellContent>();

            foreach (var cell in cells)
            {
                // IMPORTANT: cell.InnerText concatenates every paragraph in the cell into a single
                // string with NO line breaks between them. That broke label bolding — e.g. a cell
                // containing "Name: X" then "Address: Y" then "Contact No.: Z" as separate <w:p>
                // paragraphs collapsed into one run-on string, so only the very first label ("Name:")
                // was ever detected; "Address:", "Contact No.:", "Email:", "Date:", "Definition:", and
                // "Installation pre-requisites..." were all swallowed into the unbolded "rest" text.
                // Extracting each paragraph separately and rejoining with '\n' preserves the line
                // breaks so downstream Split('\n') + per-line bold-label detection works correctly.
                var cellParagraphTexts = cell.Elements<Paragraph>()
                    .Select(p => p.InnerText.Trim())
                    .Where(t => !string.IsNullOrEmpty(t))
                    .ToList();
                var cellText = cellParagraphTexts.Count > 0
                    ? string.Join("\n", cellParagraphTexts)
                    : cell.InnerText.Trim();
                var cellProps = cell.TableCellProperties;

                bool isBold = false;
                // Check if this is a header row by looking at the first row's shading
                bool isHeaderRow = false;
                if (rows.IndexOf(row) == 0)
                {
                    // Check if first row has dark shading (header indicator) - supports both old (1B2A4E) and new (65AADB) template colors
                    var firstCell = row.Elements<TableCell>().FirstOrDefault();
                    var shading = firstCell?.TableCellProperties?.GetFirstChild<Shading>();
                    var fillValue = shading?.Fill?.Value;
                    if (!string.IsNullOrEmpty(fillValue) && (fillValue.Equals("1B2A4E", StringComparison.OrdinalIgnoreCase) || fillValue.Equals("65AADB", StringComparison.OrdinalIgnoreCase)))
                    {
                        isHeaderRow = true;
                    }
                }
                QuestPDFColor? backgroundColor = null;
                QuestPDFColor? textColor = null;
                float padding = 4f; // Default padding in points (twips/20)

                // Check cell margins
                if (cellProps?.TableCellMargin != null)
                {
                    var topMargin = cellProps.TableCellMargin.GetFirstChild<TopMargin>();
                    if (topMargin?.Width?.Value != null)
                    {
                        padding = int.Parse(topMargin.Width.Value) / 20f;
                    }
                }

                // Check for shading/background
                var cellShading = cellProps?.GetFirstChild<Shading>();
                var cellShadingFill = cellShading?.Fill?.Value;
                if (!string.IsNullOrWhiteSpace(cellShadingFill) && TryParseColor(cellShadingFill, out var qc))
                    backgroundColor = qc;

                // Check for vertical alignment - use OpenXML VerticalAlignment
                // var vAlign = cellProps?.GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.VerticalAlignment>();
                // We'll handle this in PDF generation

                // Check run properties for bold and color
                var paragraphs = cell.Elements<Paragraph>();
                foreach (var para in paragraphs)
                {
                    var runs = para.Elements<Run>();
                    foreach (var run in runs)
                    {
                        var runProps = run.RunProperties;
                        if (runProps?.Bold != null || runProps?.BoldComplexScript != null)
                        {
                            isBold = true;
                        }
                        if (runProps?.FontSize?.Val != null)
                        {
                            // Font size handled at cell level
                        }
                    }
                }

                // Header row defaults
                if (isHeaderRow)
                {
                    isBold = true;
                    if (!backgroundColor.HasValue) backgroundColor = White;
                    if (!textColor.HasValue) textColor = TextBlack;
                }

                rowContent.Add(new CellContent
                {
                    Text = cellText,
                    IsBold = isBold,
                    IsHeader = isHeaderRow,
                    BackgroundColor = backgroundColor,
                    TextColor = textColor,
                    Padding = padding
                });
            }

            if (rowContent.Count > 0)
            {
                tableContent.Rows.Add(rowContent);
            }
        }

        return tableContent;
    }

    private async Task GeneratePdfAsync(DocumentContent content, string outputPath)
    {
        var watermarkPath = GetWatermarkPath();
        var hasWatermark = !string.IsNullOrEmpty(watermarkPath) && File.Exists(watermarkPath);
        var logoPath = GetLogoPath();

        var questDocument = QuestPDF.Fluent.Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1, Unit.Centimetre); // 1.5cm margins (~15mm)
                page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Calibri").FontColor(TextBlack).LineHeight(1.5f));

                // Add watermark as a background layer - centered with equal margins from all sides
                if (hasWatermark)
                {
                    page.Background()
                        .Padding(80) // Equal padding from all 4 sides
                        .AlignCenter()
                        .AlignMiddle()
                        .Width(250)
                        .Height(250)
                        .Image(watermarkPath);
                }

                // Header - repeated on every page
                page.Header().Column(headerCol =>
                {
                    headerCol.Spacing(0);
                    headerCol.Item().Row(headerRow =>
                    {
                        headerRow.RelativeItem().Column(leftCol =>
                        {
                            // Load and display logo image from multiple possible locations
                            if (!string.IsNullOrEmpty(logoPath) && File.Exists(logoPath))
                            {
                                leftCol.Item().Height(30).Image(logoPath);
                            }
                            else
                            {
                                // Fallback text if logo not found
                                leftCol.Item().Text("BlechTek Software Solutions LLP")
                                    .FontSize(14).FontFamily("Calibri").FontColor(TextBlack).SemiBold();
                            }
                        });
                        headerRow.RelativeItem().AlignRight().Column(rightCol =>
                        {
                            rightCol.Item().Text("QUOTATION")
                                .FontSize(16).FontFamily("Calibri").FontColor(TextBlack).Bold();
                        });
                    });

                    headerCol.Item().PaddingTop(8).BorderBottom(2).BorderColor(TextBlack).PaddingBottom(0);
                    headerCol.Item().PaddingTop(16);
                });

                page.Content().Column(column =>
                {
                    column.Spacing(0);

                    // Process all elements in order
                    var allElements = content.Elements;
                    for (int i = 0; i < allElements.Count; i++)
                    {
                        var element = allElements[i];
                        if (element is ParagraphContent para)
                        {
                            RenderParagraph(column, para, i, allElements);
                        }
                        else if (element is TableContent table)
                        {
                            RenderTable(column, table);
                        }
                    }
                });

                page.Footer().Height(80).Column(col =>
                {
                    col.Item().PaddingTop(16).BorderTop(1).BorderColor(TextBlack)
                        .Column(footerCol =>
                        {
                            footerCol.Item().AlignCenter().Text("BlechTek Software Solutions LLP")
                                .FontSize(10).FontFamily("Calibri").FontColor(TextBlack).Bold();
                            footerCol.Item().AlignCenter().Text("Address: S.NO. 257/2/2A/4 ABC Business Center, S Floor, Opp. WindMill Village Road, WindMill Village, Bavdhan, Pune 411021, Maharashtra")
                                .FontSize(9).FontFamily("Calibri").FontColor(TextBlack);
                            footerCol.Item().AlignCenter().Text("LLP No.: ACD-6620 | GST NO.: 27ABCFB0283B1Z0 | MSME Certificate No.: UDYAM-MH-26-0746115")
                                .FontSize(9).FontFamily("Calibri").FontColor(TextBlack);
                        });
                });
            });
        });

        questDocument.GeneratePdf(outputPath);
        await Task.CompletedTask;
    }

    private void RenderParagraph(ColumnDescriptor column, ParagraphContent para, int index, List<IDocumentElement> allElements)
    {
        if (string.IsNullOrWhiteSpace(para.Text)) return;

        // Check for section headings (uppercase headings like "QUOTATION TO", "SCOPE OF WORK", etc.)
        var isSectionHeading = IsSectionHeading(para.Text);
        var isQuotationToHeading = para.Text.Trim().StartsWith("QUOTATION TO", StringComparison.OrdinalIgnoreCase);
        var isNote = para.Text.Contains("Deliverables do not include", StringComparison.OrdinalIgnoreCase);
        var isPricingHeading = IsPricingSectionHeading(para.Text);
        var isScopeHeading = IsScopeSectionHeading(para.Text);

        if (isPricingHeading)
        {
            column.Item().PaddingTop(24).PaddingBottom(8).BorderBottom(1).BorderColor(TextBlack).PaddingBottom(4)
                .Text(para.Text.ToUpper())
                .FontSize(11).FontFamily("Calibri").FontColor(TextBlack).Bold();
            return;
        }

        if (isScopeHeading)
        {
            column.Item().PaddingTop(24).PaddingBottom(8).BorderBottom(1).BorderColor(TextBlack).PaddingBottom(4)
                .Text(para.Text.ToUpper())
                .FontSize(11).FontFamily("Calibri").FontColor(TextBlack).Bold();
            return;
        }

        if (isSectionHeading)
        {
            column.Item().PaddingTop(24).PaddingBottom(8).BorderBottom(1).BorderColor(TextBlack).PaddingBottom(4)
                .Text(para.Text.ToUpper())
                .FontSize(11).FontFamily("Calibri").FontColor(TextBlack).Bold();
            return;
        }

        if (isQuotationToHeading)
        {
            column.Item().PaddingBottom(6)
                .Text(para.Text)
                .FontSize(11).FontFamily("Calibri").FontColor(TextBlack).Bold();
            return;
        }

        if (isNote)
        {
            column.Item().PaddingTop(16).PaddingBottom(16).PaddingLeft(12).BorderLeft(3).BorderColor(TextBlack)
                .Background(NoteBackground).Padding(10, Unit.Point).PaddingRight(12, Unit.Point)
                .Text(para.Text)
                .FontSize(10).FontFamily("Calibri").FontColor(DarkText).Italic().LineHeight(1.55f);
            return;
        }

        // Check if we're in the Terms and Conditions section
        var isTermsAndConditions = IsInTermsAndConditionsSection(index, allElements);

        if (isTermsAndConditions)
        {
            RenderTermsAndConditionsItem(column, para, index, allElements);
            return;
        }

        // Override bold for specific body texts that should be normal (not bold)
        var text = para.Text.Trim();
        var isBodyText = text.StartsWith("Reference:", StringComparison.OrdinalIgnoreCase) ||
                         text.StartsWith("Subject:", StringComparison.OrdinalIgnoreCase) ||
                         text.StartsWith("We discussed the current challenges", StringComparison.OrdinalIgnoreCase) ||
                         text.StartsWith("Preliminary Business Proposal", StringComparison.OrdinalIgnoreCase) ||
                         text.StartsWith("Our experts will be involved", StringComparison.OrdinalIgnoreCase) ||
                         text.StartsWith("Digitization of Audit Tracker", StringComparison.OrdinalIgnoreCase) ||
                         text.StartsWith("Training and implementation using CQUAL", StringComparison.OrdinalIgnoreCase);

        // Texts that should always be bold (field labels, section headers)
        var isBoldText = text.Equals("Name:", StringComparison.OrdinalIgnoreCase) ||
                         text.Equals("Address:", StringComparison.OrdinalIgnoreCase) ||
                         text.Equals("Contact No.:", StringComparison.OrdinalIgnoreCase) ||
                         text.Equals("Email:", StringComparison.OrdinalIgnoreCase) ||
                         text.Equals("Quotation No.:", StringComparison.OrdinalIgnoreCase) ||
                         text.Equals("Date:", StringComparison.OrdinalIgnoreCase) ||
                         text.Equals("Reference:", StringComparison.OrdinalIgnoreCase) ||
                         text.Equals("Subject:", StringComparison.OrdinalIgnoreCase) ||
                         text.Equals("Dear Sir / Madam,", StringComparison.OrdinalIgnoreCase) ||
                         text.Equals("Definition:", StringComparison.OrdinalIgnoreCase) ||
                         text.Equals("Installation pre-requisites (in case of on-premise Server):", StringComparison.OrdinalIgnoreCase);

        // NEW: If this paragraph starts with one of the known label prefixes (Name:, Address:,
        // Contact No.:, Email:, Quotation No.:, Date:, Reference:, Subject:, Dear Sir / Madam,,
        // Definition:, Installation pre-requisites...), render it as two spans so ONLY the
        // label portion is bold and any trailing value stays normal weight. This also correctly
        // covers the case where the paragraph is just the label by itself (rest will be empty).
        if (TryGetBoldLabelPrefix(text, out var boldLabel, out var labelRest))
        {
            var labelContainer = column.Item();

            if (para.SpacingBefore > 0)
                labelContainer = labelContainer.PaddingTop(para.SpacingBefore, Unit.Point);

            if (para.SpacingAfter > 0)
                labelContainer = labelContainer.PaddingBottom(para.SpacingAfter, Unit.Point);

            if (para.HasBottomBorder)
                labelContainer = labelContainer.BorderBottom(1).BorderColor(para.BorderColor ?? TableBorder);

            labelContainer.Text(t =>
            {
                if (para.IsCentered)
                    t.AlignCenter();

                t.DefaultTextStyle(TextStyle.Default
                    .FontSize(para.FontSize)
                    .FontFamily(para.FontFamily)
                    .FontColor(para.TextColor ?? TextBlack)
                    .LineHeight(1.55f));

                t.Span(boldLabel).Bold();
                if (!string.IsNullOrEmpty(labelRest))
                {
                    t.Span(labelRest);
                }
            });

            return;
        }

        var paragraphContainer = column.Item();

        if (para.SpacingBefore > 0)
            paragraphContainer = paragraphContainer.PaddingTop(para.SpacingBefore, Unit.Point);

        if (para.SpacingAfter > 0)
            paragraphContainer = paragraphContainer.PaddingBottom(para.SpacingAfter, Unit.Point);

        if (para.HasBottomBorder)
            paragraphContainer = paragraphContainer.BorderBottom(1).BorderColor(para.BorderColor ?? TableBorder);

        var textStyle = TextStyle.Default
            .FontSize(para.FontSize)
            .FontFamily(para.FontFamily)
            .FontColor(para.TextColor ?? TextBlack)
            .LineHeight(1.55f);

        // Only apply bold if not one of the body texts that should be normal, or if it's a bold text
        if ((para.IsBold && !isBodyText) || isBoldText) textStyle = textStyle.Bold();

        var textItem = paragraphContainer.Text(para.Text).Style(textStyle);

        if (para.IsCentered)
            textItem.AlignCenter();

        if (para.IsAllCaps)
        {
            // Small caps not directly supported, use uppercase text if needed.
        }

        if (para.IsNumbered)
        {
            // QuestPDF handles numbering differently; keep text as-is.
        }
    }

    private bool IsInTermsAndConditionsSection(int currentIndex, List<IDocumentElement> allElements)
    {
        // Look backwards to find if we're after "TERMS AND CONDITIONS" heading and before the next section
        bool foundTermsHeading = false;

        // BUG FIX: the old forward-only check could never detect that we'd already passed the
        // closing statement ("Thanking You" / "We hope this document is in line...") because it
        // only looked FORWARD for that closing phrase. Any paragraph physically located AFTER the
        // closing (e.g. the "For BlechTek Software Solutions LLP" / "Sushama Inamdar" signature
        // block) would find no closing phrase ahead of it, fall through, and incorrectly be
        // reported as "still in Terms and Conditions" -- which routed it to
        // RenderTermsAndConditionsItem (plain, unbolded text) instead of our label-bolding logic.
        // We now also scan backwards for the closing phrase; if it appears before currentIndex,
        // we know we're already past the Terms and Conditions section.
        bool foundClosingBeforeCurrent = false;

        for (int i = currentIndex - 1; i >= 0; i--)
        {
            if (allElements[i] is ParagraphContent prevPara)
            {
                var text = prevPara.Text.Trim().ToUpper();
                if (text == "TERMS AND CONDITIONS")
                {
                    foundTermsHeading = true;
                    break;
                }
                if (text == "WE HOPE THIS DOCUMENT IS IN LINE" || text.StartsWith("THANKING YOU"))
                {
                    foundClosingBeforeCurrent = true;
                }
                // Stop if we hit another major section heading
                if (IsSectionHeading(prevPara.Text) && text != "TERMS AND CONDITIONS")
                {
                    break;
                }
            }
        }

        if (!foundTermsHeading || foundClosingBeforeCurrent) return false;

        // Also check we're not past the closing section
        for (int i = currentIndex + 1; i < allElements.Count; i++)
        {
            if (allElements[i] is ParagraphContent nextPara)
            {
                var text = nextPara.Text.Trim().ToUpper();
                if (text == "WE HOPE THIS DOCUMENT IS IN LINE" || text.StartsWith("THANKING YOU"))
                {
                    return true; // We're still in terms section
                }
            }
        }

        return foundTermsHeading;
    }

    private void RenderTermsAndConditionsItem(ColumnDescriptor column, ParagraphContent para, int index, List<IDocumentElement> allElements)
    {
        var text = para.Text.Trim();

        // Check if this is a main numbered item (starts with number like "1.", "2.", etc.)
        var mainItemMatch = System.Text.RegularExpressions.Regex.Match(text, @"^(\d+)\.\s*(.+)");
        // Check if this is a sub-item (starts with "i.", "ii.", "iii.", etc.)
        var subItemMatch = System.Text.RegularExpressions.Regex.Match(text, @"^([ivx]+)\.\s*(.+)");

        if (mainItemMatch.Success)
        {
            var number = mainItemMatch.Groups[1].Value;
            var content = mainItemMatch.Groups[2].Value;

            // Check if the content starts with a bold title (e.g., "Confidentiality", "License", etc.)
            var titleEndIndex = content.IndexOfAny(new[] { ' ', '\t', '\n' });
            string title = "";
            string body = content;

            // Known titles in the terms and conditions
            var knownTitles = new[] { "Confidentiality", "License", "Ownership of Source Code", "Warranty",
                "Installation of Software", "Excused Performance", "Discontinuation of Contract", "TDS",
                "Validity of the Offer", "Suggestions by your Auditors and / or Consultants", "Legal" };

            foreach (var knownTitle in knownTitles)
            {
                if (content.StartsWith(knownTitle, StringComparison.OrdinalIgnoreCase))
                {
                    title = knownTitle;
                    body = content.Substring(knownTitle.Length).TrimStart();
                    break;
                }
            }

            column.Item().PaddingTop(8).Column(itemCol =>
            {
                // Number and title
                itemCol.Item().Row(row =>
                {
                    row.AutoItem().Width(25).AlignRight().Text($"{number}.")
                        .FontSize(10).FontFamily("Calibri").FontColor(TextBlack).Bold();
                    row.RelativeItem().PaddingLeft(8).Column(contentCol =>
                    {
                        if (!string.IsNullOrEmpty(title))
                        {
                            contentCol.Item().Text(title).FontSize(10).FontFamily("Calibri").FontColor(TextBlack).Bold();
                        }
                        if (!string.IsNullOrEmpty(body))
                        {
                            // NEW: if no known title matched above, the body may itself start with one
                            // of our known bold labels (e.g. "Definition:", "Installation pre-requisites
                            // (in case of on-premise Server):"). In that case render only the label
                            // portion bold, inline with the rest of the body, instead of plain text.
                            if (string.IsNullOrEmpty(title) && TryGetBoldLabelPrefix(body, out var bodyBoldLabel, out var bodyLabelRest))
                            {
                                contentCol.Item().PaddingTop(2).Text(t =>
                                {
                                    t.DefaultTextStyle(TextStyle.Default.FontSize(10).FontFamily("Calibri").FontColor(DarkText).LineHeight(1.5f));
                                    t.Span(bodyBoldLabel).Bold();
                                    if (!string.IsNullOrEmpty(bodyLabelRest))
                                    {
                                        t.Span(bodyLabelRest);
                                    }
                                });
                            }
                            else
                            {
                                contentCol.Item().PaddingTop(2).Text(body)
                                    .FontSize(10).FontFamily("Calibri").FontColor(DarkText).LineHeight(1.5f);
                            }
                        }
                    });
                });
            });
        }
        else if (subItemMatch.Success)
        {
            var subNumber = subItemMatch.Groups[1].Value;
            var subContent = subItemMatch.Groups[2].Value;

            column.Item().PaddingLeft(30).PaddingTop(4).Row(row =>
            {
                row.AutoItem().Width(20).AlignRight().Text($"{subNumber}.")
                    .FontSize(10).FontFamily("Calibri").FontColor(TextBlack);
                row.RelativeItem().PaddingLeft(8).Text(subContent)
                    .FontSize(10).FontFamily("Calibri").FontColor(DarkText).LineHeight(1.5f);
            });
        }
        else
        {
            // Regular paragraph within terms section.
            // Safety net: if this line starts with one of our known bold labels (e.g. "Definition:",
            // "Installation pre-requisites (in case of on-premise Server):", "For BlechTek Software
            // Solutions LLP", "Sushama Inamdar"), still render only the label portion bold here too,
            // in case such content is nested inside the Terms and Conditions numbering structure.
            if (TryGetBoldLabelPrefix(text, out var termsBoldLabel, out var termsLabelRest))
            {
                column.Item().PaddingLeft(30).PaddingTop(4).Text(t =>
                {
                    t.DefaultTextStyle(TextStyle.Default.FontSize(10).FontFamily("Calibri").FontColor(DarkText).LineHeight(1.5f));
                    t.Span(termsBoldLabel).Bold();
                    if (!string.IsNullOrEmpty(termsLabelRest))
                    {
                        t.Span(termsLabelRest);
                    }
                });
            }
            else
            {
                column.Item().PaddingLeft(30).PaddingTop(4).Text(text)
                    .FontSize(10).FontFamily("Calibri").FontColor(DarkText).LineHeight(1.5f);
            }
        }
    }

    private bool IsSectionHeading(string text)
    {
        var trimmed = text.Trim();
        var headings = new[]
        {
            "GOALS AND EXPECTATIONS",
            "SCOPE OF WORK",
            "SCOPE",
            "PRICE FOR IMPLEMENTATION",
            "TERMS AND CONDITIONS"
        };
        return headings.Any(h => trimmed.Equals(h, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsPricingSectionHeading(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Equals("PRICE FOR IMPLEMENTATION", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsScopeSectionHeading(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Equals("SCOPE", StringComparison.OrdinalIgnoreCase) ||
               trimmed.Equals("SCOPE OF WORK", StringComparison.OrdinalIgnoreCase);
    }

    private void RenderTable(ColumnDescriptor column, TableContent table)
    {
        if (table.Rows.Count == 0) return;

        // If table has no visible borders, render as grid (for QUOTATION TO section)
        if (!table.HasBorders)
        {
            RenderQuotationToGrid(column, table);
            return;
        }

        // Check if this is the pricing table (has 3 columns with specific headers)
        var isPricingTable = table.Rows.Count > 0 && table.Rows[0].Count == 3 &&
            table.Rows[0][0].Text.Trim().Equals("Sr. No.", StringComparison.OrdinalIgnoreCase) &&
            table.Rows[0][1].Text.Trim().Equals("Particulars", StringComparison.OrdinalIgnoreCase) &&
            table.Rows[0][2].Text.Trim().Equals("Price in INR", StringComparison.OrdinalIgnoreCase);

        // Check if this is a scope table (has specific headers for scope)
        var isScopeTable = table.Rows.Count > 0 && table.Rows[0].Count >= 2 &&
            (table.Rows[0][0].Text.Trim().Equals("Sr. No.", StringComparison.OrdinalIgnoreCase) ||
             table.Rows[0][0].Text.Trim().Equals("S.No.", StringComparison.OrdinalIgnoreCase) ||
             table.Rows[0][0].Text.Trim().Equals("S.No", StringComparison.OrdinalIgnoreCase)) &&
            (table.Rows[0][1].Text.Trim().Equals("Particulars", StringComparison.OrdinalIgnoreCase) ||
             table.Rows[0][1].Text.Trim().Equals("Description", StringComparison.OrdinalIgnoreCase) ||
             table.Rows[0][1].Text.Trim().Equals("Scope", StringComparison.OrdinalIgnoreCase));

        column.Item().Table(tableDef =>
                    {
                        // Define columns based on column widths or table type
                        if (isPricingTable)
                        {
                            tableDef.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn(0.08f); // Sr. No. - 8%
                                c.RelativeColumn(0.72f); // Particulars - 72%
                                c.RelativeColumn(0.20f); // Price in INR - 20%
                            });
                        }
                        else if (isScopeTable && table.ColumnWidths.Count > 0)
                        {
                            tableDef.ColumnsDefinition(c =>
                            {
                                var totalWidth = table.ColumnWidths.Sum();
                                foreach (var width in table.ColumnWidths)
                                {
                                    float ratio = (float)width / totalWidth;
                                    c.RelativeColumn(ratio);
                                }
                            });
                        }
                        else if (table.ColumnWidths.Count > 0)
                        {
                            tableDef.ColumnsDefinition(c =>
                            {
                                var totalWidth = table.ColumnWidths.Sum();
                                foreach (var width in table.ColumnWidths)
                                {
                                    float ratio = (float)width / totalWidth;
                                    c.RelativeColumn(ratio);
                                }
                            });
                        }
                        else
                        {
                            // Fallback: equal columns
                            int colCount = table.Rows[0].Count;
                            tableDef.ColumnsDefinition(c =>
                            {
                                for (int i = 0; i < colCount; i++)
                                    c.RelativeColumn();
                            });
                        }

                        // Header row
                        if (table.Rows[0].Any(c => c.IsHeader))
                        {
                            tableDef.Header(header =>
                            {
                                foreach (var cell in table.Rows[0])
                                {
                                    header.Cell().Border(1).BorderColor(TextBlack)
                                        .PaddingVertical(2, Unit.Point).PaddingHorizontal(4, Unit.Point)
                                        .Background(White)
                                        .Text(cell.Text)
                                        .FontSize(11).FontFamily("Calibri").FontColor(TextBlack).Bold();
                                }
                            });
                        }

                        // Data rows - CSS style: padding 4px 8px, border 1px solid #CCCCCC, alternating #F2F4F7
                        for (int rowIndex = (table.Rows[0].Any(c => c.IsHeader) ? 1 : 0); rowIndex < table.Rows.Count; rowIndex++)
                        {
                            var row = table.Rows[rowIndex];
                            bool isAlternate = rowIndex % 2 == 0; // Alternate shading (even rows after header)

                            foreach (var cell in row)
                            {
                                var cellBackground = cell.BackgroundColor;
                                if (!cellBackground.HasValue && isAlternate && table.Rows.Count > 1)
                                {
                                    cellBackground = LightGray; // Alternate row shading #F2F4F7
                                }

                                var cellTextColor = cell.TextColor ?? TextBlack;

                                var cellBuilder = tableDef.Cell()
                                        .Border(1).BorderColor(TableBorder) // #CCCCCC
                                        .PaddingVertical(2, Unit.Point).PaddingHorizontal(4, Unit.Point); // Reduced padding: 2px vertical, 4px horizontal

                                if (cellBackground.HasValue)
                                {
                                    cellBuilder = cellBuilder.Background(cellBackground.Value);
                                }

                                // For pricing table, right-align the price column (last column)
                                var textElement = cellBuilder.Text(cell.Text)
                                        .FontSize(10).FontFamily("Calibri").FontColor(cellTextColor).LineHeight(1.4f);

                                if (isPricingTable && cell == row.Last())
                                {
                                    textElement.AlignRight().Bold();
                                }
                                // For scope table, also right-align last column if it's a price/amount column
                                else if (isScopeTable && cell == row.Last() && IsPriceColumn(cell.Text))
                                {
                                    textElement.AlignRight().Bold();
                                }
                            }
                        }
                    });
    }

    private bool IsPriceColumn(string text)
    {
        var trimmed = text.Trim();
        // Check if the cell content looks like a price (contains currency symbol or is numeric with decimals)
        return trimmed.StartsWith("₹") || trimmed.StartsWith("Rs") ||
               System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^\d+(\.\d{1,2})?$") ||
               trimmed.Contains(",") && System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^[\d,\.]+$");
    }

    private void RenderQuotationToGrid(ColumnDescriptor column, TableContent table)
    {
        // Render QUOTATION TO as a 2-column grid layout matching frontend
        if (table.Rows.Count < 2) return;

        var headingRow = table.Rows[0];
        var dataRow = table.Rows[1];

        // Heading row spans both columns
        if (headingRow.Count > 0)
        {
            column.Item().PaddingBottom(6)
                .Text(headingRow[0].Text)
                .FontSize(11).FontFamily("Calibri").FontColor(TextBlack).Bold();
        }

        // Data row has 2 cells: left (Name, Address, Contact, Email) and right (Quotation No, Date)
        if (dataRow.Count >= 2)
        {
            column.Item().Row(row =>
            {
                // Left column - 50%
                row.RelativeItem(1).Column(leftCol =>
                {
                    leftCol.Spacing(3);
                    foreach (var cell in dataRow[0].Text.Split('\n'))
                    {
                        var trimmed = cell.Trim();
                        if (!string.IsNullOrEmpty(trimmed))
                        {
                            // NEW: render only the label portion (e.g. "Name:", "Address:", "Contact No.:",
                            // "Email:", "Dear Sir / Madam,", "Definition:", "Installation pre-requisites...")
                            // in bold, keeping any trailing value at normal weight.
                            if (TryGetBoldLabelPrefix(trimmed, out var leftBoldLabel, out var leftLabelRest))
                            {
                                leftCol.Item().Text(t =>
                                {
                                    t.DefaultTextStyle(TextStyle.Default.FontSize(10).FontFamily("Calibri").FontColor(TextBlack).LineHeight(1.4f));
                                    t.Span(leftBoldLabel).Bold();
                                    if (!string.IsNullOrEmpty(leftLabelRest))
                                    {
                                        t.Span(leftLabelRest);
                                    }
                                });
                                continue;
                            }

                            var isLabel = trimmed.EndsWith(":", StringComparison.Ordinal) ||
                                         trimmed.Equals("Dear Sir / Madam,", StringComparison.OrdinalIgnoreCase) ||
                                         trimmed.Equals("Definition:", StringComparison.OrdinalIgnoreCase) ||
                                         trimmed.Equals("Installation pre-requisites (in case of on-premise Server):", StringComparison.OrdinalIgnoreCase);
                            var textStyle = TextStyle.Default
                                .FontSize(10).FontFamily("Calibri").FontColor(TextBlack).LineHeight(1.4f);
                            if (isLabel) textStyle = textStyle.Bold();
                            leftCol.Item().Text(trimmed).Style(textStyle);
                        }
                    }
                });

                // Right column - 50%
                row.RelativeItem(1).Column(rightCol =>
                {
                    rightCol.Spacing(3);
                    foreach (var cell in dataRow[1].Text.Split('\n'))
                    {
                        var trimmed = cell.Trim();
                        if (!string.IsNullOrEmpty(trimmed))
                        {
                            // NEW: same label-only bold treatment for right column (e.g. "Quotation No.:", "Date:")
                            if (TryGetBoldLabelPrefix(trimmed, out var rightBoldLabel, out var rightLabelRest))
                            {
                                rightCol.Item().Text(t =>
                                {
                                    t.AlignRight();
                                    t.DefaultTextStyle(TextStyle.Default.FontSize(10).FontFamily("Calibri").FontColor(TextBlack).LineHeight(1.4f));
                                    t.Span(rightBoldLabel).Bold();
                                    if (!string.IsNullOrEmpty(rightLabelRest))
                                    {
                                        t.Span(rightLabelRest);
                                    }
                                });
                                continue;
                            }

                            var isLabel = trimmed.EndsWith(":", StringComparison.Ordinal);
                            var textStyle = TextStyle.Default
                                .FontSize(10).FontFamily("Calibri").FontColor(TextBlack).LineHeight(1.4f);
                            if (isLabel) textStyle = textStyle.Bold();
                            rightCol.Item().AlignRight().Text(trimmed).Style(textStyle);
                        }
                    }
                });
            });
        }
    }

    private static bool TryParseColor(string val, out QuestPDFColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(val)) return false;
        var s = val.Trim();
        if (s.StartsWith("#")) s = s[1..];
        if (s.Equals("auto", StringComparison.OrdinalIgnoreCase)) return false;
        if (!(s.Length == 3 || s.Length == 4 || s.Length == 6 || s.Length == 8)) return false;
        // Only hex digits allowed
        for (int i = 0; i < s.Length; i++)
        {
            var c = s[i];
            bool isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
            if (!isHex) return false;
        }
        try
        {
            color = QuestPDFColor.FromHex("#" + s);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

// Helper classes for document structure
public class DocumentContent
{
    public List<IDocumentElement> Elements { get; set; } = new();
}

public interface IDocumentElement
{
}

public class ParagraphContent : IDocumentElement
{
    public string Text { get; set; } = "";
    public bool IsBold { get; set; }
    public bool IsCentered { get; set; }
    public bool IsAllCaps { get; set; }
    public bool IsNumbered { get; set; }
    public int FontSize { get; set; } = 10;
    public string FontFamily { get; set; } = "Calibri";
    public QuestPDFColor? TextColor { get; set; }
    public QuestPDFColor? BackgroundColor { get; set; }
    public float SpacingAfter { get; set; }
    public float SpacingBefore { get; set; }
    public bool HasBottomBorder { get; set; }
    public QuestPDFColor? BorderColor { get; set; }
}

public class TableContent : IDocumentElement
{
    public List<List<CellContent>> Rows { get; set; } = new();
    public List<int> ColumnWidths { get; set; } = new();
    public bool HasBorders { get; set; }
    public QuestPDFColor BorderColor { get; set; } = QuestPDFColors.Grey.Medium;
}

public class CellContent
{
    public string Text { get; set; } = "";
    public bool IsBold { get; set; }
    public bool IsHeader { get; set; }
    public QuestPDFColor? BackgroundColor { get; set; }
    public QuestPDFColor? TextColor { get; set; }
    public float Padding { get; set; } = 4f;
}