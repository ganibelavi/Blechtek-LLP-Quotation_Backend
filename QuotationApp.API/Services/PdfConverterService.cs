using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
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
/// </summary>
public class PdfConverterService : IPdfConverterService
{
    // Color constants matching the Word template
    private static readonly QuestPDFColor DarkBlue = QuestPDFColor.FromHex("#65aadb");
    private static readonly QuestPDFColor Teal = QuestPDFColor.FromHex("#65aadb");
    private static readonly QuestPDFColor White = QuestPDFColor.FromHex("#FFFFFF");
    private static readonly QuestPDFColor LightGray = QuestPDFColor.FromHex("#F2F4F7");
    private static readonly QuestPDFColor TableBorder = QuestPDFColor.FromHex("#CCCCCC");
    private static readonly QuestPDFColor TextBlack = QuestPDFColor.FromHex("#000000");

    public PdfConverterService(IOptions<QuotationSettings> settings)
    {
        QuestPDF.Settings.License = LicenseType.Community;
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
                    if (!backgroundColor.HasValue) backgroundColor = DarkBlue;
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
        var questDocument = QuestPDF.Fluent.Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(25, Unit.Millimetre); // ~1100 twips = 25mm
                page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Calibri").FontColor(TextBlack));

                page.Header().Height(20).AlignCenter().Text("").FontSize(8);

                page.Content().Column(column =>
                {
                    // Process paragraphs and tables in order
                    foreach (var element in content.Elements)
                    {
                        if (element is ParagraphContent para)
                        {
                            if (string.IsNullOrWhiteSpace(para.Text)) continue;

                            var paragraphContainer = column.Item();

                            if (para.SpacingBefore > 0)
                                paragraphContainer = paragraphContainer.PaddingTop(para.SpacingBefore, Unit.Point);

                            if (para.SpacingAfter > 0)
                                paragraphContainer = paragraphContainer.PaddingBottom(para.SpacingAfter, Unit.Point);

                            if (para.HasBottomBorder)
                                paragraphContainer = paragraphContainer.BorderBottom(1).BorderColor(para.BorderColor ?? Teal);

                            var textStyle = TextStyle.Default
                                .FontSize(para.FontSize)
                                .FontFamily(para.FontFamily)
                                .FontColor(para.TextColor ?? TextBlack);

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
                        else if (element is TableContent table)
                        {
                            if (table.Rows.Count == 0) continue;

                            // If table has no visible borders, render as paragraphs (for QUOTATION TO section)
                            if (!table.HasBorders)
                            {
                                foreach (var row in table.Rows)
                                {
                                    foreach (var cell in row)
                                    {
                                        if (string.IsNullOrWhiteSpace(cell.Text)) continue;

                                        var cellStyle = TextStyle.Default
                                            .FontSize(9)
                                            .FontFamily("Calibri")
                                            .FontColor(cell.TextColor ?? TextBlack);

                                        if (cell.IsBold) cellStyle = cellStyle.Bold();

                                        var cellContainer = column.Item().PaddingTop(2).PaddingBottom(2);
                                        if (cell.BackgroundColor.HasValue)
                                            cellContainer = cellContainer.Background(cell.BackgroundColor.Value);
                                        
                                        cellContainer.Text(cell.Text).Style(cellStyle);
                                    }
                                }
                                continue;
                            }

                            column.Item().PaddingTop(10).Table(tableDef =>
                            {
                                // Define columns based on column widths
                                if (table.ColumnWidths.Count > 0)
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
                                                header.Cell().Border(0.5f).BorderColor(table.BorderColor)
                                                    .Padding(cell.Padding, Unit.Point)
                                                    .Background(cell.BackgroundColor ?? DarkBlue)
                                                    .Text(cell.Text)
                                                    .FontSize(9)
                                                    .FontFamily("Calibri")
                                                    .FontColor(cell.TextColor ?? White)
                                                    .Bold();
                                            }
                                        });
                                }

                                // Data rows
                                for (int rowIndex = (table.Rows[0].Any(c => c.IsHeader) ? 1 : 0); rowIndex < table.Rows.Count; rowIndex++)
                                {
                                    var row = table.Rows[rowIndex];
                                    bool isAlternate = rowIndex % 2 == 0; // Alternate shading

                                    foreach (var cell in row)
                                    {
                                        var cellBackground = cell.BackgroundColor;
                                        if (!cellBackground.HasValue && isAlternate && table.Rows.Count > 1)
                                        {
                                            cellBackground = LightGray; // Alternate row shading
                                        }

                                        var cellTextColor = cell.TextColor ?? TextBlack;

                                        var cellBuilder = tableDef.Cell()
                                                .Border(0.5f).BorderColor(table.BorderColor)
                                                .Padding(cell.Padding, Unit.Point);

                                        if (cellBackground.HasValue)
                                        {
                                            cellBuilder = cellBuilder.Background(cellBackground.Value);
                                        }

                                        cellBuilder.Text(cell.Text)
                                                .FontSize(9)
                                                .FontFamily("Calibri")
                                                .FontColor(cellTextColor);
                                    }
                                }
                            });
                        }
                    }
                });

                page.Footer().Height(50).Column(col =>
                {
                    col.Item().AlignCenter().Text("BlechTek Software Solutions LLP").FontSize(8).FontColor(Colors.Grey.Darken2);
                    col.Item().AlignCenter().Text("Address: S.NO. 257/2/2A/4 ABC Business Center, S Floor, Opp. WindMill Village Road, WindMill Village, Bavdhan, Pune 411021, Maharashtra").FontSize(7).FontColor(Colors.Grey.Darken2);
                    col.Item().AlignCenter().Text("LLP No.: ACD-6620 | GST NO.: 27ABCFB0283B1Z0 | MSME Certificate No.: UDYAM-MH-26-0746115").FontSize(7).FontColor(Colors.Grey.Darken2);
                });
            });
        });

        questDocument.GeneratePdf(outputPath);
        await Task.CompletedTask;
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