namespace QuotationApp.API.Services;

/// <summary>Bound from the "QuotationSettings" section of appsettings.json.</summary>
public class QuotationSettings
{
    public string TemplatePath { get; set; } = "Templates/QuotationTemplate.docx";
    public string OutputFolder { get; set; } = "Generated";
    public string SofficePath { get; set; } = "soffice";
    public string CompanyName { get; set; } = "BlechTek Software Solutions LLP";

    /// <summary>
    /// Prefix for the quotation number (e.g., "BTSS").
    /// </summary>
    public string QuotationNoPrefix { get; set; } = "BTSS";

    /// <summary>
    /// Financial year format (e.g., "FY2025-26").
    /// </summary>
    public string FinancialYear { get; set; } = "FY2025-26";

    /// <summary>
    /// Sequence prefix (e.g., "PR").
    /// </summary>
    public string SequencePrefix { get; set; } = "PR";

    /// <summary>
    /// Number of digits for the sequence (e.g., 4 for 0001, 0002).
    /// </summary>
    public int SequenceDigits { get; set; } = 4;
}
