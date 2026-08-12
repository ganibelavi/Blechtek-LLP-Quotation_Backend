namespace QuotationApp.API.Services;

/// <summary>Bound from the "QuotationSettings" section of appsettings.json.</summary>
public class QuotationSettings
{
    public string TemplatePath { get; set; } = "Templates/QuotationTemplate.docx";
    public string OutputFolder { get; set; } = "Generated";
    public string ModulesFile { get; set; } = "Data/modules.json";
    public string SofficePath { get; set; } = "soffice";
    public string CompanyName { get; set; } = "BlechTek Software Solutions LLP";
}
