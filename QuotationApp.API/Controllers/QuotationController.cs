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
    private readonly IEmailService _emailService;

    public QuotationController(IQuotationService quotationService, ILogger<QuotationController> logger, IEmailService emailService)
    {
        _quotationService = quotationService;
        _logger = logger;
        _emailService = emailService;
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

    [HttpGet("{quotationId}")]
    [ProducesResponseType(typeof(QuotationHistoryEntry), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<QuotationHistoryEntry>> GetQuotation(string quotationId)
    {
        try
        {
            var quotation = await _quotationService.GetQuotationAsync(quotationId);
            if (quotation is null)
                return NotFound(new { error = "Quotation not found." });

            return Ok(quotation);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve quotation {QuotationId}", quotationId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "Could not retrieve quotation details." });
        }
    }

    [HttpGet("{quotationId}/revisions")]
    [ProducesResponseType(typeof(List<QuotationRevisionEntry>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<QuotationRevisionEntry>>> GetRevisions(string quotationId)
    {
        try
        {
            return Ok(await _quotationService.GetRevisionsAsync(quotationId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve revisions for quotation {QuotationId}", quotationId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "Could not retrieve quotation revisions." });
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

    /// <summary>Send the quotation PDF by email to the specified recipient.</summary>
    public class SendEmailRequest
    {
        public string RecipientEmail { get; set; }
        public string Subject { get; set; }
        public string Message { get; set; }
        public bool AttachPdf { get; set; } = true;
    }

    [HttpPost("{quotationId}/send-email")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SendEmail(string quotationId, [FromBody] SendEmailRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.RecipientEmail))
            return BadRequest(new { error = "RecipientEmail is required." });

        try
        {
            // Try to resolve the PDF attachment if requested
            string attachment = null;
            if (request.AttachPdf)
            {
                attachment = _quotationService.ResolveFilePath(quotationId, "pdf");
                if (attachment is null) return NotFound(new { error = "Quotation PDF not found." });
            }

            var subject = string.IsNullOrWhiteSpace(request.Subject) ? $"Quotation {quotationId} from BlechTek" : request.Subject;
            var message = request.Message ?? "Please find attached the quotation.";

            await _emailService.SendQuotationEmailAsync(quotationId, request.RecipientEmail, subject, message, attachment);
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send quotation {QuotationId} email", quotationId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to send email." });
        }
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

    /// <summary>Updates quotation details (validation date, modules) and regenerates documents.</summary>
    [HttpPut("{quotationId}")]
    [ProducesResponseType(typeof(QuotationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<QuotationResult>> UpdateQuotation(string quotationId, [FromBody] UpdateQuotationRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        try
        {
            var result = await _quotationService.UpdateQuotationAsync(quotationId, request.ValidationDate, request.SelectedModules);
            if (result is null) return NotFound(new { error = "Quotation not found." });
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update quotation {QuotationId}", quotationId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "Could not update quotation. Please try again." });
        }
    }

    /// <summary>Gets the next auto-generated quotation number without creating a quotation.</summary>
    [HttpGet("next-quotation-no")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<ActionResult<object>> GetNextQuotationNo()
    {
        try
        {
            var nextQuotationNo = await _quotationService.GetNextQuotationNoAsync();
            return Ok(new { quotationNo = nextQuotationNo });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get next quotation number");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "Could not get next quotation number." });
        }
    }
}
