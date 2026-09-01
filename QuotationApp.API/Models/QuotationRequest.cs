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

    /// <summary>Exact module names as they appear in the master list (Data/modules.json).</summary>
    [Required, MinLength(1, ErrorMessage = "Select at least one module.")]
    public List<string> SelectedModules { get; set; } = new();

    [Required]
    public QuotationToInfo QuotationTo { get; set; } = new();

    /// <summary>Discount percentage to apply on module prices (0-100).</summary>
    [Range(0, 100, ErrorMessage = "Discount must be between 0 and 100.")]
    public decimal DiscountPercentage { get; set; } = 0;
}

public class QuotationToInfo
{
    [Required, StringLength(150)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(400)]
    public string Address { get; set; } = string.Empty;

    [Required, Phone, StringLength(30)]
    public string ContactNo { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(150)]
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
}

public class QuotationHistoryEntry
{
    public string QuotationId { get; set; } = string.Empty;
    public string OrganizationName { get; set; } = string.Empty;
    public string QuotationNo { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public DateTime ValidationDate { get; set; }
    public string QuotationToName { get; set; } = string.Empty;
    public string QuotationToAddress { get; set; } = string.Empty;
    public string QuotationToContactNo { get; set; } = string.Empty;
    public string QuotationToEmail { get; set; } = string.Empty;
    public string ReferenceBy { get; set; } = string.Empty;
    public List<string> Modules { get; set; } = new();
    public List<QuotationModuleDetail> ModuleDetails { get; set; } = new();
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

/// <summary>Request payload for updating quotation details (validation date, modules).</summary>
public class UpdateQuotationRequest
{
    [Required]
    public DateTime ValidationDate { get; set; }

    /// <summary>Exact module names as they appear in the master list.</summary>
    [Required, MinLength(1, ErrorMessage = "Select at least one module.")]
    public List<string> SelectedModules { get; set; } = new();
}
