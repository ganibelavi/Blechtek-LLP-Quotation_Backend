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

        var buyerName = GetFirstNonEmpty(request.BuyerName, request.CompanyName, request.SupplierName, "Unknown Buyer");
        var supplierName = GetFirstNonEmpty(request.SupplierName, request.CompanyName, request.BuyerName, buyerName);

        if (string.IsNullOrWhiteSpace(buyerName) && string.IsNullOrWhiteSpace(supplierName))
        {
            return BadRequest(new { error = "Buyer or supplier name is required." });
        }

        var buyer = await ResolveCustomerAsync(request.CustomerId, buyerName, request.BuyerAddress, request.BuyerState, request.BuyerStateCode, request.BuyerGSTN);
        var supplier = await ResolveSupplierAsync(request.SupplierId, supplierName, request.SupplierAddress, request.SupplierState, request.SupplierStateCode, request.SupplierGSTN);

        var poNo = await ResolveUniquePoNoAsync(request.PoNo);

        var purchaseOrder = new PurchaseOrderEntity
        {
            CustomerId = buyer.Id,
            SupplierId = supplier.Id,
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

        var totalAmount = request.Items.Sum(item => item.Qty * item.Rate);

        var response = new
        {
            id = purchaseOrder.Id,
            quotationId = purchaseOrder.QuotationId,
            poNo = purchaseOrder.PoNo,
            poDate = purchaseOrder.PoDate,
            status = purchaseOrder.Status,
            companyName = buyer.Name,
            buyerName = buyer.Name,
            buyerAddress = buyer.Address,
            buyerState = buyer.State,
            buyerStateCode = buyer.StateCode,
            buyerGSTN = buyer.Gstn,
            supplierName = supplier.Name,
            supplierAddress = supplier.Address,
            supplierState = supplier.State,
            supplierStateCode = supplier.StateCode,
            supplierGSTN = supplier.Gstn,
            deliveryTerms = purchaseOrder.DeliveryTerms,
            paymentTerms = purchaseOrder.PaymentTerms,
            quotationRefNo = request.QuotationRefNo,
            quotationRefDate = request.QuotationRefDate,
            totalAmount = totalAmount,
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

        var buyer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == record.CustomerId);
        var supplier = record.SupplierId.HasValue
            ? await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == record.SupplierId.Value)
            : null;

        var response = new
        {
            id = record.Id,
            quotationId = record.QuotationId,
            poNo = record.PoNo,
            poDate = record.PoDate,
            status = record.Status,
            companyName = buyer?.Name ?? supplier?.Name,
            buyerName = buyer?.Name,
            buyerAddress = buyer?.Address,
            buyerState = buyer?.State,
            buyerStateCode = buyer?.StateCode,
            buyerGSTN = buyer?.Gstn,
            supplierName = supplier?.Name ?? buyer?.Name,
            supplierAddress = supplier?.Address ?? buyer?.Address,
            supplierState = supplier?.State ?? buyer?.State,
            supplierStateCode = supplier?.StateCode ?? buyer?.StateCode,
            supplierGSTN = supplier?.Gstn ?? buyer?.Gstn,
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
                buyerAddress = _db.Customers
                    .Where(c => c.Id == p.CustomerId)
                    .Select(c => c.Address)
                    .FirstOrDefault(),
                buyerState = _db.Customers
                    .Where(c => c.Id == p.CustomerId)
                    .Select(c => c.State)
                    .FirstOrDefault(),
                buyerStateCode = _db.Customers
                    .Where(c => c.Id == p.CustomerId)
                    .Select(c => c.StateCode)
                    .FirstOrDefault(),
                buyerGSTN = _db.Customers
                    .Where(c => c.Id == p.CustomerId)
                    .Select(c => c.Gstn)
                    .FirstOrDefault(),
                supplierName = p.SupplierId.HasValue
                    ? _db.Suppliers
                        .Where(s => s.Id == p.SupplierId.Value)
                        .Select(s => s.Name)
                        .FirstOrDefault()
                    : _db.Customers
                        .Where(c => c.Id == p.CustomerId)
                        .Select(c => c.Name)
                        .FirstOrDefault(),
                supplierAddress = p.SupplierId.HasValue
                    ? _db.Suppliers
                        .Where(s => s.Id == p.SupplierId.Value)
                        .Select(s => s.Address)
                        .FirstOrDefault()
                    : _db.Customers
                        .Where(c => c.Id == p.CustomerId)
                        .Select(c => c.Address)
                        .FirstOrDefault(),
                supplierState = p.SupplierId.HasValue
                    ? _db.Suppliers
                        .Where(s => s.Id == p.SupplierId.Value)
                        .Select(s => s.State)
                        .FirstOrDefault()
                    : _db.Customers
                        .Where(c => c.Id == p.CustomerId)
                        .Select(c => c.State)
                        .FirstOrDefault(),
                supplierStateCode = p.SupplierId.HasValue
                    ? _db.Suppliers
                        .Where(s => s.Id == p.SupplierId.Value)
                        .Select(s => s.StateCode)
                        .FirstOrDefault()
                    : _db.Customers
                        .Where(c => c.Id == p.CustomerId)
                        .Select(c => c.StateCode)
                        .FirstOrDefault(),
                supplierGSTN = p.SupplierId.HasValue
                    ? _db.Suppliers
                        .Where(s => s.Id == p.SupplierId.Value)
                        .Select(s => s.Gstn)
                        .FirstOrDefault()
                    : _db.Customers
                        .Where(c => c.Id == p.CustomerId)
                        .Select(c => c.Gstn)
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

    private async Task<CustomerEntity> ResolveCustomerAsync(int? requestedCustomerId, string buyerName, string? buyerAddress, string? buyerState, string? buyerStateCode, string? buyerGstn)
    {
        if (requestedCustomerId.HasValue && requestedCustomerId.Value > 0)
        {
            var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == requestedCustomerId.Value);
            if (customer is not null)
            {
                return customer;
            }
        }

        var trimmedBuyerName = buyerName.Trim();
        var customerRecord = await _db.Customers.FirstOrDefaultAsync(c => c.Name == trimmedBuyerName);
        if (customerRecord is not null)
        {
            return customerRecord;
        }

        var createdCustomer = new CustomerEntity
        {
            Name = trimmedBuyerName,
            Address = buyerAddress,
            State = buyerState,
            StateCode = buyerStateCode,
            Gstn = buyerGstn,
            CreatedAt = DateTime.UtcNow,
        };

        _db.Customers.Add(createdCustomer);
        await _db.SaveChangesAsync();
        return createdCustomer;
    }

    private async Task<SupplierEntity> ResolveSupplierAsync(int? requestedSupplierId, string supplierName, string? supplierAddress, string? supplierState, string? supplierStateCode, string? supplierGstn)
    {
        if (requestedSupplierId.HasValue && requestedSupplierId.Value > 0)
        {
            var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == requestedSupplierId.Value);
            if (supplier is not null)
            {
                return supplier;
            }
        }

        var trimmedSupplierName = supplierName.Trim();
        var supplierRecord = await _db.Suppliers.FirstOrDefaultAsync(s => s.Name == trimmedSupplierName);
        if (supplierRecord is not null)
        {
            return supplierRecord;
        }

        var createdSupplier = new SupplierEntity
        {
            Name = trimmedSupplierName,
            Address = supplierAddress,
            State = supplierState,
            StateCode = supplierStateCode,
            Gstn = supplierGstn,
            CreatedAt = DateTime.UtcNow,
        };

        _db.Suppliers.Add(createdSupplier);
        await _db.SaveChangesAsync();
        return createdSupplier;
    }

    private static string GetFirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return "Unknown";
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
