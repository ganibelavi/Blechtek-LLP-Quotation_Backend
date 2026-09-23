using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.Globalization;
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

    // Text labels that must render with ONLY the label portion bold (e.g. "Name:" bold, "XYZ Corp" normal)
    private static readonly string[] BoldLabelPrefixes = new[]
    {
        "Name:",
        "Address:",
        "Contact No.:",
        "Email:",
        "Quotation No:",
        "Quotation No.:",
        "Date:",
        "Reference:",
        "Subject:",
        "Dear Sir / Madam,",
        "Definition:",
        "Installation pre-requisites (in case of on-premise Server):",
        "For BlechTek Software Solutions LLP",
        "Sushama Inamdar",
        "Customization -",
        "Customization –",
        "Annual License Renewal -",
        "Annual License Renewal –",
        "Support Services -",
        "Support Services –",
        "Payment Terms -",
        "Payment Terms –",
        "Support Level",
        "L1: Telephone Support -",
        "L1: Telephone Support –",
        "L2: Bugs -",
        "L2: Bugs –",
        "L3: Customer Specific Enhancements -",
        "L3: Customer Specific Enhancements –",
        "L4: Product Upgrade -",
        "L4: Product Upgrade –",
        "L5: Implementation -",
        "L5: Implementation –"
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
        // Check multiple locations for the logo. The frontend project folder is
        // named "frontend" (with assets in public/logo, where they are served
        // from); also check the legacy sibling folder name for compatibility.
        var candidates = new[]
        {
            Path.Combine(_contentRoot, "logo", "logo.png"),
            Path.Combine(_contentRoot, "logo", "logo.jpg"),
            Path.GetFullPath(Path.Combine(_contentRoot, "..", "..", "frontend", "public", "logo", "logo.png")),
            Path.GetFullPath(Path.Combine(_contentRoot, "..", "..", "frontend", "public", "logo", "logo.jpg")),
            Path.GetFullPath(Path.Combine(_contentRoot, "..", "..", "frontend", "logo", "logo.png")),
            Path.GetFullPath(Path.Combine(_contentRoot, "..", "..", "frontend", "logo", "logo.jpg")),
            Path.GetFullPath(Path.Combine(_contentRoot, "..", "..", "Blechtek-LLP-Quotation_Frontend", "logo", "logo.png")),
            Path.GetFullPath(Path.Combine(_contentRoot, "..", "..", "Blechtek-LLP-Quotation_Frontend", "logo", "logo.jpg")),
            Path.GetFullPath(Path.Combine(_contentRoot, "..", "..", "Blechtek-LLP-Quotation_Frontend", "public", "logo", "logo.png")),
            Path.GetFullPath(Path.Combine(_contentRoot, "..", "..", "Blechtek-LLP-Quotation_Frontend", "public", "logo", "logo.jpg")),
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private string GetAuthoritySealPath()
    {
        var candidates = new[]
        {
            Path.Combine(_contentRoot, "logo", "Authority_Seal.png"),
            Path.Combine(_contentRoot, "logo", "Authority_Seal.jpg"),
            Path.GetFullPath(Path.Combine(_contentRoot, "..", "..", "frontend", "public", "logo", "Authority_Seal.png")),
            Path.GetFullPath(Path.Combine(_contentRoot, "..", "..", "frontend", "public", "logo", "Authority_Seal.jpg")),
            Path.GetFullPath(Path.Combine(_contentRoot, "..", "..", "frontend", "logo", "Authority_Seal.png")),
            Path.GetFullPath(Path.Combine(_contentRoot, "..", "..", "frontend", "logo", "Authority_Seal.jpg")),
            Path.GetFullPath(Path.Combine(_contentRoot, "..", "..", "Blechtek-LLP-Quotation_Frontend", "public", "logo", "Authority_Seal.png")),
            Path.GetFullPath(Path.Combine(_contentRoot, "..", "..", "Blechtek-LLP-Quotation_Frontend", "public", "logo", "Authority_Seal.jpg")),
            Path.GetFullPath(Path.Combine(_contentRoot, "..", "..", "Blechtek-LLP-Quotation_Frontend", "logo", "Authority_Seal.png")),
            Path.GetFullPath(Path.Combine(_contentRoot, "..", "..", "Blechtek-LLP-Quotation_Frontend", "logo", "Authority_Seal.jpg")),
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private string GetWatermarkPath()
    {

        var candidates = new[]
        {
            Path.Combine(_contentRoot, "logo", "watermark.png"),
            Path.Combine(_contentRoot, "logo", "watermark.jpg"),
            Path.GetFullPath(Path.Combine(_contentRoot, "..", "..", "frontend", "public", "logo", "watermark.png")),
            Path.GetFullPath(Path.Combine(_contentRoot, "..", "..", "frontend", "public", "logo", "watermark.jpg")),
            Path.GetFullPath(Path.Combine(_contentRoot, "..", "..", "frontend", "logo", "watermark.png")),
            Path.GetFullPath(Path.Combine(_contentRoot, "..", "..", "frontend", "logo", "watermark.jpg")),
            Path.GetFullPath(Path.Combine(_contentRoot, "..", "..", "Blechtek-LLP-Quotation_Frontend", "logo", "watermark.png")),
            Path.GetFullPath(Path.Combine(_contentRoot, "..", "..", "Blechtek-LLP-Quotation_Frontend", "logo", "watermark.jpg")),
            Path.GetFullPath(Path.Combine(_contentRoot, "..", "..", "Blechtek-LLP-Quotation_Frontend", "public", "logo", "watermark.png")),
            Path.GetFullPath(Path.Combine(_contentRoot, "..", "..", "Blechtek-LLP-Quotation_Frontend", "public", "logo", "watermark.jpg")),
        };

        return candidates.FirstOrDefault(File.Exists);
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
        // Extract text preserving tabs (w:tab elements) - InnerText doesn't include them
        var text = ExtractTextWithTabs(paragraph);
        if (string.IsNullOrWhiteSpace(text)) return new ParagraphContent();

        var properties = paragraph.ParagraphProperties;
        bool isBold = false;
        bool isCentered = false;
        bool isAllCaps = false;
        bool isNumbered = false;
        int fontSize = 10;
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

    private static string ExtractTextWithTabs(Paragraph paragraph)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var run in paragraph.Elements<Run>())
        {
            foreach (var element in run.Elements())
            {
                if (element is Text textElement)
                {
                    sb.Append(textElement.Text);
                }
                else if (element.LocalName == "tab") // w:tab element
                {
                    sb.Append('\t');
                }
            }
        }
        return sb.ToString().Trim();
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
                            // Display the company logo image from the frontend public/logo
                            // folder (or any of the other checked locations). Fall back to
                            // the company name as text when no logo file is available.
                            if (!string.IsNullOrEmpty(logoPath) && File.Exists(logoPath))
                            {
                                leftCol.Item().Height(30).Image(logoPath);
                            }
                            else
                            {
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

                    headerCol.Item().PaddingTop(8).PaddingBottom(0);
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
                    col.Item().PaddingTop(16).Column(footerCol =>
                        {
                            footerCol.Item().AlignCenter().Text("BlechTek Software Solutions LLP")
                                .FontSize(10).FontFamily("Calibri").FontColor(TextBlack).Bold();
                            footerCol.Item().AlignCenter().Text("Address: S.NO. 257/2/2A/4 ABC Business Center, S Floor, Opp. WindMill Village Road, WindMill Village, Bavdhan, Pune 411021, Maharashtra")
                                .FontSize(9).FontFamily("Calibri").FontColor(TextBlack);
                            footerCol.Item().AlignCenter().Text("LLP No.: ACD-6620 | GST NO.: 27ABCFB0283B1Z0 | MSME Certificate No.: UDYAM-MH-26-0746115")
                                .FontSize(9).FontFamily("Calibri").FontColor(TextBlack);
                            footerCol.Item().PaddingTop(6).Height(6).Row(gradientRow =>
                            {
                                const int segmentCount = 100;
                                for (var segment = 0; segment < segmentCount; segment++)
                                {
                                    gradientRow.RelativeItem()
                                        .Background(GetFooterGradientColor(segment, segmentCount - 1));
                                }
                            });
                        });
                });
            });
        });

        questDocument.GeneratePdf(outputPath);
        await Task.CompletedTask;
    }

    private static QuestPDFColor GetFooterGradientColor(int position, int lastPosition)
    {
        var ratio = lastPosition <= 0 ? 0 : (double)position / lastPosition;
        var red = (int)Math.Round(0x30 + ((0x48 - 0x30) * ratio));
        var green = (int)Math.Round(0x8A + ((0xCA - 0x8A) * ratio));
        var blue = (int)Math.Round(0xEA + ((0xE4 - 0xEA) * ratio));
        return QuestPDFColor.FromHex($"#{red:X2}{green:X2}{blue:X2}");
    }

    private void RenderParagraph(ColumnDescriptor column, ParagraphContent para, int index, List<IDocumentElement> allElements)
    {
        if (string.IsNullOrWhiteSpace(para.Text)) return;

        if (para.Text.Trim().Equals("Sushama Inamdar", StringComparison.OrdinalIgnoreCase) &&
            index > 0 &&
            allElements[index - 1] is ParagraphContent previousParagraph &&
            previousParagraph.Text.Trim().TrimEnd('.').Equals(
                "For BlechTek Software Solutions LLP",
                StringComparison.OrdinalIgnoreCase))
        {
            var signingSealPath = GetAuthoritySealPath();
            if (!string.IsNullOrEmpty(signingSealPath) && File.Exists(signingSealPath))
            {
                column.Item().PaddingTop(10).PaddingBottom(10)
                    .AlignLeft().Width(120).Image(signingSealPath);
            }
            else
            {
                column.Item().Height(40, Unit.Point);
            }
        }

        // Check for section headings (uppercase headings like "QUOTATION TO", "SCOPE OF WORK", etc.)
        var isSectionHeading = IsSectionHeading(para.Text);
        var isQuotationToHeading = para.Text.Trim().StartsWith("QUOTATION TO", StringComparison.OrdinalIgnoreCase);
        var isPricingHeading = IsPricingSectionHeading(para.Text);
        var isScopeHeading = IsScopeSectionHeading(para.Text);

        if (isPricingHeading)
        {
            RenderHeadingWithTextWidthUnderline(column, para.Text.ToUpper(), 24, 8);
            return;
        }

        if (isScopeHeading)
        {
            RenderHeadingWithTextWidthUnderline(column, para.Text.ToUpper(), 2, 2, ensureSpace: true);
            return;
        }

        if (isSectionHeading)
        {
            RenderHeadingWithTextWidthUnderline(column, para.Text.ToUpper(), 24, 8);
            return;
        }

        if (isQuotationToHeading)
        {
            column.Item().PaddingBottom(6)
                .Text(para.Text)
                .FontSize(11).FontFamily("Calibri").FontColor(TextBlack).Bold();
            return;
        }

        // Digitization paragraphs are explanatory body text even when the
        // source document marks the paragraph as bold.
        if (para.Text.Trim().StartsWith("Digitization of", StringComparison.OrdinalIgnoreCase))
        {
            column.Item()
                .Text(para.Text)
                .Style(TextStyle.Default
                    .FontSize(para.FontSize)
                    .FontFamily("Calibri")
                    .FontColor(para.TextColor ?? TextBlack)
                    .LineHeight(1.55f));
            return;
        }

        // Check if we're in the Scope of Work section
        var isScopeOfWork = IsInScopeOfWorkSection(index, allElements);

        if (isScopeOfWork)
        {
            RenderScopeOfWorkBullet(column, para);
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
                         text.StartsWith("Digitization of", StringComparison.OrdinalIgnoreCase) ||
                         text.StartsWith("Deliverables do not include", StringComparison.OrdinalIgnoreCase) ||
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

        if (TryGetBoldLabelPrefix(text, out var boldLabel, out var labelRest) || ContainsTabSeparatedLabels(text))
        {
            RenderParagraphWithLabels(column, para, text);
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

    private static bool ContainsTabSeparatedLabels(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        var segments = text.Split('\t');
        foreach (var segment in segments)
        {
            var trimmed = segment.Trim();
            if (!string.IsNullOrEmpty(trimmed))
            {
                foreach (var prefix in BoldLabelPrefixes)
                {
                    if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
        }
        return false;
    }


    private void RenderParagraphWithLabels(ColumnDescriptor column, ParagraphContent para, string text)
    {
        var segments = text.Split('\t');
        var validSegments = segments
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();

        if (validSegments.Count <= 1)
        {
            // Fallback to single label rendering
            if (TryGetBoldLabelPrefix(text, out var boldLabel, out var labelRest))
            {
                RenderSingleLabel(column, para, boldLabel, labelRest);
            }
            else
            {
                RenderPlainParagraph(column, para);
            }
            return;
        }

        column.Item().Row(row =>
        {
            for (int i = 0; i < validSegments.Count; i++)
            {
                var segment = validSegments[i];
                var isLast = i == validSegments.Count - 1;

                if (TryGetBoldLabelPrefix(segment, out var boldLabel, out var labelRest))
                {
                    // Label + value segment
                    var relativeWidth = isLast ? 1f : 1f; // Equal width for simplicity
                    row.RelativeItem(relativeWidth).Column(col =>
                    {
                        col.Item().Text(t =>
                        {
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
                    });
                }
                else
                {
                    // Plain text segment
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text(segment)
                            .FontSize(para.FontSize)
                            .FontFamily(para.FontFamily)
                            .FontColor(para.TextColor ?? TextBlack)
                            .LineHeight(1.55f);
                    });
                }
            }
        });
    }

    private void RenderSingleLabel(ColumnDescriptor column, ParagraphContent para, string boldLabel, string labelRest)
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
    }

    private void RenderPlainParagraph(ColumnDescriptor column, ParagraphContent para)
    {
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

        var textItem = paragraphContainer.Text(para.Text).Style(textStyle);

        if (para.IsCentered)
            textItem.AlignCenter();
    }

    private bool IsInTermsAndConditionsSection(int currentIndex, List<IDocumentElement> allElements)
    {
        // Look backwards to find if we're after "TERMS AND CONDITIONS" heading and before the next section
        bool foundTermsHeading = false;

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

    private bool IsInScopeOfWorkSection(int currentIndex, List<IDocumentElement> allElements)
    {
        // Look backwards to find if we're after "SCOPE OF WORK" heading and before the next section
        bool foundScopeHeading = false;

        for (int i = currentIndex - 1; i >= 0; i--)
        {
            if (allElements[i] is ParagraphContent prevPara)
            {
                var text = prevPara.Text.Trim().ToUpper();
                if (text == "SCOPE OF WORK")
                {
                    foundScopeHeading = true;
                    break;
                }
                // Stop if we hit another major section heading
                if (IsSectionHeading(prevPara.Text) && text != "SCOPE OF WORK")
                {
                    break;
                }
            }
        }

        if (!foundScopeHeading) return false;

        // Also check we're not past the next section (Deliverables note or next heading)
        for (int i = currentIndex + 1; i < allElements.Count; i++)
        {
            if (allElements[i] is ParagraphContent nextPara)
            {
                var text = nextPara.Text.Trim().ToUpper();
                if (text.StartsWith("DELIVERABLES DO NOT INCLUDE") || IsSectionHeading(nextPara.Text))
                {
                    return false; // We've left the scope of work section
                }
            }
        }

        return foundScopeHeading;
    }

    private void RenderScopeOfWorkBullet(ColumnDescriptor column, ParagraphContent para)
    {
        var text = para.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;

        // Use black bullet character
        const string bullet = "\u2022"; // •

        column.Item().PaddingLeft(20).PaddingTop(4).PaddingBottom(4).Row(row =>
        {
            row.AutoItem().Width(15).AlignRight().Text(bullet)
                .FontSize(10).FontFamily("Calibri").FontColor(TextBlack).Bold();
            row.RelativeItem().PaddingLeft(8).Text(text)
                .FontSize(10).FontFamily("Calibri").FontColor(TextBlack).LineHeight(1.5f);
        });
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

    /// <summary>
    /// Renders a section heading (e.g. "GOALS AND EXPECTATIONS", "SCOPE OF WORK",
    /// "PRICE FOR IMPLEMENTATION") with a bottom border whose width matches the
    /// rendered text width instead of spanning the full page width.
    /// </summary>
    private void RenderHeadingWithTextWidthUnderline(
        ColumnDescriptor column,
        string headingText,
        float paddingTop,
        float paddingBottom,
        bool ensureSpace = false)
    {
        var item = ensureSpace ? column.Item().EnsureSpace(100) : column.Item();
        if (paddingTop > 0) item = item.PaddingTop(paddingTop, Unit.Point);
        if (paddingBottom > 0) item = item.PaddingBottom(paddingBottom, Unit.Point);

        item.Row(row =>
        {
            // AutoItem shrinks to the text width, so the border only spans the text.
            row.AutoItem().BorderBottom(1).BorderColor(TextBlack)
                .PaddingBottom(2).Text(headingText)
                .FontSize(11).FontFamily("Calibri").FontColor(TextBlack).Bold();
        });
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

    private static void AlignPricingCellValues(CellContent particularsCell, CellContent priceCell)
    {
        var particularsLines = SplitCellLines(particularsCell.Text);
        var numericValues = SplitCellLines(priceCell.Text)
            .Where(line => decimal.TryParse(
                line.Trim(),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out _))
            .ToList();

        var valueIndex = 0;
        var alignedParticularsLines = new List<string>();
        var alignedPriceLines = new List<string>();

        foreach (var line in particularsLines)
        {
            var trimmedLine = line.Trim();
            var isPriceLabel =
                trimmedLine.StartsWith("Module Price:", StringComparison.OrdinalIgnoreCase) ||
                trimmedLine.StartsWith("Implementation Total:", StringComparison.OrdinalIgnoreCase) ||
                trimmedLine.StartsWith("Module Subtotal:", StringComparison.OrdinalIgnoreCase) ||
                trimmedLine.StartsWith("Discount (", StringComparison.OrdinalIgnoreCase) ||
                trimmedLine.StartsWith("Module Final Price:", StringComparison.OrdinalIgnoreCase) ||
                trimmedLine.StartsWith("Subtotal:", StringComparison.OrdinalIgnoreCase) ||
                trimmedLine.StartsWith("Final Price:", StringComparison.OrdinalIgnoreCase);

            if (IsZeroDiscountLine(trimmedLine))
            {
                if (isPriceLabel)
                {
                    valueIndex++;
                }

                continue;
            }

            alignedParticularsLines.Add(line);

            if (!isPriceLabel || valueIndex >= numericValues.Count)
            {
                alignedPriceLines.Add(string.Empty);
                continue;
            }

            alignedPriceLines.Add(numericValues[valueIndex++]);
        }

        particularsCell.Text = string.Join("\n", alignedParticularsLines);
        priceCell.Text = string.Join("\n", alignedPriceLines);
    }

    private static bool IsZeroDiscountLine(string text)
    {
        if (!text.StartsWith("Discount (", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var percentStart = text.IndexOf('(') + 1;
        var percentEnd = text.IndexOf('%', percentStart);
        if (percentStart <= 0 || percentEnd <= percentStart)
        {
            return false;
        }

        var percentageText = text.Substring(percentStart, percentEnd - percentStart).Trim();
        return decimal.TryParse(
                   percentageText,
                   NumberStyles.Number,
                   CultureInfo.CurrentCulture,
                   out var percentage) &&
               percentage == 0m;
    }

    private static List<string> SplitCellLines(string text)
    {
        return text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .ToList();
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
            (table.Rows[0][2].Text.Trim().Equals("Price in INR", StringComparison.OrdinalIgnoreCase) ||
             table.Rows[0][2].Text.Trim().Equals("Price (INR)", StringComparison.OrdinalIgnoreCase));

        if (isPricingTable)
        {
            foreach (var pricingRow in table.Rows.Skip(1).Where(row => row.Count >= 3))
            {
                if (SplitCellLines(pricingRow[1].Text)
                    .Any(line => line.Trim().StartsWith("Discount (", StringComparison.OrdinalIgnoreCase)))
                {
                    AlignPricingCellValues(pricingRow[1], pricingRow[2]);
                }
            }
        }

        // Check if this is a scope table (has specific headers for scope)
        var scopeFirstHeader = table.Rows.Count > 0 && table.Rows[0].Count >= 2
            ? NormalizeTableHeader(table.Rows[0][0].Text)
            : string.Empty;
        var scopeSecondHeader = table.Rows.Count > 0 && table.Rows[0].Count >= 2
            ? NormalizeTableHeader(table.Rows[0][1].Text)
            : string.Empty;
        // The Word scope table is a bordered two-column table. Some template
        // revisions use merged or blank header text, so rely on its stable
        // two-column shape as a fallback for applying the scope grid styling.
        var isScopeTable = ((scopeFirstHeader == "srno" || scopeFirstHeader == "sno") &&
            (scopeSecondHeader == "particulars" ||
             scopeSecondHeader == "description" ||
             scopeSecondHeader == "scope")) ||
            (table.Rows[0].Count == 2 && table.Rows[0].Any(c => c.IsHeader));

        // The module-selection table is stored with a wide template grid for
        // Word layout. Use compact PDF proportions so Pillar does not consume
        // the space needed by Module and Selected.
        var isModuleSelectionTable = table.Rows.Count > 0 &&
            table.Rows[0].Count == 3 &&
            table.Rows[0][0].Text.Trim().Equals("Pillar", StringComparison.OrdinalIgnoreCase) &&
            table.Rows[0][1].Text.Trim().Equals("Module", StringComparison.OrdinalIgnoreCase) &&
            table.Rows[0][2].Text.Trim().Equals("Selected", StringComparison.OrdinalIgnoreCase);

        column.Item().Table(tableDef =>
                    {
                        // Define columns based on column widths or table type
                        if (isPricingTable)
                        {
                            tableDef.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn(0.07f); // Sr. No. - 7%
                                c.RelativeColumn(0.73f); // Particulars - 73%
                                c.RelativeColumn(0.20f); // Price in INR - 20%
                            });
                        }
                        else if (isModuleSelectionTable)
                        {
                            tableDef.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn(0.17f); // Pillar - 17%
                                c.RelativeColumn(0.63f); // Module - 63%
                                c.RelativeColumn(0.20f); // Selected - 20%
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

                        // Keep the Scope and Price for Implementation grids consistent:
                        // their header and data borders should both render in black.
                        var dataBorderColor = isScopeTable || isPricingTable || isModuleSelectionTable
                            ? TextBlack
                            : TableBorder;

                        // Data rows - padding 2px 4px with optional alternating background.
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
                                        .Border(1).BorderColor(dataBorderColor)
                                        .PaddingVertical(2, Unit.Point).PaddingHorizontal(4, Unit.Point); // Reduced padding: 2px vertical, 4px horizontal

                                if (cellBackground.HasValue)
                                {
                                    cellBuilder = cellBuilder.Background(cellBackground.Value);
                                }

                                // Pricing-table particulars contain labels such as
                                // "Customization" and "L1: Telephone Support -" that
                                // must retain their bold emphasis in the PDF.
                                string pricingLabel = null;
                                string pricingLabelRest = null;
                                var hasBoldPricingLabel = isPricingTable &&
                                    TryGetBoldLabelPrefix(cell.Text.Trim(), out pricingLabel, out pricingLabelRest);
                                var shouldBoldPricingCell = !isPricingTable ||
                                    IsPricingCellBold(cell.Text);
                                var shouldAlignRight = (isPricingTable && cell == row.Last()) ||
                                    (isScopeTable && cell == row.Last() && IsPriceColumn(cell.Text));
                                var textCell = shouldAlignRight
                                    ? cellBuilder.AlignRight()
                                    : cellBuilder;
                                textCell.Text(text =>
                                {
                                    text.DefaultTextStyle(TextStyle.Default
                                        .FontSize(10)
                                        .FontFamily("Calibri")
                                        .FontColor(cellTextColor)
                                        .LineHeight(1.4f));

                                    if (hasBoldPricingLabel)
                                    {
                                        var labelSpan = text.Span(pricingLabel);
                                        if (cell.IsBold || isPricingTable)
                                        {
                                            labelSpan.Bold();
                                        }
                                        if (!string.IsNullOrEmpty(pricingLabelRest))
                                        {
                                            var restSpan = text.Span(pricingLabelRest);
                                            if (cell.IsBold && shouldBoldPricingCell)
                                            {
                                                restSpan.Bold();
                                            }
                                        }
                                    }
                                    else
                                    {
                                        var cellSpan = text.Span(cell.Text);
                                        if (cell.IsBold && shouldBoldPricingCell)
                                        {
                                            cellSpan.Bold();
                                        }
                                    }
                                });
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

    private static bool IsPricingCellBold(string text)
    {
        var trimmed = text.Trim();
        return trimmed.EndsWith(":", StringComparison.Ordinal) &&
            (trimmed.Equals("Overall Calculation:", StringComparison.OrdinalIgnoreCase) ||
             trimmed.Equals("Final Price:", StringComparison.OrdinalIgnoreCase) ||
             trimmed.Equals("Module Final Price:", StringComparison.OrdinalIgnoreCase) ||
             trimmed.StartsWith("Product License -", StringComparison.OrdinalIgnoreCase) ||
             trimmed.EndsWith(":", StringComparison.Ordinal) &&
             !trimmed.StartsWith("No. of ", StringComparison.OrdinalIgnoreCase) &&
             !trimmed.StartsWith("Implementation Effort:", StringComparison.OrdinalIgnoreCase) &&
             !trimmed.StartsWith("Module Price:", StringComparison.OrdinalIgnoreCase) &&
             !trimmed.StartsWith("Implementation Total:", StringComparison.OrdinalIgnoreCase) &&
             !trimmed.StartsWith("Module Subtotal:", StringComparison.OrdinalIgnoreCase) &&
             !trimmed.StartsWith("Discount (", StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeTableHeader(string text)
    {
        return System.Text.RegularExpressions.Regex.Replace(text ?? string.Empty, "[^A-Za-z0-9]", string.Empty)
            .ToLowerInvariant();
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
            var leftLines = SplitQuotationMetadataLines(dataRow[0].Text);
            var rightLines = SplitQuotationMetadataLines(dataRow[1].Text);
            var dateLines = leftLines
                .Where(line => line.StartsWith("Date:", StringComparison.OrdinalIgnoreCase))
                .ToList();
            leftLines = leftLines
                .Where(line => !line.StartsWith("Date:", StringComparison.OrdinalIgnoreCase))
                .ToList();
            rightLines.AddRange(dateLines);

            column.Item().Row(row =>
            {
                // Left column - 50%
                row.RelativeItem(1).Column(leftCol =>
                {
                    leftCol.Spacing(3);
                    foreach (var cell in leftLines)
                    {
                        var trimmed = cell.Trim();
                        if (!string.IsNullOrEmpty(trimmed))
                        {
                            // NEW: render only the label portion (e.g. "Name:", "Address:", "Contact No.:",
                            // "Email:", "Dear Sir / Madam,", "Definition:", "Installation pre-requisites...")
                            // in bold, keeping any trailing value at normal weight.
                            if (TryGetBoldLabelPrefix(trimmed, out var leftBoldLabel, out var leftLabelRest))
                            {
                                if (leftBoldLabel.Equals("Email:", StringComparison.OrdinalIgnoreCase))
                                {
                                    leftCol.Item().Text(leftBoldLabel)
                                        .FontSize(10).FontFamily("Calibri").FontColor(TextBlack).Bold();
                                    if (!string.IsNullOrWhiteSpace(leftLabelRest))
                                    {
                                        leftCol.Item().Text(leftLabelRest.Trim())
                                            .FontSize(10).FontFamily("Calibri").FontColor(TextBlack).LineHeight(1.4f);
                                    }
                                    continue;
                                }

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
                    foreach (var cell in rightLines)
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

    private static List<string> SplitQuotationMetadataLines(string text)
    {
        var normalized = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace('|', '\n');

        return System.Text.RegularExpressions.Regex.Split(
                normalized,
                @"(?=(?:Name|Address|Contact\s+No\.|Email|Quotation\s+No\.?|Date)\s*:)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();
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