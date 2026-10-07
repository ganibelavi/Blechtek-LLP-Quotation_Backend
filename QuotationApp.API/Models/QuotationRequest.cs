using System.ComponentModel.DataAnnotations;

namespace QuotationApp.API.Models;

/// <summary>Payload posted by the React form to generate a quotation.</summary>
public class QuotationRequest
{
    [Required]
    public DateTime ValidationDate { get; set; }

    [Required, StringLength(200)]
    public string OrganizationName { get; set; } = string.Empty;

    [StringLength(150)]
    public string ReferenceBy { get; set; } = string.Empty;

    [StringLength(200)]
    public string CreatedByUser { get; set; } = string.Empty;

    [StringLength(50)]
    public string QuotationNo { get; set; } = string.Empty;

    [Required]
    public DateTime Date { get; set; }

    public DateTime? ExpectedStartDate { get; set; }

    /// <summary>Exact module names as they appear in the master list (Data/modules.json).</summary>
    [Required, MinLength(1, ErrorMessage = "Select at least one module.")]
    public List<string> SelectedModules { get; set; } = new();

    public List<QuotationModuleRequest> ModuleDetails { get; set; } = new();

    public List<AdditionalScopeRequest> AdditionalScopes { get; set; } = new();

    public List<TimeEstimateStageRequest> TimeEstimate { get; set; } = new();

    [Required]
    public QuotationToInfo QuotationTo { get; set; } = new();

    /// <summary>Discount percentage to apply on module prices (0-100).</summary>
    [Range(0, 100, ErrorMessage = "Discount must be between 0 and 100.")]
    public decimal DiscountPercentage { get; set; } = 0;

    /// <summary>Renewal percentage (e.g., 20 for 20%) to apply on module price for renewal years.</summary>
    [Range(0, 100, ErrorMessage = "Renewal percentage must be between 0 and 100.")]
    public decimal RenewalPercentage { get; set; }

    /// <summary>Annual escalation percentage (e.g., 7 for 7%) applied from 3rd renewal year onwards.</summary>
    [Range(0, 100, ErrorMessage = "Annual escalation percentage must be between 0 and 100.")]
    public decimal AnnualEscalationPercentage { get; set; }
}

public class QuotationModuleRequest
{
    [Required, StringLength(200)]
    public string ModuleName { get; set; } = string.Empty;

    [Range(0, int.MaxValue)]
    public int? NoOfUsers { get; set; }

    [Range(0, int.MaxValue)]
    public int? NoOfInstallations { get; set; }

    [Range(0, int.MaxValue)]
    public int? NoOfSites { get; set; }

    [StringLength(30)]
    public string? ImplementationEffortUnit { get; set; }

    [Range(0, 100, ErrorMessage = "Module discount must be between 0 and 100.")]
    public decimal? DiscountPercentage { get; set; }

    public decimal? ModulePriceOverride { get; set; }

    public int? TimelineWeeks { get; set; }

    public int? DeliveryDays { get; set; }
}

public class AdditionalScopeRequest
{
    [StringLength(500)]
    public string Requirement { get; set; } = string.Empty;

    public int? ModulesId { get; set; }

    [StringLength(200)]
    public string Modules { get; set; } = string.Empty;

    [Range(0, int.MaxValue)]
    public int NoOfManpower { get; set; }

    [Range(0, int.MaxValue)]
    public int NoOfDays { get; set; }

    [Range(0, double.MaxValue)]
    public decimal Rate { get; set; }

    [Range(0, double.MaxValue)]
    public decimal Amount { get; set; }

    [Range(0, double.MaxValue)]
    public decimal Price { get; set; }
}

public class QuotationToInfo
{
    [StringLength(150)]
    public string Name { get; set; } = string.Empty;

    [StringLength(400)]
    public string Address { get; set; } = string.Empty;

    [StringLength(30)]
    public string ContactNo { get; set; } = string.Empty;

    [StringLength(150)]
    public string Email { get; set; } = string.Empty;
}

