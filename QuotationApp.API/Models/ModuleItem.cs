namespace QuotationApp.API.Models;

/// <summary>
/// One row of the company's master "Scope" list.
/// Loaded from Data/modules.json so new modules can be added without a code change.
/// </summary>
public class ModuleItem
{
    public int Id { get; set; }
    public string Pillar { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    public decimal? Price { get; set; }
    public string? HsnCode { get; set; }
    public string? SacCode { get; set; }
    public bool ReverseChargeDefault { get; set; }
}

public class ModuleUpsertRequest
{
    public string Pillar { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    public decimal? Price { get; set; }
    public string? HsnCode { get; set; }
    public string? SacCode { get; set; }
    public bool ReverseChargeDefault { get; set; }
}
