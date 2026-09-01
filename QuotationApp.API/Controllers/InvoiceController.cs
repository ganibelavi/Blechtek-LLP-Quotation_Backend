using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuotationApp.API.Data;
using QuotationApp.API.Models;

namespace QuotationApp.API.Controllers;

[ApiController]
[Route("api/invoice")]
public class InvoiceController : ControllerBase
{
    private readonly QuotationDbContext _db;

    public InvoiceController(QuotationDbContext db)
    {
        _db = db;
    }

    [HttpPost]
    public async Task<ActionResult<object>> Create([FromBody] CreateInvoiceRequest request)
    {
        if (request is null)
        {
            return BadRequest(new { error = "Invoice payload is required." });
        }

        var customerName = (request.CompanyName ?? request.SupplierName ?? request.ReceiverName ?? request.ConsigneeName ?? "Unknown Customer").Trim();
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
                Address = request.SupplierAddress ?? request.ReceiverAddress ?? request.ConsigneeAddress,
                State = request.SupplierState ?? request.ReceiverState ?? request.ConsigneeState,
                StateCode = request.SupplierStateCode ?? request.ReceiverStateCode ?? request.ConsigneeStateCode,
                Gstn = request.SupplierGSTN ?? request.ReceiverGSTN ?? request.ConsigneeGSTN,
            };
            _db.Customers.Add(customer);
            await _db.SaveChangesAsync();
        }

        var invoiceNo = await ResolveUniqueInvoiceNoAsync(request.InvoiceNo);

        var invoice = new InvoiceEntity
        {
            CustomerId = customer.Id,
            PoId = request.PoId,
            InvoiceNo = invoiceNo,
            InvoiceDate = ParseDate(request.DateOfIssue, DateTime.UtcNow),
            PlaceOfSupply = request.PlaceOfService,
            HsnCode = request.HsnCode,
            SacCode = request.SacCode,
            SgstPct = request.SgstPct,
            CgstPct = request.CgstPct,
            IgstPct = request.IgstPct,
            TdsPct = request.TdsPct,
            Insurance = request.Insurance,
            ReverseCharge = !string.IsNullOrWhiteSpace(request.ReverseCharge) && request.ReverseCharge.Equals("Yes", StringComparison.OrdinalIgnoreCase),
            Subtotal = request.TotalAmount,
            GrandTotal = request.TotalAmount,
            Status = "unpaid",
            AmountInWords = request.AmountInWords,
            CreatedAt = DateTime.UtcNow,
        };

        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync();

        if (request.Items is { Count: > 0 })
        {
            var lineItems = request.Items
                .Where(item => !string.IsNullOrWhiteSpace(item.Description))
                .Select(item => new InvoiceItemEntity
                {
                    InvoiceId = invoice.Id,
                    Description = item.Description ?? "",
                    Qty = item.Qty <= 0 ? 1 : item.Qty,
                    Uom = string.IsNullOrWhiteSpace(item.Uom) ? "Nos." : item.Uom,
                    Rate = item.Rate,
                })
                .ToList();

            if (lineItems.Count > 0)
            {
                _db.InvoiceItems.AddRange(lineItems);
                await _db.SaveChangesAsync();
            }
        }

        var response = new
        {
            id = invoice.Id,
            poId = invoice.PoId,
            invoiceNo = invoice.InvoiceNo,
            dateOfIssue = invoice.InvoiceDate,
            companyName = customerName,
            receiverName = request.ReceiverName ?? customerName,
            consigneeName = request.ConsigneeName ?? customerName,
            poNoDate = request.PoNoDate,
            totalAmount = invoice.GrandTotal,
            items = request.Items,
            invoice = new
            {
                originalFor = request.OriginalFor,
                companyName = customerName,
                invoiceNo = invoice.InvoiceNo,
                dateOfIssue = invoice.InvoiceDate,
                timeOfIssue = request.TimeOfIssue,
                placeOfService = request.PlaceOfService,
                supplierName = request.SupplierName,
                supplierAddress = request.SupplierAddress,
                supplierState = request.SupplierState,
                supplierStateCode = request.SupplierStateCode,
                supplierGSTN = request.SupplierGSTN,
                receiverName = request.ReceiverName,
                receiverAddress = request.ReceiverAddress,
                receiverState = request.ReceiverState,
                receiverStateCode = request.ReceiverStateCode,
                receiverGSTN = request.ReceiverGSTN,
                consigneeName = request.ConsigneeName,
                consigneeAddress = request.ConsigneeAddress,
                consigneeState = request.ConsigneeState,
                consigneeStateCode = request.ConsigneeStateCode,
                consigneeGSTN = request.ConsigneeGSTN,
                poNoDate = request.PoNoDate,
                hsnCode = request.HsnCode,
                sacCode = request.SacCode,
                reverseCharge = request.ReverseCharge,
                amountInWords = request.AmountInWords,
                termsOfSale = request.TermsOfSale,
                sgstPct = request.SgstPct,
                cgstPct = request.CgstPct,
                igstPct = request.IgstPct,
                tdsPct = request.TdsPct,
                insurance = request.Insurance,
            },
            totals = new
            {
                totalPrice = request.TotalAmount,
                grandTotal = request.TotalAmount,
                subtotal = request.TotalAmount,
            },
        };

        return Ok(response);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<object>> GetById(int id)
    {
        var record = await _db.Invoices
            .Include(i => i.Items)
            .FirstOrDefaultAsync(i => i.Id == id);

        if (record is null)
        {
            return NotFound(new { error = "Invoice not found." });
        }

        var customerName = await _db.Customers
            .Where(c => c.Id == record.CustomerId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync();

        var response = new
        {
            id = record.Id,
            poId = record.PoId,
            invoiceNo = record.InvoiceNo,
            companyName = customerName,
            receiverName = customerName,
            dateOfIssue = record.InvoiceDate,
            poNoDate = record.InvoiceNo,
            totalAmount = record.Items.Sum(item => item.Qty * item.Rate),
            items = record.Items.Select(item => new
            {
                id = item.Id,
                description = item.Description,
                qty = item.Qty,
                uom = item.Uom,
                rate = item.Rate,
            }).ToList(),
        };

        return Ok(response);
    }

    [HttpGet]
    public async Task<ActionResult<List<object>>> GetAll()
    {
        var records = await _db.Invoices
            .Include(i => i.Items)
            .OrderByDescending(i => i.Id)
            .Select(i => new
            {
                id = i.Id,
                invoiceNo = i.InvoiceNo,
                companyName = _db.Customers
                    .Where(c => c.Id == i.CustomerId)
                    .Select(c => c.Name)
                    .FirstOrDefault(),
                receiverName = _db.Customers
                    .Where(c => c.Id == i.CustomerId)
                    .Select(c => c.Name)
                    .FirstOrDefault(),
                dateOfIssue = i.InvoiceDate,
                poNoDate = i.InvoiceNo,
                totalAmount = i.Items.Sum(item => item.Qty * item.Rate),
                items = i.Items.Select(item => new
                {
                    id = item.Id,
                    description = item.Description,
                    qty = item.Qty,
                    uom = item.Uom,
                    rate = item.Rate,
                }).ToList(),
            })
            .ToListAsync();

        return Ok(records);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var record = await _db.Invoices.Include(i => i.Items).FirstOrDefaultAsync(i => i.Id == id);
        if (record is null)
        {
            return NotFound(new { error = "Invoice not found." });
        }

        _db.InvoiceItems.RemoveRange(record.Items);
        _db.Invoices.Remove(record);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private async Task<string> ResolveUniqueInvoiceNoAsync(string? requestedInvoiceNo)
    {
        var trimmed = requestedInvoiceNo?.Trim();
        if (!string.IsNullOrWhiteSpace(trimmed) && !await _db.Invoices.AnyAsync(i => i.InvoiceNo == trimmed))
        {
            return trimmed;
        }

        var baseInvoiceNo = string.IsNullOrWhiteSpace(trimmed)
            ? $"INV-{DateTime.UtcNow:yyyyMMdd}"
            : trimmed;

        var candidate = baseInvoiceNo;
        var index = 1;
        while (await _db.Invoices.AnyAsync(i => i.InvoiceNo == candidate))
        {
            candidate = $"{baseInvoiceNo}-{index}";
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
