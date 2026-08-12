using System.ComponentModel.DataAnnotations;

namespace QuotationApp.API.Models;

/// <summary>Payload posted by the React form to generate a quotation.</summary>
public class QuotationRequest
{
    [Required]
    public DateTime ValidationDate { get; set; }

    [Required, StringLength(200)]
    public string OrganizationName { get; set; } = string.Empty;

    /// <summary>Exact module names as they appear in the master list (Data/modules.json).</summary>
    [Required, MinLength(1, ErrorMessage = "Select at least one module.")]
    public List<string> SelectedModules { get; set; } = new();

    [Required]
    public QuotationToInfo QuotationTo { get; set; } = new();
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
    public DateTime GeneratedAt { get; set; }
    public string WordDownloadUrl { get; set; } = string.Empty;
    public string PdfDownloadUrl { get; set; } = string.Empty;
}
