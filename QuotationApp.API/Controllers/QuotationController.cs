using Microsoft.AspNetCore.Mvc;
using QuotationApp.API.Models;
using QuotationApp.API.Services;

namespace QuotationApp.API.Controllers;

[ApiController]
[Route("api/quotation")]
public class QuotationController : ControllerBase
{
    private readonly IQuotationService _quotationService;
    private readonly ILogger<QuotationController> _logger;

    public QuotationController(IQuotationService quotationService, ILogger<QuotationController> logger)
    {
        _quotationService = quotationService;
        _logger = logger;
    }

    /// <summary>Generates a Word + PDF quotation from the submitted form data.</summary>
    [HttpPost("generate")]
    [ProducesResponseType(typeof(QuotationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<QuotationResult>> Generate([FromBody] QuotationRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        try
        {
            var result = await _quotationService.GenerateQuotationAsync(request);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Quotation generation failed for {Org}", request.OrganizationName);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "Could not generate the quotation. Please try again." });
        }
    }

    /// <summary>Gets quotation history with pagination.</summary>
    [HttpGet("history")]
    [ProducesResponseType(typeof(List<QuotationHistoryEntry>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<QuotationHistoryEntry>>> GetHistory([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        try
        {
            var history = await _quotationService.GetHistoryAsync(page, pageSize);
            return Ok(history);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve quotation history");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "Could not retrieve quotation history." });
        }
    }

    /// <summary>Gets dashboard analytics data.</summary>
    [HttpGet("dashboard")]
    [ProducesResponseType(typeof(DashboardData), StatusCodes.Status200OK)]
    public async Task<ActionResult<DashboardData>> GetDashboard()
    {
        try
        {
            var data = await _quotationService.GetDashboardDataAsync();
            return Ok(data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve dashboard data");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "Could not retrieve dashboard data." });
        }
    }

    [HttpGet("{quotationId}/download/word")]
    public IActionResult DownloadWord(string quotationId)
    {
        var path = _quotationService.ResolveFilePath(quotationId, "docx");
        if (path is null) return NotFound(new { error = "Quotation not found." });

        return PhysicalFile(path,
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            $"{quotationId}.docx");
    }

    [HttpGet("{quotationId}/download/pdf")]
    public IActionResult DownloadPdf(string quotationId)
    {
        var path = _quotationService.ResolveFilePath(quotationId, "pdf");
        if (path is null) return NotFound(new { error = "Quotation not found." });

        return PhysicalFile(path, "application/pdf", $"{quotationId}.pdf");
    }

    /// <summary>Updates the discount percentage for an existing quotation and regenerates documents.</summary>
    [HttpPut("{quotationId}/discount")]
    [ProducesResponseType(typeof(QuotationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<QuotationResult>> UpdateDiscount(string quotationId, [FromBody] UpdateDiscountRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        if (request.DiscountPercentage < 0 || request.DiscountPercentage > 100)
            return BadRequest(new { error = "Discount must be between 0 and 100." });

        try
        {
            var result = await _quotationService.UpdateDiscountAsync(quotationId, request.DiscountPercentage);
            if (result is null) return NotFound(new { error = "Quotation not found." });
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update discount for quotation {QuotationId}", quotationId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "Could not update discount. Please try again." });
        }
    }
}
