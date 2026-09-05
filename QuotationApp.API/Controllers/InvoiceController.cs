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

    [HttpGet("next-number")]
    public async Task<ActionResult<object>> GetNextNumber()
    {
        return Ok(new { invoiceNo = await GenerateInvoiceNoAsync() });
    }

    [HttpPost]
    public async Task<ActionResult<object>> Create([FromBody] CreateInvoiceRequest request)
    {
        if (request is null)
        {
            return BadRequest(new { error = "Invoice payload is required." });
        }

        var customerName = (request.BuyerName ?? request.CompanyName ?? request.SupplierName ?? request.ReceiverName ?? request.ConsigneeName ?? "Unknown Customer").Trim();
        if (string.IsNullOrWhiteSpace(customerName))
        {
            return BadRequest(new { error = "Company name is required." });
        }

        if (request.PoId.HasValue)
        {
            var purchaseOrder = await _db.PurchaseOrders
                .AsNoTracking()
                .FirstOrDefaultAsync(po => po.Id == request.PoId.Value);
            if (purchaseOrder is null)
            {
                return BadRequest(new { error = "The referenced purchase order does not exist." });
            }

            if (!string.Equals(purchaseOrder.VerificationStatus, "verified", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { error = "Only verified purchase orders can be invoiced." });
            }
        }

        var customer = request.CustomerId.HasValue
            ? await _db.Customers.FirstOrDefaultAsync(c => c.Id == request.CustomerId.Value)
            : await _db.Customers.FirstOrDefaultAsync(c => c.Name == customerName);
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

        var invoiceNo = await ResolveRequestedOrGeneratedInvoiceNoAsync(request.InvoiceNo);

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
            CompanyProfileId = request.CompanyProfileId,
            SellerName = request.SellerName ?? request.SupplierName,
            SellerAddress = request.SellerAddress ?? request.SupplierAddress,
            SellerState = request.SellerState ?? request.SupplierState,
            SellerStateCode = request.SellerStateCode ?? request.SupplierStateCode,
            SellerGstn = request.SellerGSTN ?? request.SupplierGSTN,
            BuyerName = request.BuyerName ?? request.ReceiverName ?? request.ConsigneeName,
            BuyerAddress = request.BuyerAddress ?? request.ReceiverAddress ?? request.ConsigneeAddress,
            BuyerState = request.BuyerState ?? request.ReceiverState ?? request.ConsigneeState,
            BuyerStateCode = request.BuyerStateCode ?? request.ReceiverStateCode ?? request.ConsigneeStateCode,
            BuyerGstn = request.BuyerGSTN ?? request.ReceiverGSTN ?? request.ConsigneeGSTN,
            ShipToAddress = request.ShipToAddress,
            GstRateId = request.GstRateId,
        };

        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync();

        var bankDetails = new InvoiceBankDetailEntity
        {
            InvoiceId = invoice.Id,
            BankName = request.BankName,
            AccountNo = request.AccountNo,
            AccountType = request.AccountType,
            Ifsc = request.Ifsc,
            MsmeNo = request.MsmeNo,
            CreatedAt = DateTime.UtcNow,
        };

        _db.InvoiceBankDetails.Add(bankDetails);
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
                bankName = bankDetails.BankName,
                accountNo = bankDetails.AccountNo,
                accountType = bankDetails.AccountType ?? "Current",
                ifsc = bankDetails.Ifsc,
                msmeNo = bankDetails.MsmeNo,
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

        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == record.CustomerId);
        var bankDetails = await _db.InvoiceBankDetails.FirstOrDefaultAsync(b => b.InvoiceId == record.Id);
        var totalAmount = record.Items.Sum(item => item.Qty * item.Rate);

        return Ok(BuildInvoiceResponse(record, customer, totalAmount, bankDetails));
    }

    [HttpGet]
    public async Task<ActionResult<List<object>>> GetAll()
    {
        var records = await _db.Invoices
            .Include(i => i.Items)
            .OrderByDescending(i => i.Id)
            .ToListAsync();

        if (records.Count == 0)
        {
            return Ok(new List<object>());
        }

        var customerIds = records.Select(r => r.CustomerId).Distinct().ToList();
        var customers = await _db.Customers
            .Where(c => customerIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c);
        var bankDetailsByInvoiceId = await _db.InvoiceBankDetails
            .Where(b => records.Select(r => r.Id).Contains(b.InvoiceId))
            .ToDictionaryAsync(b => b.InvoiceId, b => b);

        var response = records
            .Select(record =>
            {
                var customer = customers.TryGetValue(record.CustomerId, out var matchedCustomer) ? matchedCustomer : null;
                var totalAmount = record.Items.Sum(item => item.Qty * item.Rate);
                var bankDetails = bankDetailsByInvoiceId.TryGetValue(record.Id, out var matchedBank) ? matchedBank : null;
                return BuildInvoiceResponse(record, customer, totalAmount, bankDetails);
            })
            .ToList();

        return Ok(response);
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

    private static object BuildInvoiceResponse(InvoiceEntity record, CustomerEntity? customer, decimal totalAmount, InvoiceBankDetailEntity? bankDetails)
    {
        var customerName = record.BuyerName ?? customer?.Name ?? "";
        var customerAddress = record.BuyerAddress ?? customer?.Address ?? "";
        var customerState = record.BuyerState ?? customer?.State ?? "";
        var customerStateCode = record.BuyerStateCode ?? customer?.StateCode ?? "";
        var customerGstn = record.BuyerGstn ?? customer?.Gstn ?? "";
        var items = record.Items.Select(item => new
        {
            id = item.Id,
            description = item.Description,
            qty = item.Qty,
            uom = item.Uom,
            rate = item.Rate,
        }).ToList();

        var bankName = bankDetails?.BankName ?? "";
        var accountNo = bankDetails?.AccountNo ?? "";
        var accountType = string.IsNullOrWhiteSpace(bankDetails?.AccountType) ? "Current" : bankDetails.AccountType;
        var ifsc = bankDetails?.Ifsc ?? "";
        var msmeNo = bankDetails?.MsmeNo ?? "";

        return new
        {
            id = record.Id,
            poId = record.PoId,
            invoiceNo = record.InvoiceNo,
            companyName = customerName,
            receiverName = customerName,
            consigneeName = customerName,
            dateOfIssue = record.InvoiceDate,
            poNoDate = record.InvoiceNo,
            totalAmount = totalAmount,
            items = items,
            invoice = new
            {
                originalFor = "ORIGINAL FOR RECIPIENT",
                companyName = customerName,
                invoiceNo = record.InvoiceNo,
                dateOfIssue = record.InvoiceDate,
                timeOfIssue = "",
                placeOfService = record.PlaceOfSupply,
                supplierName = record.SellerName ?? customerName,
                supplierAddress = record.SellerAddress ?? customerAddress,
                supplierState = record.SellerState ?? customerState,
                supplierStateCode = record.SellerStateCode ?? customerStateCode,
                supplierGSTN = record.SellerGstn ?? customerGstn,
                bankName = bankName,
                accountNo = accountNo,
                accountType = accountType,
                ifsc = ifsc,
                msmeNo = msmeNo,
                receiverName = customerName,
                receiverAddress = customerAddress,
                receiverState = customerState,
                receiverStateCode = customerStateCode,
                receiverGSTN = customerGstn,
                consigneeName = customerName,
                consigneeAddress = customerAddress,
                consigneeState = customerState,
                consigneeStateCode = customerStateCode,
                consigneeGSTN = customerGstn,
                poNoDate = record.InvoiceNo,
                hsnCode = record.HsnCode,
                sacCode = record.SacCode,
                reverseCharge = record.ReverseCharge ? "Yes" : "No",
                amountInWords = record.AmountInWords,
                termsOfSale = "",
                sgstPct = record.SgstPct,
                cgstPct = record.CgstPct,
                igstPct = record.IgstPct,
                tdsPct = record.TdsPct,
                insurance = record.Insurance,
                companyProfileId = record.CompanyProfileId,
                sellerName = record.SellerName,
                sellerAddress = record.SellerAddress,
                sellerState = record.SellerState,
                sellerStateCode = record.SellerStateCode,
                sellerGSTN = record.SellerGstn,
                buyerName = record.BuyerName,
                buyerAddress = record.BuyerAddress,
                buyerState = record.BuyerState,
                buyerStateCode = record.BuyerStateCode,
                buyerGSTN = record.BuyerGstn,
                shipToAddress = record.ShipToAddress,
                gstRateId = record.GstRateId,
            },
            totals = new
            {
                totalQty = items.Sum(item => (decimal)item.qty),
                totalPrice = totalAmount,
                subtotal = totalAmount,
                grandTotal = totalAmount,
                sgst = 0m,
                cgst = 0m,
                igst = 0m,
                tds = 0m,
                insurance = record.Insurance,
            },
        };
    }

    private async Task<string> GenerateInvoiceNoAsync()
    {
        var now = DateTime.UtcNow.AddHours(5.5);
        var financialYear = $"FY{now.Year}-{(now.Year + 1) % 100:00}";
        var prefix = $"BTSS/{financialYear}/INV-";
        var existingNumbers = await _db.Invoices
            .AsNoTracking()
            .Where(invoice => invoice.InvoiceNo != null && invoice.InvoiceNo.StartsWith(prefix))
            .Select(invoice => invoice.InvoiceNo!)
            .ToListAsync();
        var nextNumber = existingNumbers
            .Select(number => int.TryParse(number[prefix.Length..], out var value) ? value : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;

        var candidate = $"{prefix}{nextNumber:0000}";
        while (await _db.Invoices.AnyAsync(invoice => invoice.InvoiceNo == candidate))
        {
            nextNumber++;
            candidate = $"{prefix}{nextNumber:0000}";
        }
        return candidate;
    }

    private async Task<string> ResolveRequestedOrGeneratedInvoiceNoAsync(string? requestedInvoiceNo)
    {
        var trimmed = requestedInvoiceNo?.Trim();
        if (!string.IsNullOrWhiteSpace(trimmed) &&
            trimmed.StartsWith("BTSS/FY", StringComparison.OrdinalIgnoreCase) &&
            !await _db.Invoices.AnyAsync(invoice => invoice.InvoiceNo == trimmed))
        {
            return trimmed;
        }

        return await GenerateInvoiceNoAsync();
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
