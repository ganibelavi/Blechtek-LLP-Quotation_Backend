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
            QuotationRefNo = GetQuotationRefNo(request.QuotationId, request.QuotationRefNo),
            QuotationRefDate = ParseNullableDate(request.QuotationRefDate),
            PoNo = poNo,
            PoDate = ParseDate(request.PoDate, DateTime.UtcNow),
            Status = string.IsNullOrWhiteSpace(request.Status) ? "open" : request.Status,
            DeliveryTerms = request.DeliveryTerms,
            PaymentTerms = request.PaymentTerms,
            CreatedAt = DateTime.UtcNow,
            PoDirection = request.PoDirection,
            ReceivedFromEmail = request.ReceivedFromEmail,
            AttachmentUrl = request.AttachmentUrl,
            VerificationStatus = string.IsNullOrWhiteSpace(request.VerificationStatus) ? "pending" : request.VerificationStatus,
            VerifiedBy = string.IsNullOrWhiteSpace(request.VerifiedBy) ? null : request.VerifiedBy.Trim(),
            VerifiedAt = ParseNullableDate(request.VerifiedAt),
            VerificationNotes = request.VerificationNotes,
            UploadedBy = string.IsNullOrWhiteSpace(request.UploadedBy) ? null : request.UploadedBy.Trim(),
            ReceivedAt = ParseNullableDate(request.ReceivedAt),
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
            customerId = purchaseOrder.CustomerId,
            supplierId = purchaseOrder.SupplierId,
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
            quotationRefNo = purchaseOrder.QuotationRefNo,
            quotationRefDate = purchaseOrder.QuotationRefDate,
            poDirection = purchaseOrder.PoDirection,
            receivedFromEmail = purchaseOrder.ReceivedFromEmail,
            attachmentUrl = purchaseOrder.AttachmentUrl,
            verificationStatus = purchaseOrder.VerificationStatus,
            verifiedBy = purchaseOrder.VerifiedBy,
            verifiedAt = purchaseOrder.VerifiedAt,
            verificationNotes = purchaseOrder.VerificationNotes,
            uploadedBy = purchaseOrder.UploadedBy,
            receivedAt = purchaseOrder.ReceivedAt,
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
        var linkedQuotationNo = await GetLinkedQuotationNoAsync(record.QuotationId);
        var response = new
        {
            id = record.Id,
            customerId = record.CustomerId,
            supplierId = record.SupplierId,
            quotationId = record.QuotationId,
            quotationRefNo = record.QuotationRefNo ?? linkedQuotationNo,
            quotationRefDate = record.QuotationRefDate,
            poDirection = record.PoDirection,
            receivedFromEmail = record.ReceivedFromEmail,
            attachmentUrl = record.AttachmentUrl,
            verificationStatus = record.VerificationStatus,
            verifiedBy = record.VerifiedBy,
            verifiedAt = record.VerifiedAt,
            verificationNotes = record.VerificationNotes,
            uploadedBy = record.UploadedBy,
            receivedAt = record.ReceivedAt,
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
            .ToListAsync();

        var customerIds = records.Select(p => p.CustomerId).Distinct().ToList();
        var supplierIds = records.Where(p => p.SupplierId.HasValue).Select(p => p.SupplierId!.Value).Distinct().ToList();

        var customerLookup = await _db.Customers
            .Where(c => customerIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c);

        var supplierLookup = await _db.Suppliers
            .Where(s => supplierIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s);

        var quotationLookup = await _db.Quotations
            .AsNoTracking()
            .Select(q => new { q.Id, q.QuotationNo })
            .ToListAsync();

        var response = records.Select(record =>
        {
            var buyer = customerLookup.TryGetValue(record.CustomerId, out var customer) ? customer : null;
            var supplier = record.SupplierId.HasValue && supplierLookup.TryGetValue(record.SupplierId.Value, out var foundSupplier)
                ? foundSupplier
                : null;

            var linkedQuotationNo = !string.IsNullOrWhiteSpace(record.QuotationId)
                ? quotationLookup.FirstOrDefault(q => q.Id == record.QuotationId)?.QuotationNo
                : null;

            var totalAmount = record.Items.Sum(i => i.Qty * i.Rate);

            return new
            {
                id = record.Id,
                customerId = record.CustomerId,
                supplierId = record.SupplierId,
                quotationId = record.QuotationId,
                quotationRefNo = record.QuotationRefNo ?? linkedQuotationNo,
                quotationRefDate = record.QuotationRefDate,
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
                poDirection = record.PoDirection,
                receivedFromEmail = record.ReceivedFromEmail,
                attachmentUrl = record.AttachmentUrl,
                verificationStatus = record.VerificationStatus,
                verifiedBy = record.VerifiedBy,
                verifiedAt = record.VerifiedAt,
                verificationNotes = record.VerificationNotes,
                uploadedBy = record.UploadedBy,
                receivedAt = record.ReceivedAt,
                totalAmount = totalAmount,
                items = record.Items.Select(i => new
                {
                    id = i.Id,
                    description = i.Description,
                    qty = i.Qty,
                    uom = i.Uom,
                    rate = i.Rate,
                }).ToList(),
            };
        }).ToList();

        return Ok(response);
    }

    [HttpPatch("{id:int}/verification")]
    public async Task<ActionResult<object>> UpdateVerification(
        int id,
        [FromBody] UpdatePurchaseOrderVerificationRequest request)
    {
        var allowedStatuses = new[] { "pending", "verified", "mismatch", "rejected" };
        var status = request?.VerificationStatus?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(status) || !allowedStatuses.Contains(status))
        {
            return BadRequest(new { error = "Verification status must be pending, verified, mismatch, or rejected." });
        }

        var purchaseOrder = await _db.PurchaseOrders.FirstOrDefaultAsync(po => po.Id == id);
        if (purchaseOrder is null)
        {
            return NotFound(new { error = "Purchase order not found." });
        }

        purchaseOrder.VerificationStatus = status;
        purchaseOrder.VerificationNotes = string.IsNullOrWhiteSpace(request?.VerificationNotes)
            ? null
            : request.VerificationNotes.Trim();
        purchaseOrder.VerifiedAt = status == "verified" ? DateTime.UtcNow : null;
        purchaseOrder.VerifiedBy = null;

        await _db.SaveChangesAsync();

        return Ok(new
        {
            id = purchaseOrder.Id,
            verificationStatus = purchaseOrder.VerificationStatus,
            verificationNotes = purchaseOrder.VerificationNotes,
            verifiedAt = purchaseOrder.VerifiedAt,
        });
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

    private async Task<string?> GetLinkedQuotationNoAsync(string? quotationId)
    {
        if (string.IsNullOrWhiteSpace(quotationId))
        {
            return null;
        }

        return await _db.Quotations
            .AsNoTracking()
            .Where(q => q.Id == quotationId)
            .Select(q => q.QuotationNo)
            .FirstOrDefaultAsync();
    }

    private static string? GetQuotationRefNo(string? quotationId, string? requestQuotationRefNo)
    {
        if (!string.IsNullOrWhiteSpace(requestQuotationRefNo))
        {
            return requestQuotationRefNo.Trim();
        }

        if (string.IsNullOrWhiteSpace(quotationId))
        {
            return null;
        }

        return null;
    }

    private static DateTime? ParseNullableDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTime.TryParse(value, out var parsed)
            ? parsed
            : null;
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
