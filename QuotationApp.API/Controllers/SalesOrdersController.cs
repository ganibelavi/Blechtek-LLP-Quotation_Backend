using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuotationApp.API.Data;
using QuotationApp.API.Models;
using QuotationApp.API.Services;
using System.Security.Claims;

namespace QuotationApp.API.Controllers;

[ApiController]
[Route("api/sales-orders")]
public class SalesOrdersController : ControllerBase
{
    private readonly ISalesOrderService _salesOrderService;
    private readonly QuotationDbContext _db;

    public SalesOrdersController(ISalesOrderService salesOrderService, QuotationDbContext db)
    {
        _salesOrderService = salesOrderService;
        _db = db;
    }

    private int GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value
            ?? User.FindFirst("id")?.Value;

        if (int.TryParse(userIdClaim, out var userId))
        {
            return userId;
        }

        var email = User.FindFirst(ClaimTypes.Email)?.Value
            ?? User.FindFirst(ClaimTypes.Name)?.Value;

        if (!string.IsNullOrWhiteSpace(email))
        {
            var user = _db.Users.AsNoTracking().FirstOrDefault(u => u.Email == email);
            if (user != null)
            {
                return user.Id;
            }
        }

        return 0;
    }

    [HttpGet]
    public async Task<ActionResult<PagedSalesOrderResultDto>> GetPaged(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        [FromQuery] int? customerId = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] string? search = null)
    {
        var result = await _salesOrderService.GetPagedAsync(page, pageSize, status, customerId, fromDate, toDate, search);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<SalesOrderDetailDto>> GetById(int id)
    {
        var result = await _salesOrderService.GetByIdAsync(id);
        return result is null ? NotFound(new { error = "Sales order not found." }) : Ok(result);
    }

    [HttpGet("po-preview/{poId:int}")]
    public async Task<ActionResult<SalesOrderPreviewDto>> GetPoPreview(int poId)
    {
        try
        {
            var result = await _salesOrderService.GetPreviewAsync(poId);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("from-po/{poId:int}")]
    public async Task<ActionResult<SalesOrderDetailDto>> CreateFromPurchaseOrder(int poId)
    {
        var userId = GetCurrentUserId();
        if (userId <= 0)
        {
            return Unauthorized(new { error = "A valid authenticated user is required." });
        }

        try
        {
            var result = await _salesOrderService.CreateFromPurchaseOrderAsync(poId, userId);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (DbUpdateException)
        {
            return Conflict(new { error = "A sales order for this purchase order already exists or a related record changed." });
        }
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<SalesOrderDetailDto>> Update(int id, [FromBody] SalesOrderUpdateRequest request)
    {
        if (request is null)
        {
            return BadRequest(new { error = "Sales order update payload is required." });
        }

        var userId = GetCurrentUserId();
        if (userId <= 0)
        {
            return Unauthorized(new { error = "A valid authenticated user is required." });
        }

        try
        {
            var result = await _salesOrderService.UpdateAsync(id, request, userId);
            return result is null
                ? NotFound(new { error = "Sales order not found." })
                : Ok(result);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { error = "The sales order changed while it was being saved. Reload and try again." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{id:int}/submit")]
    public async Task<ActionResult<SalesOrderDetailDto>> Submit(int id)
    {
        var userId = GetCurrentUserId();
        if (userId <= 0)
        {
            return Unauthorized(new { error = "A valid authenticated user is required." });
        }

        try
        {
            var result = await _salesOrderService.SubmitAsync(id, userId);
            return result is null
                ? NotFound(new { error = "Sales order not found." })
                : Ok(result);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { error = "The sales order changed while being submitted. Reload and try again." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{id:int}/verify")]
    public async Task<ActionResult<SalesOrderDetailDto>> Verify(int id, [FromBody] SalesOrderVerifyRequest request)
    {
        if (request is null)
        {
            return BadRequest(new { error = "Verification payload is required." });
        }

        var userId = GetCurrentUserId();
        if (userId <= 0)
        {
            return Unauthorized(new { error = "A valid authenticated user is required." });
        }

        try
        {
            var result = await _salesOrderService.VerifyAsync(id, request.Action, request.Remarks, userId);
            return result is null
                ? NotFound(new { error = "Sales order not found." })
                : Ok(result);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { error = "The sales order changed while being verified. Reload and try again." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{id:int}/confirm")]
    public async Task<ActionResult<SalesOrderDetailDto>> Confirm(int id)
    {
        var userId = GetCurrentUserId();
        if (userId <= 0)
        {
            return Unauthorized(new { error = "A valid authenticated user is required." });
        }

        try
        {
            var result = await _salesOrderService.ConfirmAsync(id, userId);
            return result is null
                ? NotFound(new { error = "Sales order not found." })
                : Ok(result);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { error = "The sales order changed while being confirmed. Reload and try again." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{id:int}/hold")]
    public async Task<ActionResult<SalesOrderDetailDto>> Hold(int id, [FromBody] SalesOrderReasonRequest request)
    {
        if (request is null)
        {
            return BadRequest(new { error = "Reason is required." });
        }

        var userId = GetCurrentUserId();
        if (userId <= 0)
        {
            return Unauthorized(new { error = "A valid authenticated user is required." });
        }

        try
        {
            var result = await _salesOrderService.HoldAsync(id, request.Reason, userId);
            return result is null
                ? NotFound(new { error = "Sales order not found." })
                : Ok(result);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { error = "The sales order changed while being placed on hold. Reload and try again." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{id:int}/cancel")]
    public async Task<ActionResult<SalesOrderDetailDto>> Cancel(int id, [FromBody] SalesOrderReasonRequest request)
    {
        if (request is null)
        {
            return BadRequest(new { error = "Reason is required." });
        }

        var userId = GetCurrentUserId();
        if (userId <= 0)
        {
            return Unauthorized(new { error = "A valid authenticated user is required." });
        }

        try
        {
            var result = await _salesOrderService.CancelAsync(id, request.Reason, userId);
            return result is null
                ? NotFound(new { error = "Sales order not found." })
                : Ok(result);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { error = "The sales order changed while being cancelled. Reload and try again." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{id:int}/billing-schedule")]
    public async Task<ActionResult<SalesOrderDetailDto>> UpdateBillingSchedule(
        int id,
        [FromBody] SalesOrderBillingScheduleRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId <= 0)
        {
            return Unauthorized(new { error = "A valid authenticated user is required." });
        }

        try
        {
            var result = await _salesOrderService.UpdateBillingScheduleAsync(id, request, userId);
            return result is null
                ? NotFound(new { error = "Sales order not found." })
                : Ok(result);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { error = "The sales order changed while the billing schedule was being saved. Reload and try again." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{id:int}/generate-invoice")]
    public async Task<ActionResult<SalesOrderInvoiceResultDto>> GenerateInvoice(
        int id,
        [FromBody] SalesOrderGenerateInvoiceRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId <= 0)
        {
            return Unauthorized(new { error = "A valid authenticated user is required." });
        }

        try
        {
            var result = await _salesOrderService.GenerateInvoiceAsync(id, request, userId);
            return result is null
                ? NotFound(new { error = "Sales order not found." })
                : Ok(result);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { error = "The sales order changed while the invoice was being generated. Reload and try again." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (DbUpdateException)
        {
            return Conflict(new { error = "The invoice could not be saved because a related record changed or conflicts with an existing invoice." });
        }
    }

    [HttpPost("{id:int}/create-subscription")]
    public async Task<ActionResult<object>> CreateSubscription(int id)
    {
        var userId = GetCurrentUserId();
        if (userId <= 0)
        {
            return Unauthorized(new { error = "A valid authenticated user is required." });
        }

        try
        {
            var result = await _salesOrderService.CreateSubscriptionAsync(id, userId);
            return result is null
                ? NotFound(new { error = "Sales order not found." })
                : Ok(new { subscriptionIds = result });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { error = "The sales order changed while the subscription was being created. Reload and try again." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
