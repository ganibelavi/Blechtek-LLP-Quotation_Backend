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
            var runColor = runProps?.Color?.Val?.Value;
            if (!string.IsNullOrWhiteSpace(runColor))
            {
                if (TryParseColor(runColor, out var qc))
                    textColor = qc;
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
                var cellText = cell.InnerText.Trim();
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
                        var cellRunColor = runProps?.Color?.Val?.Value;
                        if (!string.IsNullOrWhiteSpace(cellRunColor) && TryParseColor(cellRunColor, out var runQc))
                            textColor = runQc;
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
                    if (!backgroundColor.HasValue) backgroundColor = PrimaryBlue;
                    if (!textColor.HasValue) textColor = White;
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
                page.DefaultTextStyle(x => x.FontSize(11).FontFamily("Calibri").FontColor(TextBlack).LineHeight(1.5f));

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
                                    .FontSize(14).FontFamily("Calibri").FontColor(PrimaryBlue).SemiBold();
                            }
                        });
                        headerRow.RelativeItem().AlignRight().Column(rightCol =>
                        {
                            rightCol.Item().Text("QUOTATION")
                                .FontSize(16).FontFamily("Calibri").FontColor(PrimaryBlue).Bold();
                        });
                    });

                    headerCol.Item().PaddingTop(8).BorderBottom(2).BorderColor(PrimaryBlue).PaddingBottom(0);
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
                    col.Item().PaddingTop(16).BorderTop(1).BorderColor(PrimaryBlue)
                        .Column(footerCol =>
                        {
                            footerCol.Item().AlignCenter().Text("BlechTek Software Solutions LLP")
                                .FontSize(10).FontFamily("Calibri").FontColor(PrimaryBlue).Bold();
                            footerCol.Item().AlignCenter().Text("Address: S.NO. 257/2/2A/4 ABC Business Center, S Floor, Opp. WindMill Village Road, WindMill Village, Bavdhan, Pune 411021, Maharashtra")
                                .FontSize(9).FontFamily("Calibri").FontColor(MediumText);
                            footerCol.Item().AlignCenter().Text("LLP No.: ACD-6620 | GST NO.: 27ABCFB0283B1Z0 | MSME Certificate No.: UDYAM-MH-26-0746115")
                                .FontSize(9).FontFamily("Calibri").FontColor(MediumText);
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

        if (isSectionHeading)
        {
            column.Item().PaddingTop(16).PaddingBottom(6).BorderBottom(1).BorderColor(PrimaryBlue).PaddingBottom(3)
                .Text(para.Text.ToUpper())
                .FontSize(11).FontFamily("Calibri").FontColor(PrimaryBlue).Bold();
            return;
        }

        if (isQuotationToHeading)
        {
            column.Item().PaddingBottom(6)
                .Text(para.Text)
                .FontSize(11).FontFamily("Calibri").FontColor(PrimaryBlue).Bold();
            return;
        }

        if (isNote)
        {
            column.Item().PaddingTop(16).PaddingBottom(16).PaddingLeft(12).BorderLeft(3).BorderColor(PrimaryBlue)
                .Background(NoteBackground).Padding(10, Unit.Point).PaddingRight(12, Unit.Point)
                .Text(para.Text)
                .FontSize(11).FontFamily("Calibri").FontColor(DarkText).Italic().LineHeight(1.55f);
            return;
        }

        // Check if we're in the Terms and Conditions section
        var isTermsAndConditions = IsInTermsAndConditionsSection(index, allElements);

        if (isTermsAndConditions)
        {
            RenderTermsAndConditionsItem(column, para, index, allElements);
            return;
        }

        var paragraphContainer = column.Item();

        if (para.SpacingBefore > 0)
            paragraphContainer = paragraphContainer.PaddingTop(para.SpacingBefore, Unit.Point);

        if (para.SpacingAfter > 0)
            paragraphContainer = paragraphContainer.PaddingBottom(para.SpacingAfter, Unit.Point);

        if (para.HasBottomBorder)
            paragraphContainer = paragraphContainer.BorderBottom(1).BorderColor(para.BorderColor ?? PrimaryBlue);

        var textStyle = TextStyle.Default
            .FontSize(para.FontSize)
            .FontFamily(para.FontFamily)
            .FontColor(para.TextColor ?? TextBlack)
            .LineHeight(1.55f);

        if (para.IsBold) textStyle = textStyle.Bold();

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
                // Stop if we hit another major section heading
                if (IsSectionHeading(prevPara.Text) && text != "TERMS AND CONDITIONS")
                {
                    break;
                }
            }
        }

        if (!foundTermsHeading) return false;

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
                            contentCol.Item().PaddingTop(2).Text(body)
                                .FontSize(10).FontFamily("Calibri").FontColor(DarkText).LineHeight(1.5f);
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
            // Regular paragraph within terms section
            column.Item().PaddingLeft(30).PaddingTop(4).Text(text)
                .FontSize(10).FontFamily("Calibri").FontColor(DarkText).LineHeight(1.5f);
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

        column.Item().PaddingTop(16).Table(tableDef =>
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
                        header.Cell().Border(0.5f).BorderColor(TableBorder)
                            .Padding(cell.Padding, Unit.Point)
                            .Background(cell.BackgroundColor ?? PrimaryBlue)
                            .Text(cell.Text)
                            .FontSize(10).FontFamily("Calibri").FontColor(cell.TextColor ?? White).Bold();
                    }
                });
            }

            // Data rows
            for (int rowIndex = (table.Rows[0].Any(c => c.IsHeader) ? 1 : 0); rowIndex < table.Rows.Count; rowIndex++)
            {
                var row = table.Rows[rowIndex];
                bool isAlternate = rowIndex % 2 == 0; // Alternate shading (even rows after header)

                foreach (var cell in row)
                {
                    var cellBackground = cell.BackgroundColor;
                    if (!cellBackground.HasValue && isAlternate && table.Rows.Count > 1)
                    {
                        cellBackground = LightGray; // Alternate row shading
                    }

                    var cellTextColor = cell.TextColor ?? TextBlack;

                    var cellBuilder = tableDef.Cell()
                            .Border(0.5f).BorderColor(TableBorder)
                            .Padding(cell.Padding, Unit.Point);

                    if (cellBackground.HasValue)
                    {
                        cellBuilder = cellBuilder.Background(cellBackground.Value);
                    }

                    // For pricing table, right-align the price column
                    var textElement = cellBuilder.Text(cell.Text)
                            .FontSize(10).FontFamily("Calibri").FontColor(cellTextColor).LineHeight(1.4f);

                    if (isPricingTable && cell == row.Last())
                    {
                        textElement.AlignRight().Bold();
                    }
                }
            }
        });
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
                .FontSize(11).FontFamily("Calibri").FontColor(PrimaryBlue).Bold();
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
                            leftCol.Item().Text(trimmed)
                                .FontSize(10).FontFamily("Calibri").FontColor(PrimaryBlue).LineHeight(1.4f);
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
                            rightCol.Item().AlignRight().Text(trimmed)
                                .FontSize(10).FontFamily("Calibri").FontColor(PrimaryBlue).LineHeight(1.4f);
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