/// <summary>Returned to the frontend after generation — ids used to build download links.</summary>
public class QuotationResult
{
    public string QuotationId { get; set; } = string.Empty;
    public string OrganizationName { get; set; } = string.Empty;
    public string QuotationNo { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public DateTime GeneratedAt { get; set; }
    public string WordDownloadUrl { get; set; } = string.Empty;
    public string PdfDownloadUrl { get; set; } = string.Empty;
}

/// <summary>Represents a quotation entry in the history list.</summary>
public class QuotationModuleDetail
{
    public string ModuleName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal? ModulePrice { get; set; }
    public decimal? ImplementationUnitPrice { get; set; }
    public decimal? ImplementationMultiplier { get; set; }
    public decimal? ImplementationPrice { get; set; }
    public decimal? ModuleSubtotal { get; set; }
    public decimal? DiscountPercentage { get; set; }
    public decimal? DiscountAmount { get; set; }
    public decimal? FinalPrice { get; set; }
    public int? NoOfUsers { get; set; }
    public int? NoOfInstallations { get; set; }
    public int? NoOfSites { get; set; }
    public string? ImplementationEffortUnit { get; set; }
    public int? TimelineWeeks { get; set; }
    public int? DeliveryDays { get; set; }
}

public class QuotationHistoryEntry
{
    public string QuotationId { get; set; } = string.Empty;
    public string OrganizationName { get; set; } = string.Empty;
    public string QuotationNo { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public DateTime ValidationDate { get; set; }
    public DateTime? ExpectedStartDate { get; set; }
    public string QuotationToName { get; set; } = string.Empty;
    public string QuotationToAddress { get; set; } = string.Empty;
    public string QuotationToContactNo { get; set; } = string.Empty;
    public string QuotationToEmail { get; set; } = string.Empty;
    public string ReferenceBy { get; set; } = string.Empty;
    public List<string> Modules { get; set; } = new();
    public List<QuotationModuleDetail> ModuleDetails { get; set; } = new();
    public List<AdditionalScope> AdditionalScopes { get; set; } = new();
    public List<TimeEstimateStageResponse> TimeEstimate { get; set; } = new();
    public DateTime GeneratedAt { get; set; }
    public decimal? DiscountPercentage { get; set; }
}

/// <summary>Request payload for updating discount percentage.</summary>
public class UpdateDiscountRequest
{
    [Required]
    [Range(0, 100, ErrorMessage = "Discount must be between 0 and 100.")]
    public decimal DiscountPercentage { get; set; }
}

/// <summary>Request payload for updating quotation details (validation date, modules, additional scopes).</summary>
public class UpdateQuotationRequest
{
    [Required]
    public DateTime ValidationDate { get; set; }

    public DateTime? ExpectedStartDate { get; set; }

    /// <summary>Exact module names as they appear in the master list.</summary>
    [Required, MinLength(1, ErrorMessage = "Select at least one module.")]
    public List<string> SelectedModules { get; set; } = new();

    public List<QuotationModuleRequest> ModuleDetails { get; set; } = new();

    public List<AdditionalScopeRequest> AdditionalScopes { get; set; } = new();

    public List<TimeEstimateStageRequest> TimeEstimate { get; set; } = new();
}

/// <summary>One module's stage schedule (From week / To week).</summary>
public class TimeEstimateStageRequest
{
    [StringLength(200)]
    public string ModuleName { get; set; } = string.Empty;

    [Required]
    public string StageKey { get; set; } = string.Empty;

    [Range(1, 8, ErrorMessage = "StartWeek must be between 1 and 8.")]
    public int StartWeek { get; set; }

    [Range(1, 8, ErrorMessage = "EndWeek must be between 1 and 8.")]
    public int EndWeek { get; set; }
}

/// <summary>One module's stage schedule in the response model.</summary>
public class TimeEstimateStageResponse
{
    public string ModuleName { get; set; } = string.Empty;
    public string StageKey { get; set; } = string.Empty;
    public int StartWeek { get; set; }
    public int EndWeek { get; set; }
}
