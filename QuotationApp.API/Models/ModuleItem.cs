using System.ComponentModel.DataAnnotations;

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
    public decimal? ImplementationEffortCost { get; set; }
    public int? ImplementationEffortManDays { get; set; }
    public int? NoOfUsersForSingleInstallation { get; set; }
    public int? TimelineWeeks { get; set; }
    public int? DeliveryDays { get; set; }
}

public class ModuleUpsertRequest
{
    public string Pillar { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    public decimal? Price { get; set; }
    public string? HsnCode { get; set; }
    public string? SacCode { get; set; }
    public bool ReverseChargeDefault { get; set; }
    public decimal? ImplementationEffortCost { get; set; }
    public int? ImplementationEffortManDays { get; set; }
    public int? NoOfUsersForSingleInstallation { get; set; }

    [Range(1, 52, ErrorMessage = "TimelineWeeks must be between 1 and 52.")]
    public int? TimelineWeeks { get; set; }

    [Range(1, 365, ErrorMessage = "DeliveryDays must be between 1 and 365.")]
    public int? DeliveryDays { get; set; }
}
