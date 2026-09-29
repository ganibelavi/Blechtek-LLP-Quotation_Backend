using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using QuotationApp.API.Data;
using QuotationApp.API.Models;
using System.Security.Claims;
using System.IO;

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

    private int GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value
            ?? User.FindFirst("id")?.Value;

        if (int.TryParse(userIdClaim, out var userId))
        {
            return userId;
        }

        // Fallback: try to get user by email from token
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

    [HttpGet("next-number")]
    public async Task<ActionResult<object>> GetNextNumber()
    {
        return Ok(new { poNo = await GeneratePoNoAsync() });
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

        var poNo = await ResolveRequestedOrGeneratedPoNoAsync(request.PoNo);
        var currentUserId = GetCurrentUserId();

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
            VerificationStatus = "pending",
            CreatedBy = currentUserId > 0 ? currentUserId : null,
            UploadedBy = request.UploadedBy,
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
                    ModulePrice = item.ModulePrice,
                    ImplementationPrice = item.ImplementationPrice,
                    DiscountPercentage = item.DiscountPercentage,
                    DiscountAmount = item.DiscountAmount,
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
            notes = purchaseOrder.VerificationNotes,
            items = request.Items,
        };

        return Ok(response);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<object>> Update(
        int id,
        [FromBody] CreatePurchaseOrderRequest request)
    {
        if (request is null)
        {
            return BadRequest(new { error = "Purchase order payload is required." });
        }

        var purchaseOrder = await _db.PurchaseOrders
            .Include(po => po.Items)
            .FirstOrDefaultAsync(po => po.Id == id);

        if (purchaseOrder is null)
        {
            return NotFound(new { error = "Purchase order not found." });
        }

        var buyerName = GetFirstNonEmpty(request.BuyerName, request.CompanyName, request.SupplierName, "Unknown Buyer");
        var supplierName = GetFirstNonEmpty(request.SupplierName, request.CompanyName, request.BuyerName, buyerName);
        var buyer = await ResolveCustomerAsync(request.CustomerId, buyerName, request.BuyerAddress, request.BuyerState, request.BuyerStateCode, request.BuyerGSTN);
        var supplier = await ResolveSupplierAsync(request.SupplierId, supplierName, request.SupplierAddress, request.SupplierState, request.SupplierStateCode, request.SupplierGSTN);

        purchaseOrder.CustomerId = buyer.Id;
        purchaseOrder.SupplierId = supplier.Id;
        purchaseOrder.QuotationId = request.QuotationId;
        purchaseOrder.QuotationRefNo = GetQuotationRefNo(request.QuotationId, request.QuotationRefNo);
        purchaseOrder.QuotationRefDate = ParseNullableDate(request.QuotationRefDate);
        purchaseOrder.PoDate = ParseDate(request.PoDate, purchaseOrder.PoDate);
        purchaseOrder.Status = string.IsNullOrWhiteSpace(request.Status) ? purchaseOrder.Status : request.Status;
        purchaseOrder.DeliveryTerms = request.DeliveryTerms;
        purchaseOrder.PaymentTerms = request.PaymentTerms;
        purchaseOrder.PoDirection = request.PoDirection;
        purchaseOrder.ReceivedFromEmail = request.ReceivedFromEmail;
        purchaseOrder.AttachmentUrl = request.AttachmentUrl;
        purchaseOrder.VerificationStatus = string.IsNullOrWhiteSpace(request.VerificationStatus)
            ? purchaseOrder.VerificationStatus
            : request.VerificationStatus;
        purchaseOrder.VerifiedBy = request.VerifiedBy;
        purchaseOrder.VerifiedAt = ParseNullableDate(request.VerifiedAt);
        purchaseOrder.VerificationNotes = string.IsNullOrWhiteSpace(request.VerificationNotes)
            ? request.Notes
            : request.VerificationNotes;
        purchaseOrder.UploadedBy = request.UploadedBy;
        purchaseOrder.ReceivedAt = ParseNullableDate(request.ReceivedAt);

        _db.PurchaseOrderItems.RemoveRange(purchaseOrder.Items);
        var lineItems = request.Items
            .Where(item => !string.IsNullOrWhiteSpace(item.Description))
            .Select(item => new PurchaseOrderItemEntity
            {
                PoId = purchaseOrder.Id,
                Description = item.Description!,
                Qty = item.Qty <= 0 ? 1 : item.Qty,
                Uom = string.IsNullOrWhiteSpace(item.Uom) ? "Nos." : item.Uom,
                Rate = item.Rate,
                ModulePrice = item.ModulePrice,
                ImplementationPrice = item.ImplementationPrice,
                DiscountPercentage = item.DiscountPercentage,
                DiscountAmount = item.DiscountAmount,
            })
            .ToList();

        _db.PurchaseOrderItems.AddRange(lineItems);
        await _db.SaveChangesAsync();

        return Ok(new
        {
            id = purchaseOrder.Id,
            poNo = purchaseOrder.PoNo,
            verificationNotes = purchaseOrder.VerificationNotes,
            notes = purchaseOrder.VerificationNotes,
            totalAmount = lineItems.Sum(item => item.Qty * item.Rate),
        });
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
            notes = record.VerificationNotes,
            totalAmount = record.Items.Sum(i => i.Qty * i.Rate),
            items = record.Items.Select(i => new
            {
                id = i.Id,
                description = i.Description,
                qty = i.Qty,
                uom = i.Uom,
                rate = i.Rate,
                modulePrice = i.ModulePrice,
                implementationPrice = i.ImplementationPrice,
                discountPercentage = i.DiscountPercentage,
                discountAmount = i.DiscountAmount,
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
            .Select(q => new { q.Id, q.QuotationNo, q.OrganizationName })
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
                organizationName = !string.IsNullOrWhiteSpace(record.QuotationId)
                    ? quotationLookup.FirstOrDefault(q => q.Id == record.QuotationId)?.OrganizationName
                    : null,
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
                notes = record.VerificationNotes,
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
                    modulePrice = i.ModulePrice,
                    implementationPrice = i.ImplementationPrice,
                    discountPercentage = i.DiscountPercentage,
                    discountAmount = i.DiscountAmount,
                }).ToList(),
            };
        }).ToList();

        return Ok(response);
    }

    [HttpGet("quotations-for-po")]
    [Authorize]
    public async Task<ActionResult<List<object>>> GetQuotationsForPo()
    {
        // Return accepted quotations that are not already linked to a PO
        var linkedQuotationIds = await _db.PurchaseOrders
            .Where(po => !string.IsNullOrWhiteSpace(po.QuotationId))
            .Select(po => po.QuotationId!)
            .ToListAsync();

        var availableQuotations = await _db.Quotations
            .AsNoTracking()
            .Where(q => !linkedQuotationIds.Contains(q.Id))
            .Select(q => new
            {
                q.Id,
                q.QuotationNo,
                q.OrganizationName,
                q.FinalPrice,
                q.Date,
                q.ValidationDate,
                q.QuotationToName,
                q.QuotationToEmail
            })
            .ToListAsync();

        return Ok(availableQuotations);
    }

    [HttpGet("{id:int}/file")]
    [Authorize]
    public async Task<IActionResult> GetFile(int id)
    {
        var po = await _db.PurchaseOrders
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id);

        if (po is null || string.IsNullOrWhiteSpace(po.UploadedFilePath))
        {
            return NotFound(new { error = "File not found." });
        }

        var filePath = po.UploadedFilePath;
        if (!System.IO.File.Exists(filePath))
        {
            return NotFound(new { error = "File not found on server." });
        }

        var contentType = string.IsNullOrWhiteSpace(po.FileContentType) ? "application/octet-stream" : po.FileContentType;
        var fileStream = System.IO.File.OpenRead(filePath);

        return File(fileStream, contentType);
    }

    [HttpPost("{id:int}/file")]
    [Authorize]
    public async Task<IActionResult> UploadFile(int id, IFormFile file)
    {
        var po = await _db.PurchaseOrders.FirstOrDefaultAsync(p => p.Id == id);
        if (po is null)
        {
            return NotFound(new { error = "Purchase order not found." });
        }

        if (file == null || file.Length == 0)
        {
            return BadRequest(new { error = "No file uploaded." });
        }

        // Create uploads directory if it doesn't exist
        var uploadsDir = Path.Combine(Directory.GetCurrentDirectory(), "uploads", "purchase-orders");
        if (!Directory.Exists(uploadsDir))
        {
            Directory.CreateDirectory(uploadsDir);
        }

        // Generate unique filename
        var extension = Path.GetExtension(file.FileName);
        var uniqueFileName = $"{Guid.NewGuid()}{extension}";
        var filePath = Path.Combine(uploadsDir, uniqueFileName);

        // Save file
        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        // Update PO with file info
        var currentUserId = GetCurrentUserId();
        po.UploadedFilePath = filePath;
        po.UploadedFileName = file.FileName;
        po.FileContentType = file.ContentType;
        po.UploadedBy = currentUserId > 0 ? currentUserId.ToString() : null;

        await _db.SaveChangesAsync();

        return Ok(new
        {
            id = po.Id,
            fileName = po.UploadedFileName,
            contentType = po.FileContentType,
            filePath = po.UploadedFilePath
        });
    }

    [HttpGet("{id:int}/verification")]
    [Authorize]
    public async Task<ActionResult<PoVerificationResponse>> GetVerification(int id)
    {
        var po = await _db.PurchaseOrders
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (po is null)
        {
            return NotFound(new { error = "Purchase order not found." });
        }

        var quotation = !string.IsNullOrWhiteSpace(po.QuotationId)
            ? await _db.Quotations.AsNoTracking().FirstOrDefaultAsync(q => q.Id == po.QuotationId)
            : null;

        var createdByUser = po.CreatedBy.HasValue
            ? await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == po.CreatedBy.Value)
            : null;

        var verifiedByUser = !string.IsNullOrEmpty(po.VerifiedBy) && int.TryParse(po.VerifiedBy, out var verifiedById)
            ? await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == verifiedById)
            : null;

        // Build quotation items from modules with quantities
        var quotationModules = quotation != null
            ? await _db.QuotationModules
                .Where(qm => qm.QuotationId == quotation.Id)
                .ToListAsync()
            : new List<QuotationModuleEntity>();

        var quotationItems = quotationModules.Any()
            ? string.Join("; ", quotationModules.Select(m =>
            {
                var qtyParts = new List<string>();
                if (m.NoOfUsers.HasValue) qtyParts.Add($"{m.NoOfUsers} Users");
                if (m.NoOfInstallations.HasValue) qtyParts.Add($"{m.NoOfInstallations} Installations");
                if (m.NoOfSites.HasValue) qtyParts.Add($"{m.NoOfSites} Sites");
                var qtyStr = qtyParts.Any() ? $" ({string.Join(", ", qtyParts)})" : "";
                return $"{m.ModuleName}{qtyStr}";
            }))
            : "";

        // Build quotation terms from quotation modules
        var quotationTerms = quotationModules.Select(m => m.ModuleName).ToList();
        var quotationTermsStr = string.Join(", ", quotationTerms);

        var response = new PoVerificationResponse
        {
            Id = po.Id,
            PoNo = po.PoNo,
            VerificationStatus = po.VerificationStatus,
            VerificationNotes = po.VerificationNotes,
            CreatedBy = po.CreatedBy,
            CreatedByName = createdByUser != null ? $"{createdByUser.FirstName} {createdByUser.LastName}".Trim() : null,
            VerifiedBy = po.VerifiedBy,
            VerifiedByName = verifiedByUser != null ? $"{verifiedByUser.FirstName} {verifiedByUser.LastName}".Trim() : null,
            VerifiedAt = po.VerifiedAt,
            UploadedFilePath = po.UploadedFilePath,
            UploadedFileName = po.UploadedFileName,
            FileContentType = po.FileContentType,
            ReceivedFromEmail = po.ReceivedFromEmail,
            QuotationRefNo = po.QuotationRefNo,
            QuotationRefDate = po.QuotationRefDate,
            QuotationAmount = quotation?.FinalPrice ?? 0,
            QuotationItems = quotationItems,
            QuotationTerms = quotationTermsStr,
            ClientPoNumber = po.ClientPoNumber,
            ClientPoDate = po.ClientPoDate,
            ClientPoAmount = po.ClientPoAmount,
            ClientPoItems = po.ClientPoItems,
            ClientPoTerms = po.ClientPoTerms,
        };

        // Compute match
        var amountMatch = po.ClientPoAmount.HasValue && quotation?.FinalPrice.HasValue == true
            ? Math.Abs(po.ClientPoAmount.Value - quotation.FinalPrice.Value) < 0.01m
            : false;

        var itemsMatch = !string.IsNullOrWhiteSpace(po.ClientPoItems) && !string.IsNullOrWhiteSpace(quotationItems)
            ? NormalizeText(po.ClientPoItems) == NormalizeText(quotationItems)
            : false;

        var termsMatch = !string.IsNullOrWhiteSpace(po.ClientPoTerms) && !string.IsNullOrWhiteSpace(quotationTermsStr)
            ? NormalizeText(po.ClientPoTerms) == NormalizeText(quotationTermsStr)
            : false;

        response.AmountMatches = amountMatch;
        response.ItemsMatch = itemsMatch;
        response.TermsMatch = termsMatch;
        response.MismatchCount = (amountMatch ? 0 : 1) + (itemsMatch ? 0 : 1) + (termsMatch ? 0 : 1);

        return Ok(response);
    }

    [HttpPut("{id:int}/client-details")]
    [Authorize]
    public async Task<ActionResult<object>> UpdateClientDetails(int id, [FromBody] PoClientDetailsRequest request)
    {
        if (request is null)
        {
            return BadRequest(new { error = "Client PO details are required." });
        }

        var po = await _db.PurchaseOrders.FirstOrDefaultAsync(p => p.Id == id);
        if (po is null)
        {
            return NotFound(new { error = "Purchase order not found." });
        }

        // Only allow editing in Draft or PendingReview status
        if (po.VerificationStatus is "Approved" or "ApprovedWithMismatch" or "Rejected")
        {
            return BadRequest(new { error = "Cannot modify client details after approval or rejection." });
        }

        var currentUserId = GetCurrentUserId();

        // Track changes for audit log
        var changes = new List<string>();
        if (po.ClientPoNumber != request.ClientPoNumber) changes.Add($"ClientPoNumber: '{po.ClientPoNumber}' -> '{request.ClientPoNumber}'");
        if (po.ClientPoDate != ParseNullableDate(request.ClientPoDate)) changes.Add($"ClientPoDate: '{po.ClientPoDate}' -> '{request.ClientPoDate}'");
        if (po.ClientPoAmount != request.ClientPoAmount) changes.Add($"ClientPoAmount: '{po.ClientPoAmount}' -> '{request.ClientPoAmount}'");
        if (po.ClientPoItems != request.ClientPoItems) changes.Add("ClientPoItems changed");
        if (po.ClientPoTerms != request.ClientPoTerms) changes.Add("ClientPoTerms changed");

        po.ClientPoNumber = request.ClientPoNumber;
        po.ClientPoDate = ParseNullableDate(request.ClientPoDate);
        po.ClientPoAmount = request.ClientPoAmount;
        po.ClientPoItems = request.ClientPoItems;
        po.ClientPoTerms = request.ClientPoTerms;

        await _db.SaveChangesAsync();

        // Write audit log if there were changes
        if (changes.Any() && currentUserId > 0)
        {
            _db.PoAuditLogs.Add(new PoAuditLogEntity
            {
                PoId = po.Id,
                Action = "ClientDetailsUpdated",
                ChangedBy = currentUserId,
                ChangedAt = DateTime.UtcNow,
                Notes = string.Join("; ", changes)
            });
            await _db.SaveChangesAsync();
        }

        // If PO was submitted and now edited, move back to PendingReview
        if (po.VerificationStatus == "PendingReview" || po.VerificationStatus == "Approved" || po.VerificationStatus == "ApprovedWithMismatch" || po.VerificationStatus == "Rejected")
        {
            // Only move back from PendingReview if it was already submitted
            // Don't move back from Approved/Rejected states
        }
        else if (po.VerificationStatus == "pending")
        {
            // Stay in Draft
        }

        return Ok(new
        {
            id = po.Id,
            clientPoNumber = po.ClientPoNumber,
            clientPoDate = po.ClientPoDate,
            clientPoAmount = po.ClientPoAmount,
            clientPoItems = po.ClientPoItems,
            clientPoTerms = po.ClientPoTerms,
        });
    }

    [HttpPost("{id:int}/reopen")]
    [Authorize]
    public async Task<ActionResult<object>> Reopen(int id, [FromBody] PoReopenRequest request)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == 0)
        {
            return Unauthorized(new { error = "User not authenticated." });
        }

        var po = await _db.PurchaseOrders.FirstOrDefaultAsync(p => p.Id == id);
        if (po is null)
        {
            return NotFound(new { error = "Purchase order not found." });
        }

        // Rule: Only allow reopen for Approved, ApprovedWithMismatch, Rejected
        var allowedReopenStatuses = new[] { "Approved", "ApprovedWithMismatch", "Rejected" };
        if (!allowedReopenStatuses.Contains(po.VerificationStatus))
        {
            return BadRequest(new { error = "Only purchase orders with status Approved, ApprovedWithMismatch, or Rejected can be reopened." });
        }

        // Check if PO has been used to generate an invoice
        var hasInvoice = await _db.Invoices.AnyAsync(i => i.PoId == po.Id);
        if (hasInvoice)
        {
            return BadRequest(new { error = "Cannot reopen a purchase order that has already been invoiced." });
        }

        // Reason required
        if (string.IsNullOrWhiteSpace(request?.Reason))
        {
            return BadRequest(new { error = "Reason is required to reopen a purchase order." });
        }

        var previousStatus = po.VerificationStatus;
        po.VerificationStatus = "pending";
        po.VerifiedBy = null;
        po.VerifiedAt = null;
        po.VerificationNotes = request.Reason.Trim();

        await _db.SaveChangesAsync();

        // Audit log
        _db.PoAuditLogs.Add(new PoAuditLogEntity
        {
            PoId = po.Id,
            Action = "Reopened",
            ChangedBy = currentUserId,
            ChangedAt = DateTime.UtcNow,
            Notes = $"Reopened from {previousStatus}. Reason: {request.Reason.Trim()}"
        });
        await _db.SaveChangesAsync();

        return Ok(new
        {
            id = po.Id,
            verificationStatus = po.VerificationStatus,
            message = "Purchase order reopened successfully."
        });
    }

    [HttpPost("{id:int}/approve")]
    [Authorize]
    public async Task<ActionResult<object>> Approve(int id, [FromBody] PoApproveRequest request)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == 0)
        {
            return Unauthorized(new { error = "User not authenticated." });
        }

        var po = await _db.PurchaseOrders
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (po is null)
        {
            return NotFound(new { error = "Purchase order not found." });
        }

        // Rule: Status must be pending or PendingReview (treat PendingReview same as Draft)
        if (po.VerificationStatus != "pending" && po.VerificationStatus != "PendingReview")
        {
            return BadRequest(new { error = "Only purchase orders in pending or PendingReview status can be approved." });
        }

        // Validate required fields
        var missingFields = new List<string>();
        if (string.IsNullOrWhiteSpace(po.UploadedFilePath)) missingFields.Add("uploaded file");
        if (string.IsNullOrWhiteSpace(po.ReceivedFromEmail)) missingFields.Add("reference email");
        if (string.IsNullOrWhiteSpace(po.ClientPoNumber)) missingFields.Add("client PO number");
        if (!po.ClientPoDate.HasValue) missingFields.Add("client PO date");
        if (!po.ClientPoAmount.HasValue) missingFields.Add("client PO amount");
        if (string.IsNullOrWhiteSpace(po.ClientPoItems)) missingFields.Add("client PO items");
        if (string.IsNullOrWhiteSpace(po.ClientPoTerms)) missingFields.Add("client PO terms");

        if (missingFields.Count > 0)
        {
            return BadRequest(new { error = $"Missing required fields: {string.Join(", ", missingFields)}" });
        }

        // Reload quotation and client values from database (don't trust frontend)
        var quotation = !string.IsNullOrWhiteSpace(po.QuotationId)
            ? await _db.Quotations.AsNoTracking().FirstOrDefaultAsync(q => q.Id == po.QuotationId)
            : null;

        // Build quotation items from modules with quantities (same as verification endpoint)
        var quotationModules = quotation != null
            ? await _db.QuotationModules
                .Where(qm => qm.QuotationId == quotation.Id)
                .ToListAsync()
            : new List<QuotationModuleEntity>();

        var quotationItems = quotationModules.Any()
            ? string.Join("; ", quotationModules.Select(m =>
            {
                var qtyParts = new List<string>();
                if (m.NoOfUsers.HasValue) qtyParts.Add($"{m.NoOfUsers} Users");
                if (m.NoOfInstallations.HasValue) qtyParts.Add($"{m.NoOfInstallations} Installations");
                if (m.NoOfSites.HasValue) qtyParts.Add($"{m.NoOfSites} Sites");
                var qtyStr = qtyParts.Any() ? $" ({string.Join(", ", qtyParts)})" : "";
                return $"{m.ModuleName}{qtyStr}";
            }))
            : "";

        var quotationTermsList = quotationModules.Select(m => m.ModuleName).ToList();
        var quotationTermsStr = string.Join(", ", quotationTermsList);

        // Recompute match on server
        var amountMatch = po.ClientPoAmount.HasValue && quotation?.FinalPrice.HasValue == true
            ? Math.Abs(po.ClientPoAmount.Value - quotation.FinalPrice.Value) < 0.01m
            : false;

        var itemsMatch = !string.IsNullOrWhiteSpace(po.ClientPoItems) && !string.IsNullOrWhiteSpace(quotationItems)
            ? NormalizeText(po.ClientPoItems) == NormalizeText(quotationItems)
            : false;

        var termsMatch = !string.IsNullOrWhiteSpace(po.ClientPoTerms) && !string.IsNullOrWhiteSpace(quotationTermsStr)
            ? NormalizeText(po.ClientPoTerms) == NormalizeText(quotationTermsStr)
            : false;

        var allMatch = amountMatch && itemsMatch && termsMatch;

        if (allMatch)
        {
            po.VerificationStatus = "Approved";
        }
        else
        {
            // Notes are mandatory when there's a mismatch
            if (string.IsNullOrWhiteSpace(request?.Notes))
            {
                return BadRequest(new { error = "Notes are required when there is a mismatch between client PO and quotation." });
            }
            po.VerificationStatus = "ApprovedWithMismatch";
        }

        po.VerifiedBy = currentUserId.ToString();
        po.VerifiedAt = DateTime.UtcNow;
        po.VerificationNotes = request?.Notes?.Trim();

        await _db.SaveChangesAsync();

        // Build mismatch details for audit log
        var mismatchDetails = new List<string>();
        if (!amountMatch) mismatchDetails.Add("Amount");
        if (!itemsMatch) mismatchDetails.Add("Items/Qty");
        if (!termsMatch) mismatchDetails.Add("Payment Terms");

        // Audit log
        _db.PoAuditLogs.Add(new PoAuditLogEntity
        {
            PoId = po.Id,
            Action = allMatch ? "Approved" : "ApprovedWithMismatch",
            ChangedBy = currentUserId,
            ChangedAt = DateTime.UtcNow,
            Notes = request?.Notes?.Trim() ?? (allMatch ? "All fields match" : $"Approved with mismatch in: {string.Join(", ", mismatchDetails)}")
        });
        await _db.SaveChangesAsync();

        return Ok(new
        {
            id = po.Id,
            verificationStatus = po.VerificationStatus,
            verifiedBy = po.VerifiedBy,
            verifiedAt = po.VerifiedAt,
            verificationNotes = po.VerificationNotes,
            amountMatch,
            itemsMatch,
            termsMatch,
        });
    }

    [HttpPost("{id:int}/reject")]
    [Authorize]
    public async Task<ActionResult<object>> Reject(int id, [FromBody] PoRejectRequest request)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == 0)
        {
            return Unauthorized(new { error = "User not authenticated." });
        }

        var po = await _db.PurchaseOrders.FirstOrDefaultAsync(p => p.Id == id);
        if (po is null)
        {
            return NotFound(new { error = "Purchase order not found." });
        }

        // Rule: Status must be pending or PendingReview (treat PendingReview same as Draft)
        if (po.VerificationStatus != "pending" && po.VerificationStatus != "PendingReview")
        {
            return BadRequest(new { error = "Only purchase orders in pending or PendingReview status can be rejected." });
        }

        // Notes required for rejection
        if (string.IsNullOrWhiteSpace(request?.Notes))
        {
            return BadRequest(new { error = "Notes are required when rejecting a purchase order." });
        }

        po.VerificationStatus = "Rejected";
        po.VerifiedBy = currentUserId.ToString();
        po.VerifiedAt = DateTime.UtcNow;
        po.VerificationNotes = request.Notes.Trim();

        await _db.SaveChangesAsync();

        // Audit log
        _db.PoAuditLogs.Add(new PoAuditLogEntity
        {
            PoId = po.Id,
            Action = "Rejected",
            ChangedBy = currentUserId,
            ChangedAt = DateTime.UtcNow,
            Notes = request.Notes.Trim()
        });
        await _db.SaveChangesAsync();

        return Ok(new
        {
            id = po.Id,
            verificationStatus = po.VerificationStatus,
            verifiedBy = po.VerifiedBy,
            verifiedAt = po.VerifiedAt,
            verificationNotes = po.VerificationNotes,
        });
    }

    [HttpGet("{id:int}/audit-log")]
    [Authorize]
    public async Task<ActionResult<List<PoAuditLogResponse>>> GetAuditLog(int id)
    {
        var po = await _db.PurchaseOrders.FirstOrDefaultAsync(p => p.Id == id);
        if (po is null)
        {
            return NotFound(new { error = "Purchase order not found." });
        }

        var logs = await _db.PoAuditLogs
            .Where(l => l.PoId == id)
            .OrderByDescending(l => l.ChangedAt)
            .ToListAsync();

        var userIds = logs.Select(l => l.ChangedBy).Distinct().ToList();
        var users = await _db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim());

        var response = logs.Select(l => new PoAuditLogResponse
        {
            Id = l.Id,
            PoId = l.PoId,
            Action = l.Action,
            ChangedBy = l.ChangedBy,
            ChangedByName = users.TryGetValue(l.ChangedBy, out var name) ? name : null,
            ChangedAt = l.ChangedAt,
            Notes = l.Notes
        }).ToList();

        return Ok(response);
    }

    private static string NormalizeText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        return string.Join(" ", text.Trim().ToLowerInvariant().Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries));
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

    private async Task<string> GeneratePoNoAsync()
    {
        var now = DateTime.UtcNow.AddHours(5.5);
        var financialYear = $"FY{now.Year}-{(now.Year + 1) % 100:00}";
        var prefix = $"BTSS/{financialYear}/PO-";
        var existingNumbers = await _db.PurchaseOrders
            .AsNoTracking()
            .Where(po => po.PoNo != null && po.PoNo.StartsWith(prefix))
            .Select(po => po.PoNo!)
            .ToListAsync();
        var nextNumber = existingNumbers
            .Select(number => int.TryParse(number[prefix.Length..], out var value) ? value : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;

        var candidate = $"{prefix}{nextNumber:0000}";
        while (await _db.PurchaseOrders.AnyAsync(po => po.PoNo == candidate))
        {
            nextNumber++;
            candidate = $"{prefix}{nextNumber:0000}";
        }
        return candidate;
    }

    private async Task<string> ResolveRequestedOrGeneratedPoNoAsync(string? requestedPoNo)
    {
        var trimmed = requestedPoNo?.Trim();
        if (!string.IsNullOrWhiteSpace(trimmed) &&
            trimmed.StartsWith("BTSS/FY", StringComparison.OrdinalIgnoreCase) &&
            !await _db.PurchaseOrders.AnyAsync(po => po.PoNo == trimmed))
        {
            return trimmed;
        }

        return await GeneratePoNoAsync();
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
