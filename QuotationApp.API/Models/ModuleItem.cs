namespace QuotationApp.API.Models;

/// <summary>
/// One row of the company's master "Scope" list.
/// Loaded from Data/modules.json so new modules can be added without a code change.
/// </summary>
public class ModuleItem
{
    public string Pillar { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
}
