using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuotationApp.API.Data;
using QuotationApp.API.Models;

namespace QuotationApp.API.Controllers;

[ApiController]
[Route("api/purchase-order")]
public class PurchaseOrderController : ControllerBase
{
    private readonly QuotationDbContext _db;

    public PurchaseOrderController(QuotationDbContext db)
    {
        _db = db;
    }

    [HttpPost]
    public async Task<ActionResult<object>> Create([FromBody] CreatePurchaseOrderRequest request)
    {
        if (request is null)
        {
            return BadRequest(new { error = "Purchase order payload is required." });
        }

        var customerName = (request.CompanyName ?? request.SupplierName ?? request.BuyerName ?? "Unknown Customer").Trim();
        if (string.IsNullOrWhiteSpace(customerName))
        {
            return BadRequest(new { error = "Company name is required." });
        }

        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Name == customerName);
        if (customer is null)
        {
            customer = new CustomerEntity
            {
                Name = customerName,
                Address = request.SupplierAddress ?? request.BuyerAddress,
                State = request.SupplierState ?? request.BuyerState,
                StateCode = request.SupplierStateCode ?? request.BuyerStateCode,
                Gstn = request.SupplierGSTN ?? request.BuyerGSTN,
            };
            _db.Customers.Add(customer);
            await _db.SaveChangesAsync();
        }

        var poNo = await ResolveUniquePoNoAsync(request.PoNo);

        var purchaseOrder = new PurchaseOrderEntity
        {
            CustomerId = customer.Id,
            QuotationId = request.QuotationId,
            PoNo = poNo,
            PoDate = ParseDate(request.PoDate, DateTime.UtcNow),
            Status = string.IsNullOrWhiteSpace(request.Status) ? "open" : request.Status,
            DeliveryTerms = request.DeliveryTerms,
            PaymentTerms = request.PaymentTerms,
            CreatedAt = DateTime.UtcNow,
        };

        _db.PurchaseOrders.Add(purchaseOrder);
        await _db.SaveChangesAsync();

        if (request.Items is { Count: > 0 })
        {
            var lineItems = request.Items
                .Where(item => !string.IsNullOrWhiteSpace(item.Description))
                .Select(item => new PurchaseOrderItemEntity
                {
                    PoId = purchaseOrder.Id,
                    Description = item.Description ?? "",
                    Qty = item.Qty <= 0 ? 1 : item.Qty,
                    Uom = string.IsNullOrWhiteSpace(item.Uom) ? "Nos." : item.Uom,
                    Rate = item.Rate,
                })
                .ToList();

            if (lineItems.Count > 0)
            {
                _db.PurchaseOrderItems.AddRange(lineItems);
                await _db.SaveChangesAsync();
            }
        }

        var response = new
        {
            id = purchaseOrder.Id,
            quotationId = purchaseOrder.QuotationId,
            poNo = purchaseOrder.PoNo,
            poDate = purchaseOrder.PoDate,
            status = purchaseOrder.Status,
            companyName = customerName,
            buyerName = customerName,
            supplierName = request.SupplierName ?? customerName,
            deliveryTerms = purchaseOrder.DeliveryTerms,
            paymentTerms = purchaseOrder.PaymentTerms,
            quotationRefNo = request.QuotationRefNo,
            quotationRefDate = request.QuotationRefDate,
            totalAmount = request.Items.Sum(item => item.Qty * item.Rate),
            items = request.Items,
        };

        return Ok(response);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<object>> GetById(int id)
    {
        var record = await _db.PurchaseOrders
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (record is null)
        {
            return NotFound(new { error = "Purchase order not found." });
        }

        var customerName = await _db.Customers
            .Where(c => c.Id == record.CustomerId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync();

        var response = new
        {
            id = record.Id,
            quotationId = record.QuotationId,
            poNo = record.PoNo,
            poDate = record.PoDate,
            status = record.Status,
            companyName = customerName,
            buyerName = customerName,
            supplierName = customerName,
            deliveryTerms = record.DeliveryTerms,
            paymentTerms = record.PaymentTerms,
            totalAmount = record.Items.Sum(i => i.Qty * i.Rate),
            items = record.Items.Select(i => new
            {
                id = i.Id,
                description = i.Description,
                qty = i.Qty,
                uom = i.Uom,
                rate = i.Rate,
            }).ToList(),
        };

        return Ok(response);
    }

    [HttpGet]
    public async Task<ActionResult<List<object>>> GetAll()
    {
        var records = await _db.PurchaseOrders
            .Include(p => p.Items)
            .OrderByDescending(p => p.Id)
            .Select(p => new
            {
                id = p.Id,
                quotationId = p.QuotationId,
                poNo = p.PoNo,
                poDate = p.PoDate,
                status = p.Status,
                companyName = _db.Customers
                    .Where(c => c.Id == p.CustomerId)
                    .Select(c => c.Name)
                    .FirstOrDefault(),
                buyerName = _db.Customers
                    .Where(c => c.Id == p.CustomerId)
                    .Select(c => c.Name)
                    .FirstOrDefault(),
                supplierName = _db.Customers
                    .Where(c => c.Id == p.CustomerId)
                    .Select(c => c.Name)
                    .FirstOrDefault(),
                deliveryTerms = p.DeliveryTerms,
                paymentTerms = p.PaymentTerms,
                totalAmount = p.Items.Sum(i => i.Qty * i.Rate),
                items = p.Items.Select(i => new
                {
                    id = i.Id,
                    description = i.Description,
                    qty = i.Qty,
                    uom = i.Uom,
                    rate = i.Rate,
                }).ToList(),
            })
            .ToListAsync();

        return Ok(records);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var record = await _db.PurchaseOrders.Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == id);
        if (record is null)
        {
            return NotFound(new { error = "Purchase order not found." });
        }

        _db.PurchaseOrderItems.RemoveRange(record.Items);
        _db.PurchaseOrders.Remove(record);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private async Task<string> ResolveUniquePoNoAsync(string? requestedPoNo)
    {
        var trimmed = requestedPoNo?.Trim();
        if (!string.IsNullOrWhiteSpace(trimmed) && !await _db.PurchaseOrders.AnyAsync(p => p.PoNo == trimmed))
        {
            return trimmed;
        }

        var basePoNo = string.IsNullOrWhiteSpace(trimmed)
            ? $"PO-{DateTime.UtcNow:yyyyMMdd}"
            : trimmed;

        var candidate = basePoNo;
        var index = 1;
        while (await _db.PurchaseOrders.AnyAsync(p => p.PoNo == candidate))
        {
            candidate = $"{basePoNo}-{index}";
            index++;
        }

        return candidate;
    }

    private static DateTime ParseDate(string? value, DateTime fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return DateTime.TryParse(value, out var parsed)
            ? parsed
            : fallback;
    }
}
