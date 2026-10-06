using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using QuotationApp.API.Data;
using QuotationApp.API.Models;
using System.Data;
using System.Globalization;

namespace QuotationApp.API.Services;

public class SalesOrderService : ISalesOrderService
{
    private readonly QuotationDbContext _db;

    public SalesOrderService(QuotationDbContext db)
    {
        _db = db;
    }

    public async Task<PagedSalesOrderResultDto> GetPagedAsync(
        int page,
        int pageSize,
        string? status,
        int? customerId,
        DateTime? fromDate,
        DateTime? toDate,
        string? search)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = _db.SalesOrders
            .AsNoTracking()
            .Include(s => s.Customer)
            .Include(s => s.PurchaseOrder)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(s => s.Status == status);

        if (customerId.HasValue)
            query = query.Where(s => s.CustomerId == customerId.Value);

        if (fromDate.HasValue)
            query = query.Where(s => s.SoDate >= fromDate.Value.Date);

        if (toDate.HasValue)
            query = query.Where(s => s.SoDate <= toDate.Value.Date);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(s =>
                s.SoNumber.Contains(term) ||
                s.CustomerPoNumber.Contains(term) ||
                (s.Customer != null && s.Customer.Name.Contains(term)) ||
                (s.PurchaseOrder != null && s.PurchaseOrder.PoNo.Contains(term)));
        }

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(s => s.SoDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new SalesOrderListItemDto
            {
                Id = s.Id,
                SoNumber = s.SoNumber,
                SoDate = s.SoDate,
                CustomerName = s.Customer != null ? s.Customer.Name : string.Empty,
                PurchaseOrderNo = s.PurchaseOrder != null ? s.PurchaseOrder.PoNo : string.Empty,
                GrandTotal = s.GrandTotal,
                InvoicedAmount = s.InvoicedAmount,
                Status = s.Status
            })
            .ToListAsync();

        return new PagedSalesOrderResultDto
        {
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            Items = items
        };
    }

    public async Task<SalesOrderDetailDto?> GetByIdAsync(int id)
    {
        var salesOrder = await _db.SalesOrders
            .AsNoTracking()
            .Include(s => s.Customer)
            .Include(s => s.PurchaseOrder)
            .Include(s => s.Items)
            .ThenInclude(i => i.Module)
            .Include(s => s.Items)
            .ThenInclude(i => i.GstRate)
            .Include(s => s.BillingSchedule)
            .Include(s => s.Documents)
            .Include(s => s.StatusHistory)
            .ThenInclude(h => h.ChangedByUser)
            .Include(s => s.Invoices)
            .Include(s => s.Subscriptions)
            .FirstOrDefaultAsync(s => s.Id == id && !s.IsDeleted);

        if (salesOrder == null)
        {
            return null;
        }

        return new SalesOrderDetailDto
        {
            Id = salesOrder.Id,
            SoNumber = salesOrder.SoNumber,
            SoDate = salesOrder.SoDate,
            QuotationId = salesOrder.QuotationId,
            PurchaseOrderId = salesOrder.PurchaseOrderId,
            PurchaseOrderNo = salesOrder.PurchaseOrder != null ? salesOrder.PurchaseOrder.PoNo : string.Empty,
            CustomerId = salesOrder.CustomerId,
            CustomerName = salesOrder.Customer != null ? salesOrder.Customer.Name : string.Empty,
            CustomerPoNumber = salesOrder.CustomerPoNumber,
            CustomerPoDate = salesOrder.CustomerPoDate,
            BillingAddress = salesOrder.BillingAddress,
            ShippingAddress = salesOrder.ShippingAddress,
            CustomerGstin = salesOrder.CustomerGstin,
            PlaceOfSupplyState = salesOrder.PlaceOfSupplyState,
            IsInterState = salesOrder.IsInterState,
            CurrencyCode = salesOrder.CurrencyCode,
            SubTotal = salesOrder.SubTotal,
            DiscountTotal = salesOrder.DiscountTotal,
            TaxableAmount = salesOrder.TaxableAmount,
            CgstAmount = salesOrder.CgstAmount,
            SgstAmount = salesOrder.SgstAmount,
            IgstAmount = salesOrder.IgstAmount,
            RoundOff = salesOrder.RoundOff,
            GrandTotal = salesOrder.GrandTotal,
            InvoicedAmount = salesOrder.InvoicedAmount,
            PaymentTermsDays = salesOrder.PaymentTermsDays,
            PaymentTermsText = salesOrder.PaymentTermsText,
            BillingType = salesOrder.BillingType,
            BankAccountId = salesOrder.BankAccountId,
            BankNameSnapshot = salesOrder.BankNameSnapshot,
            BankAccountNoSnapshot = salesOrder.BankAccountNoSnapshot,
            BankAccountTypeSnapshot = salesOrder.BankAccountTypeSnapshot,
            BankIfscSnapshot = salesOrder.BankIfscSnapshot,
            BankMsmeNoSnapshot = salesOrder.BankMsmeNoSnapshot,
            TermsTemplateId = salesOrder.TermsTemplateId,
            TermsAndConditions = salesOrder.TermsAndConditions,
            IsSubscription = salesOrder.IsSubscription,
            SubscriptionStart = salesOrder.SubscriptionStart,
            SubscriptionEnd = salesOrder.SubscriptionEnd,
            BillingCycle = salesOrder.BillingCycle,
            RenewalTermMonths = salesOrder.RenewalTermMonths,
            AutoRenew = salesOrder.AutoRenew,
            RenewalReminderDays = salesOrder.RenewalReminderDays,
            HasMismatch = salesOrder.HasMismatch,
            MismatchRemarks = salesOrder.MismatchRemarks,
            VerifiedBy = salesOrder.VerifiedBy,
            VerifiedOn = salesOrder.VerifiedOn,
            VerificationRemarks = salesOrder.VerificationRemarks,
            Status = salesOrder.Status,
            CancelReason = salesOrder.CancelReason,
            InternalRemarks = salesOrder.InternalRemarks,
            CreatedBy = salesOrder.CreatedBy,
            CreatedOn = salesOrder.CreatedOn,
            UpdatedBy = salesOrder.UpdatedBy,
            UpdatedOn = salesOrder.UpdatedOn,
            RowVer = salesOrder.RowVer ?? Array.Empty<byte>(),
            Items = salesOrder.Items.OrderBy(i => i.LineNo).Select(i => new SalesOrderItemDto
            {
                Id = i.Id,
                LineNo = i.LineNo,
                ModuleId = i.ModuleId,
                ModuleName = i.Module != null ? i.Module.ModuleName : string.Empty,
                ItemDescription = i.ItemDescription,
                HsnSacCode = i.HsnSacCode,
                Quantity = i.Quantity,
                Uom = i.Uom,
                QuotedUnitPrice = i.QuotedUnitPrice,
                PoUnitPrice = i.PoUnitPrice,
                UnitPrice = i.UnitPrice,
                DiscountPercent = i.DiscountPercent,
                DiscountAmount = i.DiscountAmount,
                TaxableAmount = i.TaxableAmount,
                GstRateId = i.GstRateId,
                GstPercent = i.GstPercent,
                CgstAmount = i.CgstAmount,
                SgstAmount = i.SgstAmount,
                IgstAmount = i.IgstAmount,
                LineTotal = i.LineTotal,
                QtyInvoiced = i.QtyInvoiced
            }).ToList(),
            BillingSchedule = salesOrder.BillingSchedule.OrderBy(b => b.SequenceNo).Select(b => new SalesOrderBillingScheduleDto
            {
                Id = b.Id,
                SequenceNo = b.SequenceNo,
                MilestoneName = b.MilestoneName,
                Percentage = b.Percentage,
                Amount = b.Amount,
                DueDate = b.DueDate,
                InvoiceId = b.InvoiceId,
                Status = b.Status
            }).ToList(),
            Documents = salesOrder.Documents.Select(d => new SalesOrderDocumentDto
            {
                Id = d.Id,
                DocumentType = d.DocumentType,
                FileName = d.FileName,
                FilePath = d.FilePath,
                ContentType = d.ContentType,
                FileSizeBytes = d.FileSizeBytes,
                UploadedBy = d.UploadedBy,
                UploadedOn = d.UploadedOn
            }).ToList(),
            StatusHistory = salesOrder.StatusHistory.OrderByDescending(h => h.ChangedOn).Select(h => new SalesOrderStatusHistoryDto
            {
                Id = h.Id,
                FromStatus = h.FromStatus,
                ToStatus = h.ToStatus,
                Remarks = h.Remarks,
                ChangedBy = h.ChangedBy,
                ChangedByName = h.ChangedByUser != null ? $"{h.ChangedByUser.FirstName} {h.ChangedByUser.LastName}".Trim() : string.Empty,
                ChangedOn = h.ChangedOn
            }).ToList(),
            InvoiceIds = salesOrder.Invoices.Select(i => i.Id).ToList(),
            SubscriptionIds = salesOrder.Subscriptions.Select(s => s.Id).ToList()
        };
    }

    public async Task<SalesOrderPreviewDto> GetPreviewAsync(int poId)
    {
        var purchaseOrder = await _db.PurchaseOrders
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.Id == poId);

        if (purchaseOrder == null)
        {
            throw new KeyNotFoundException($"Purchase order {poId} was not found.");
        }

        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == purchaseOrder.CustomerId);
        var companyProfile = await _db.CompanyProfiles.FirstOrDefaultAsync(c => c.IsActive);
        var customerState = customer?.State ?? companyProfile?.State ?? string.Empty;
        var isInterState = IsInterState(customer?.State, companyProfile?.State);
        var gstRate = await _db.GstRates
            .Where(rate => rate.IsActive)
            .OrderByDescending(rate => rate.IgstPct)
            .FirstOrDefaultAsync();
        var gstPercent = gstRate is null
            ? 18m
            : isInterState
                ? gstRate.IgstPct
                : gstRate.CgstPct + gstRate.SgstPct;
        var quotationModules = string.IsNullOrWhiteSpace(purchaseOrder.QuotationId)
            ? new List<QuotationModuleEntity>()
            : await _db.QuotationModules
                .Where(qm => qm.QuotationId == purchaseOrder.QuotationId)
                .ToListAsync();
        var items = purchaseOrder.Items
            .Select(item => new SalesOrderItemDto
            {
                ItemDescription = item.Description,
                Quantity = item.Qty,
                Uom = item.Uom,
                UnitPrice = item.Rate,
                DiscountPercent = item.DiscountPercentage,
                DiscountAmount = item.DiscountAmount,
                GstRateId = gstRate?.Id,
                GstPercent = gstPercent,
                LineTotal = item.Qty * item.Rate,
                ModuleId = null,
                LineNo = 0,
                Id = 0,
                QtyInvoiced = 0m
            }).ToList();
        var comparisonRows = purchaseOrder.Items.Select(item =>
        {
            var quotationModule = quotationModules.FirstOrDefault(qm =>
                string.Equals(qm.ModuleName.Trim(), item.Description.Trim(), StringComparison.OrdinalIgnoreCase));
            var quotedPrice = GetQuotationUnitPrice(quotationModule);
            var quotedQuantity = quotationModule?.NoOfUsers;

            return new PoComparisonRowDto
            {
                ItemName = item.Description,
                QuotationValue = quotationModule is null
                    ? "Not present"
                    : $"{FormatAmount(quotedPrice ?? 0m)} × {quotedQuantity?.ToString(CultureInfo.InvariantCulture) ?? "1"}",
                PoValue = $"{FormatAmount(item.Rate)} × {FormatAmount(item.Qty)}",
                SalesOrderValue = $"{FormatAmount(item.Rate)} × {FormatAmount(item.Qty)}",
                HasDifference = quotationModule is null
                    || quotedPrice.HasValue && quotedPrice.Value != item.Rate
                    || quotedQuantity.HasValue && quotedQuantity.Value != item.Qty
            };
        }).ToList();
        comparisonRows.AddRange(quotationModules
            .Where(quotationModule => !purchaseOrder.Items.Any(item => string.Equals(
                item.Description.Trim(),
                quotationModule.ModuleName.Trim(),
                StringComparison.OrdinalIgnoreCase)))
            .Select(quotationModule => new PoComparisonRowDto
            {
                ItemName = quotationModule.ModuleName,
                QuotationValue = $"{FormatAmount(GetQuotationUnitPrice(quotationModule) ?? 0m)} × {quotationModule.NoOfUsers?.ToString(CultureInfo.InvariantCulture) ?? "1"}",
                PoValue = "Not present",
                SalesOrderValue = "Not present",
                HasDifference = true
            }));

        var subtotal = 0m;
        var discountTotal = 0m;
        var taxableAmount = 0m;
        var cgst = 0m;
        var sgst = 0m;
        var igst = 0m;
        foreach (var item in items)
        {
            var gross = item.Quantity * item.UnitPrice;
            var discount = item.DiscountPercent > 0
                ? Math.Round(gross * item.DiscountPercent / 100m, 2, MidpointRounding.AwayFromZero)
                : Math.Min(item.DiscountAmount, gross);
            var lineTaxable = Math.Max(gross - discount, 0m);
            var lineTax = Math.Round(lineTaxable * item.GstPercent / 100m, 2, MidpointRounding.AwayFromZero);
            var lineCgst = isInterState ? 0m : Math.Round(lineTax / 2m, 2, MidpointRounding.AwayFromZero);
            var lineSgst = isInterState ? 0m : lineTax - lineCgst;
            var lineIgst = isInterState ? lineTax : 0m;

            item.DiscountAmount = discount;
            item.TaxableAmount = lineTaxable;
            item.CgstAmount = lineCgst;
            item.SgstAmount = lineSgst;
            item.IgstAmount = lineIgst;
            item.LineTotal = lineTaxable + lineTax;
            subtotal += gross;
            discountTotal += discount;
            taxableAmount += lineTaxable;
            cgst += lineCgst;
            sgst += lineSgst;
            igst += lineIgst;
        }
        var totalBeforeRounding = taxableAmount + cgst + sgst + igst;
        var grandTotal = Math.Round(totalBeforeRounding, 0, MidpointRounding.AwayFromZero);

        return new SalesOrderPreviewDto
        {
            PurchaseOrderId = purchaseOrder.Id,
            PurchaseOrderNo = purchaseOrder.PoNo,
            CustomerName = customer?.Name ?? string.Empty,
            CustomerPoNumber = purchaseOrder.ClientPoNumber ?? purchaseOrder.PoNo,
            BillingAddress = customer?.Address,
            ShippingAddress = customer?.Address,
            CustomerGstin = customer?.Gstn,
            PlaceOfSupplyState = customerState,
            IsInterState = isInterState,
            SubTotal = subtotal,
            DiscountTotal = discountTotal,
            TaxableAmount = taxableAmount,
            CgstAmount = cgst,
            SgstAmount = sgst,
            IgstAmount = igst,
            RoundOff = grandTotal - totalBeforeRounding,
            GrandTotal = grandTotal,
            HasMismatch = comparisonRows.Any(row => row.HasDifference),
            MismatchRemarks = null,
            PaymentTermsText = purchaseOrder.PaymentTerms,
            TermsAndConditions = companyProfile?.DefaultTermsOfSale,
            Items = items,
            ComparisonRows = comparisonRows
        };
    }

    public async Task<SalesOrderDetailDto> CreateFromPurchaseOrderAsync(int poId, int createdByUserId)
    {
        if (createdByUserId <= 0)
        {
            throw new InvalidOperationException("A valid user is required to create a sales order.");
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var purchaseOrder = await _db.PurchaseOrders
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.Id == poId);

        if (purchaseOrder == null)
        {
            throw new KeyNotFoundException($"Purchase order {poId} was not found.");
        }

        if (!string.Equals(purchaseOrder.VerificationStatus, "Approved", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(purchaseOrder.VerificationStatus, "ApprovedWithMismatch", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A sales order can only be created from a verified purchase order.");
        }
        if (purchaseOrder.Items.Count == 0
            || purchaseOrder.Items.Any(item =>
                item.Qty <= 0
                || item.Rate < 0
                || item.DiscountPercentage < 0
                || item.DiscountPercentage > 100
                || item.DiscountAmount < 0
                || item.DiscountAmount > item.Qty * item.Rate))
        {
            throw new InvalidOperationException("The verified purchase order must contain valid line items before a sales order can be created.");
        }

        var activeSalesOrderExists = await _db.SalesOrders
            .AnyAsync(s => s.PurchaseOrderId == poId && !s.IsDeleted && s.Status != "Cancelled");

        if (activeSalesOrderExists)
        {
            throw new InvalidOperationException("An active sales order already exists for this purchase order.");
        }

        var companyProfile = await _db.CompanyProfiles.FirstOrDefaultAsync(c => c.IsActive);
        var defaultBank = await _db.CompanyBankAccounts.FirstOrDefaultAsync(b => b.IsActive && b.IsDefault)
            ?? await _db.CompanyBankAccounts.FirstOrDefaultAsync(b => b.IsActive);
        var defaultTerms = await _db.TermsTemplates.FirstOrDefaultAsync(t => t.IsActive && t.IsDefault)
            ?? await _db.TermsTemplates.FirstOrDefaultAsync(t => t.IsActive);

        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == purchaseOrder.CustomerId);
        var soDate = DateTime.UtcNow.AddHours(5.5).Date;
        var financialYear = GetFinancialYear(soDate);
        var soNumber = await GetNextSalesOrderNumberAsync(financialYear);
        var defaultGstRate = await _db.GstRates
            .Where(rate => rate.IsActive)
            .OrderByDescending(rate => rate.IgstPct)
            .FirstOrDefaultAsync();
        var defaultGstPercent = defaultGstRate is null
            ? 18m
            : customer?.State != null
                && companyProfile?.State != null
                && !string.Equals(customer.State, companyProfile.State, StringComparison.OrdinalIgnoreCase)
                    ? defaultGstRate.IgstPct
                    : defaultGstRate.CgstPct + defaultGstRate.SgstPct;
        var quotationModules = string.IsNullOrWhiteSpace(purchaseOrder.QuotationId)
            ? new List<QuotationModuleEntity>()
            : await _db.QuotationModules
                .Where(qm => qm.QuotationId == purchaseOrder.QuotationId)
                .ToListAsync();
        var salesOrder = new SalesOrderEntity
        {
            SoNumber = soNumber,
            SoDate = soDate,
            QuotationId = purchaseOrder.QuotationId,
            PurchaseOrderId = purchaseOrder.Id,
            CustomerId = purchaseOrder.CustomerId,
            CustomerPoNumber = purchaseOrder.ClientPoNumber ?? purchaseOrder.PoNo,
            CustomerPoDate = purchaseOrder.ClientPoDate ?? purchaseOrder.PoDate,
            BillingAddress = customer?.Address,
            ShippingAddress = customer?.Address,
            CustomerGstin = customer?.Gstn,
            PlaceOfSupplyState = customer?.State ?? companyProfile?.State,
            IsInterState = IsInterState(customer?.State, companyProfile?.State),
            CurrencyCode = "INR",
            PaymentTermsDays = null,
            PaymentTermsText = purchaseOrder.PaymentTerms,
            BillingType = "OneTime",
            BankAccountId = defaultBank?.Id,
            BankNameSnapshot = defaultBank?.BankName,
            BankAccountNoSnapshot = defaultBank?.AccountNo,
            BankAccountTypeSnapshot = defaultBank?.AccountType,
            BankIfscSnapshot = defaultBank?.Ifsc,
            BankMsmeNoSnapshot = defaultBank?.MsmeNo,
            TermsTemplateId = defaultTerms?.Id,
            TermsAndConditions = defaultTerms?.Content ?? companyProfile?.DefaultTermsOfSale,
            IsSubscription = false,
            RenewalReminderDays = 30,
            Status = "Draft",
            CreatedBy = createdByUserId,
            CreatedOn = DateTime.UtcNow,
            UpdatedBy = createdByUserId,
            UpdatedOn = DateTime.UtcNow
        };

        if (purchaseOrder.Items.Any())
        {
            var modules = await _db.Modules.AsNoTracking().ToListAsync();
            var sequence = 1;
            var itemList = purchaseOrder.Items.Select(item =>
            {
                var quotationModule = quotationModules.FirstOrDefault(qm =>
                    string.Equals(qm.ModuleName.Trim(), item.Description.Trim(), StringComparison.OrdinalIgnoreCase));
                var module = modules.FirstOrDefault(m =>
                    string.Equals(m.ModuleName.Trim(), item.Description.Trim(), StringComparison.OrdinalIgnoreCase));
                var quotedPrice = GetQuotationUnitPrice(quotationModule);

                return new SalesOrderItemEntity
                {
                    LineNo = sequence++,
                    ModuleId = module?.Id,
                    ItemDescription = item.Description,
                    HsnSacCode = module?.HsnCode ?? module?.SacCode,
                    Quantity = item.Qty,
                    Uom = item.Uom,
                    QuotedUnitPrice = quotedPrice,
                    PoUnitPrice = item.Rate,
                    UnitPrice = item.Rate,
                    DiscountPercent = item.DiscountPercentage,
                    DiscountAmount = item.DiscountAmount,
                    GstRateId = defaultGstRate?.Id,
                    GstPercent = defaultGstPercent,
                    QtyInvoiced = 0m,
                    SalesOrder = salesOrder
                };
            }).ToList();

            salesOrder.Items = itemList;
        }

        var preview = await GetPreviewAsync(poId);
        salesOrder.HasMismatch = preview.HasMismatch;
        salesOrder.MismatchRemarks = null;
        RecalculateSummary(salesOrder);
        salesOrder.StatusHistory = new List<SalesOrderStatusHistoryEntity>
        {
            new SalesOrderStatusHistoryEntity
            {
                FromStatus = null,
                ToStatus = salesOrder.Status,
                Remarks = "Sales order created from purchase order.",
                ChangedBy = createdByUserId,
                ChangedOn = DateTime.UtcNow,
                SalesOrder = salesOrder
            }
        };

        _db.SalesOrders.Add(salesOrder);
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        return await GetByIdAsync(salesOrder.Id) ?? throw new InvalidOperationException("Sales order could not be loaded after creation.");
    }

    public async Task<SalesOrderDetailDto?> UpdateAsync(int id, SalesOrderUpdateRequest request, int updatedByUserId)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }
        if (updatedByUserId <= 0)
        {
            throw new InvalidOperationException("A valid user is required to update a sales order.");
        }

        var salesOrder = await _db.SalesOrders
            .Include(s => s.Items)
            .Include(s => s.Invoices)
            .FirstOrDefaultAsync(s => s.Id == id && !s.IsDeleted);

        if (salesOrder == null)
        {
            return null;
        }

        if (!string.Equals(salesOrder.Status, "Draft", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(salesOrder.Status, "PendingVerification", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only Draft or PendingVerification sales orders can be updated.");
        }

        salesOrder.CustomerPoNumber = string.IsNullOrWhiteSpace(request.CustomerPoNumber) ? salesOrder.CustomerPoNumber : request.CustomerPoNumber;
        salesOrder.CustomerPoDate = request.CustomerPoDate ?? salesOrder.CustomerPoDate;
        salesOrder.BillingAddress = string.IsNullOrWhiteSpace(request.BillingAddress) ? salesOrder.BillingAddress : request.BillingAddress;
        salesOrder.ShippingAddress = string.IsNullOrWhiteSpace(request.ShippingAddress) ? salesOrder.ShippingAddress : request.ShippingAddress;
        salesOrder.CustomerGstin = string.IsNullOrWhiteSpace(request.CustomerGstin) ? salesOrder.CustomerGstin : request.CustomerGstin;
        salesOrder.PaymentTermsText = string.IsNullOrWhiteSpace(request.PaymentTermsText) ? salesOrder.PaymentTermsText : request.PaymentTermsText;
        if (request.MismatchRemarks is not null)
        {
            salesOrder.MismatchRemarks = string.IsNullOrWhiteSpace(request.MismatchRemarks)
                ? null
                : request.MismatchRemarks.Trim();
        }
        if (!string.IsNullOrWhiteSpace(request.BillingType))
        {
            if (request.BillingType is not ("OneTime" or "Advance" or "Milestone" or "Recurring"))
            {
                throw new ArgumentException("BillingType must be OneTime, Advance, Milestone, or Recurring.");
            }
            salesOrder.BillingType = request.BillingType;
        }
        if (request.BankAccountId.HasValue && request.BankAccountId != salesOrder.BankAccountId)
        {
            var bankAccount = await _db.CompanyBankAccounts
                .FirstOrDefaultAsync(account => account.Id == request.BankAccountId.Value && account.IsActive);
            if (bankAccount is null)
            {
                throw new ArgumentException("The selected bank account does not exist or is inactive.");
            }

            salesOrder.BankAccountId = bankAccount.Id;
            salesOrder.BankNameSnapshot = bankAccount.BankName;
            salesOrder.BankAccountNoSnapshot = bankAccount.AccountNo;
            salesOrder.BankAccountTypeSnapshot = bankAccount.AccountType;
            salesOrder.BankIfscSnapshot = bankAccount.Ifsc;
            salesOrder.BankMsmeNoSnapshot = bankAccount.MsmeNo;
        }
        if (request.TermsTemplateId.HasValue && request.TermsTemplateId != salesOrder.TermsTemplateId)
        {
            var termsTemplate = await _db.TermsTemplates
                .FirstOrDefaultAsync(template => template.Id == request.TermsTemplateId.Value && template.IsActive);
            if (termsTemplate is null)
            {
                throw new ArgumentException("The selected terms template does not exist or is inactive.");
            }

            salesOrder.TermsTemplateId = termsTemplate.Id;
            if (string.IsNullOrWhiteSpace(request.TermsAndConditions))
            {
                salesOrder.TermsAndConditions = termsTemplate.Content;
            }
        }
        salesOrder.TermsAndConditions = string.IsNullOrWhiteSpace(request.TermsAndConditions) ? salesOrder.TermsAndConditions : request.TermsAndConditions;
        salesOrder.IsSubscription = request.IsSubscription;
        salesOrder.SubscriptionStart = request.SubscriptionStart ?? salesOrder.SubscriptionStart;
        salesOrder.SubscriptionEnd = request.SubscriptionEnd ?? salesOrder.SubscriptionEnd;
        salesOrder.BillingCycle = request.BillingCycle ?? salesOrder.BillingCycle;
        salesOrder.RenewalTermMonths = request.RenewalTermMonths ?? salesOrder.RenewalTermMonths;
        salesOrder.AutoRenew = request.AutoRenew;
        salesOrder.RenewalReminderDays = request.RenewalReminderDays ?? salesOrder.RenewalReminderDays;
        if (salesOrder.IsSubscription
            && (!salesOrder.SubscriptionStart.HasValue
                || salesOrder.SubscriptionEnd.HasValue && salesOrder.SubscriptionEnd.Value.Date < salesOrder.SubscriptionStart.Value.Date
                || (salesOrder.RenewalTermMonths ?? 0) <= 0))
        {
            throw new ArgumentException("Subscription orders need a start date, valid date range, and positive renewal term.");
        }
        salesOrder.UpdatedBy = updatedByUserId;
        salesOrder.UpdatedOn = DateTime.UtcNow;

        if (request.Items != null && request.Items.Count > 0)
        {
            if (salesOrder.Invoices.Any())
            {
                throw new InvalidOperationException("Items and prices cannot be changed after an invoice has been created.");
            }

            if (request.Items.Any(item =>
                    string.IsNullOrWhiteSpace(item.ItemDescription)
                    || item.Quantity <= 0
                    || item.UnitPrice < 0
                    || item.DiscountPercent < 0
                    || item.DiscountPercent > 100
                    || item.DiscountAmount < 0))
            {
                throw new ArgumentException("Each item needs a description, a positive quantity, a non-negative price, and a discount between 0 and 100 percent.");
            }

            var requestedRateIds = request.Items
                .Where(item => item.GstRateId.HasValue)
                .Select(item => item.GstRateId!.Value)
                .Distinct()
                .ToList();
            var gstRates = await _db.GstRates
                .Where(rate => rate.IsActive && requestedRateIds.Contains(rate.Id))
                .ToDictionaryAsync(rate => rate.Id);
            var defaultGstRate = await _db.GstRates
                .Where(rate => rate.IsActive)
                .OrderByDescending(rate => rate.IgstPct)
                .FirstOrDefaultAsync();

            if (requestedRateIds.Any(rateId => !gstRates.ContainsKey(rateId)))
            {
                throw new ArgumentException("One or more selected GST rates are missing or inactive.");
            }

            _db.SalesOrderItems.RemoveRange(salesOrder.Items);

            var itemList = request.Items.Select((item, index) =>
            {
                var gstRate = item.GstRateId.HasValue
                    ? gstRates[item.GstRateId.Value]
                    : defaultGstRate;
                var gstPercent = gstRate is null
                    ? 18m
                    : salesOrder.IsInterState
                        ? gstRate.IgstPct
                        : gstRate.CgstPct + gstRate.SgstPct;

                return new SalesOrderItemEntity
                {
                    SalesOrderId = salesOrder.Id,
                    LineNo = index + 1,
                    ModuleId = item.ModuleId,
                    ItemDescription = item.ItemDescription.Trim(),
                    HsnSacCode = item.HsnSacCode,
                    Quantity = item.Quantity,
                    Uom = string.IsNullOrWhiteSpace(item.Uom) ? "Nos." : item.Uom,
                    QuotedUnitPrice = item.QuotedUnitPrice,
                    PoUnitPrice = item.PoUnitPrice,
                    UnitPrice = item.UnitPrice,
                    DiscountPercent = item.DiscountPercent,
                    DiscountAmount = item.DiscountAmount,
                    GstRateId = gstRate?.Id,
                    GstPercent = gstPercent,
                    QtyInvoiced = 0m,
                    SalesOrder = salesOrder
                };
            }).ToList();

            salesOrder.Items = itemList;
        }

        salesOrder.VerifiedBy = null;
        salesOrder.VerifiedOn = null;
        salesOrder.VerificationRemarks = null;
        RecalculateSummary(salesOrder);
        await _db.SaveChangesAsync();

        return await GetByIdAsync(salesOrder.Id);
    }

    public async Task<SalesOrderDetailDto?> SubmitAsync(int id, int userId)
    {
        if (userId <= 0)
        {
            throw new InvalidOperationException("A valid user is required to submit a sales order.");
        }

        var salesOrder = await _db.SalesOrders
            .Include(s => s.StatusHistory)
            .Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.Id == id && !s.IsDeleted);

        if (salesOrder == null)
        {
            return null;
        }

        var previousStatus = salesOrder.Status;
        if (!string.Equals(previousStatus, "Draft", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only Draft sales orders can be submitted for verification.");
        }
        if (salesOrder.Items.Count == 0 || salesOrder.GrandTotal <= 0)
        {
            throw new InvalidOperationException("A sales order must contain at least one valid item before it can be submitted.");
        }

        salesOrder.Status = "PendingVerification";
        salesOrder.VerifiedBy = null;
        salesOrder.VerifiedOn = null;
        salesOrder.VerificationRemarks = null;
        salesOrder.UpdatedBy = userId;
        salesOrder.UpdatedOn = DateTime.UtcNow;

        AddStatusHistory(salesOrder, previousStatus, salesOrder.Status, "Submitted for verification.", userId);

        await _db.SaveChangesAsync();
        return await GetByIdAsync(salesOrder.Id);
    }

    public async Task<SalesOrderDetailDto?> VerifyAsync(int id, string action, string? remarks, int userId)
    {
        if (userId <= 0)
        {
            throw new InvalidOperationException("A valid user is required to verify a sales order.");
        }

        var salesOrder = await _db.SalesOrders
            .Include(s => s.StatusHistory)
            .FirstOrDefaultAsync(s => s.Id == id && !s.IsDeleted);

        if (salesOrder == null)
        {
            return null;
        }

        if (!string.Equals(salesOrder.Status, "PendingVerification", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only a sales order in PendingVerification can be verified.");
        }

        var normalizedAction = (action ?? string.Empty).Trim();
        var fromStatus = salesOrder.Status;
        var note = string.IsNullOrWhiteSpace(remarks) ? "Verified." : remarks.Trim();

        if (string.Equals(normalizedAction, "approve", StringComparison.OrdinalIgnoreCase))
        {
            salesOrder.VerifiedBy = userId;
            salesOrder.VerifiedOn = DateTime.UtcNow;
            salesOrder.VerificationRemarks = note;
            salesOrder.UpdatedBy = userId;
            salesOrder.UpdatedOn = DateTime.UtcNow;
        }
        else if (string.Equals(normalizedAction, "sendback", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedAction, "reject", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedAction, "return", StringComparison.OrdinalIgnoreCase))
        {
            salesOrder.Status = "Draft";
            salesOrder.VerifiedBy = null;
            salesOrder.VerifiedOn = null;
            salesOrder.VerificationRemarks = note;
            salesOrder.UpdatedBy = userId;
            salesOrder.UpdatedOn = DateTime.UtcNow;
            AddStatusHistory(salesOrder, fromStatus, salesOrder.Status, note, userId);
        }
        else
        {
            throw new InvalidOperationException("Verification action must be 'approve' or 'sendback'.");
        }

        await _db.SaveChangesAsync();
        return await GetByIdAsync(salesOrder.Id);
    }

    public async Task<SalesOrderDetailDto?> ConfirmAsync(int id, int userId)
    {
        if (userId <= 0)
        {
            throw new InvalidOperationException("A valid user is required to confirm a sales order.");
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var salesOrder = await _db.SalesOrders
            .Include(s => s.StatusHistory)
            .FirstOrDefaultAsync(s => s.Id == id && !s.IsDeleted);

        if (salesOrder == null)
        {
            return null;
        }

        if (string.Equals(salesOrder.Status, "Confirmed", StringComparison.OrdinalIgnoreCase))
        {
            return await GetByIdAsync(salesOrder.Id);
        }

        if (!string.Equals(salesOrder.Status, "PendingVerification", StringComparison.OrdinalIgnoreCase)
            || !salesOrder.VerifiedBy.HasValue)
        {
            throw new InvalidOperationException("Sales orders can only be confirmed after they have been approved in verification.");
        }

        if (salesOrder.HasMismatch && string.IsNullOrWhiteSpace(salesOrder.MismatchRemarks))
        {
            throw new InvalidOperationException("Mismatch remarks are required before confirming the sales order.");
        }

        var previousStatus = salesOrder.Status;
        salesOrder.Status = "Confirmed";
        salesOrder.UpdatedBy = userId;
        salesOrder.UpdatedOn = DateTime.UtcNow;
        AddStatusHistory(salesOrder, previousStatus, salesOrder.Status, "Sales order confirmed.", userId);

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
        return await GetByIdAsync(salesOrder.Id);
    }

    public async Task<SalesOrderDetailDto?> HoldAsync(int id, string reason, int userId)
    {
        if (userId <= 0)
        {
            throw new InvalidOperationException("A valid user is required to place a sales order on hold.");
        }
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A reason is required to place a sales order on hold.", nameof(reason));
        }

        var salesOrder = await _db.SalesOrders
            .Include(s => s.StatusHistory)
            .FirstOrDefaultAsync(s => s.Id == id && !s.IsDeleted);

        if (salesOrder == null)
        {
            return null;
        }

        if (salesOrder.Status is "OnHold" or "Cancelled" or "PartiallyInvoiced" or "Invoiced")
        {
            throw new InvalidOperationException($"Sales orders in {salesOrder.Status} status cannot be placed on hold.");
        }

        var previousStatus = salesOrder.Status;
        salesOrder.Status = "OnHold";
        salesOrder.CancelReason = reason;
        salesOrder.UpdatedBy = userId;
        salesOrder.UpdatedOn = DateTime.UtcNow;
        AddStatusHistory(salesOrder, previousStatus, salesOrder.Status, reason, userId);

        await _db.SaveChangesAsync();
        return await GetByIdAsync(salesOrder.Id);
    }

    public async Task<SalesOrderDetailDto?> CancelAsync(int id, string reason, int userId)
    {
        if (userId <= 0)
        {
            throw new InvalidOperationException("A valid user is required to cancel a sales order.");
        }
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A reason is required to cancel a sales order.", nameof(reason));
        }

        var salesOrder = await _db.SalesOrders
            .Include(s => s.StatusHistory)
            .Include(s => s.Invoices)
            .FirstOrDefaultAsync(s => s.Id == id && !s.IsDeleted);

        if (salesOrder == null)
        {
            return null;
        }

        if (salesOrder.Status is "Cancelled" or "Invoiced")
        {
            throw new InvalidOperationException($"Sales orders in {salesOrder.Status} status cannot be cancelled.");
        }

        if (salesOrder.Invoices.Any(i => !new[] { "cancelled", "void", "rejected" }
                .Contains(i.Status, StringComparer.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Sales order cannot be cancelled while active invoices exist.");
        }

        var previousStatus = salesOrder.Status;
        salesOrder.Status = "Cancelled";
        salesOrder.CancelReason = reason;
        salesOrder.UpdatedBy = userId;
        salesOrder.UpdatedOn = DateTime.UtcNow;
        AddStatusHistory(salesOrder, previousStatus, salesOrder.Status, reason, userId);

        await _db.SaveChangesAsync();
        return await GetByIdAsync(salesOrder.Id);
    }

    public async Task<SalesOrderDetailDto?> UpdateBillingScheduleAsync(
        int id,
        SalesOrderBillingScheduleRequest request,
        int userId)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (userId <= 0)
        {
            throw new InvalidOperationException("A valid user is required to update the billing schedule.");
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var salesOrder = await _db.SalesOrders
            .Include(s => s.BillingSchedule)
            .Include(s => s.Invoices)
            .FirstOrDefaultAsync(s => s.Id == id && !s.IsDeleted);
        if (salesOrder is null)
        {
            return null;
        }

        if (request.Items is null)
        {
            throw new ArgumentException("Billing schedule items are required.");
        }
        if (salesOrder.Status is not ("Draft" or "PendingVerification" or "Confirmed"))
        {
            throw new InvalidOperationException("The billing schedule cannot be changed in the current sales order status.");
        }
        if (salesOrder.Invoices.Any() || salesOrder.BillingSchedule.Any(row => row.InvoiceId.HasValue))
        {
            throw new InvalidOperationException("The billing schedule cannot be replaced after invoicing has started.");
        }
        if (request.Items.Count == 0
            || request.Items.Any(item =>
                string.IsNullOrWhiteSpace(item.MilestoneName)
                || item.Amount < 0
                || item.Percentage is < 0 or > 100))
        {
            throw new ArgumentException("Add at least one valid billing milestone with a non-negative amount.");
        }
        if (Math.Abs(request.Items.Sum(item => item.Amount) - salesOrder.GrandTotal) > 0.01m)
        {
            throw new ArgumentException("Billing schedule amounts must add up to the sales order grand total.");
        }

        _db.SalesOrderBillingSchedule.RemoveRange(salesOrder.BillingSchedule);
        salesOrder.BillingSchedule = request.Items
            .Select((item, index) => new SalesOrderBillingScheduleEntity
            {
                SequenceNo = index + 1,
                MilestoneName = item.MilestoneName.Trim(),
                Percentage = item.Percentage,
                Amount = item.Amount,
                DueDate = item.DueDate?.Date,
                Status = "Pending",
                SalesOrder = salesOrder
            })
            .ToList();
        salesOrder.UpdatedBy = userId;
        salesOrder.UpdatedOn = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
        return await GetByIdAsync(salesOrder.Id);
    }

    public async Task<SalesOrderInvoiceResultDto?> GenerateInvoiceAsync(
        int id,
        SalesOrderGenerateInvoiceRequest request,
        int userId)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Items is null)
        {
            throw new ArgumentException("Invoice items are required.");
        }
        if (userId <= 0)
        {
            throw new InvalidOperationException("A valid user is required to generate an invoice.");
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var salesOrder = await _db.SalesOrders
            .Include(s => s.Items)
            .Include(s => s.BillingSchedule)
            .Include(s => s.StatusHistory)
            .Include(s => s.Customer)
            .Include(s => s.PurchaseOrder)
            .FirstOrDefaultAsync(s => s.Id == id && !s.IsDeleted);
        if (salesOrder is null)
        {
            return null;
        }
        if (salesOrder.Status is not ("Confirmed" or "PartiallyInvoiced"))
        {
            throw new InvalidOperationException("Invoices can only be generated from Confirmed or PartiallyInvoiced sales orders.");
        }

        var schedule = request.BillingScheduleId.HasValue
            ? salesOrder.BillingSchedule.FirstOrDefault(row => row.Id == request.BillingScheduleId.Value)
            : null;
        if (request.BillingScheduleId.HasValue && (schedule is null || schedule.Status != "Pending"))
        {
            throw new InvalidOperationException("The selected billing schedule row is missing or is not pending.");
        }

        var requestedItems = request.Items;
        if (requestedItems.Count == 0 && schedule is null)
        {
            throw new ArgumentException("Select one or more sales order line quantities to invoice.");
        }
        if (requestedItems.Count == 0)
        {
            requestedItems = salesOrder.Items
                .Where(item => item.Quantity > item.QtyInvoiced)
                .Select(item => new SalesOrderInvoiceItemRequest
                {
                    SalesOrderItemId = item.Id,
                    Quantity = item.Quantity - item.QtyInvoiced
                })
                .ToList();
        }
        if (requestedItems.GroupBy(item => item.SalesOrderItemId).Any(group => group.Count() > 1))
        {
            throw new ArgumentException("Each sales order line can appear only once in an invoice request.");
        }

        var invoiceLines = new List<(SalesOrderItemEntity Item, decimal Quantity, decimal Discount, decimal Taxable, decimal Cgst, decimal Sgst, decimal Igst)>();
        foreach (var requestedItem in requestedItems)
        {
            var salesOrderItem = salesOrder.Items.FirstOrDefault(item => item.Id == requestedItem.SalesOrderItemId);
            if (salesOrderItem is null)
            {
                throw new ArgumentException($"Sales order item {requestedItem.SalesOrderItemId} was not found.");
            }
            if (requestedItem.Quantity <= 0
                || requestedItem.Quantity != Math.Round(requestedItem.Quantity, 2)
                || requestedItem.Quantity > salesOrderItem.Quantity - salesOrderItem.QtyInvoiced)
            {
                throw new ArgumentException($"Invoice quantity for '{salesOrderItem.ItemDescription}' must be positive and cannot exceed the uninvoiced quantity.");
            }

            var discount = salesOrderItem.Quantity == 0
                ? 0m
                : Math.Round(salesOrderItem.DiscountAmount * requestedItem.Quantity / salesOrderItem.Quantity, 2, MidpointRounding.AwayFromZero);
            var taxable = Math.Max(requestedItem.Quantity * salesOrderItem.UnitPrice - discount, 0m);
            var tax = Math.Round(taxable * salesOrderItem.GstPercent / 100m, 2, MidpointRounding.AwayFromZero);
            var cgst = salesOrder.IsInterState ? 0m : Math.Round(tax / 2m, 2, MidpointRounding.AwayFromZero);
            var sgst = salesOrder.IsInterState ? 0m : tax - cgst;
            var igst = salesOrder.IsInterState ? tax : 0m;
            invoiceLines.Add((salesOrderItem, requestedItem.Quantity, discount, taxable, cgst, sgst, igst));
        }
        if (invoiceLines.Count == 0)
        {
            throw new ArgumentException("The invoice must contain at least one sales order line.");
        }

        var taxableTotal = invoiceLines.Sum(line => line.Taxable);
        var cgstTotal = invoiceLines.Sum(line => line.Cgst);
        var sgstTotal = invoiceLines.Sum(line => line.Sgst);
        var igstTotal = invoiceLines.Sum(line => line.Igst);
        var exactInvoiceTotal = taxableTotal + cgstTotal + sgstTotal + igstTotal;
        if (schedule is not null && Math.Abs(exactInvoiceTotal - schedule.Amount) > 0.01m)
        {
            throw new ArgumentException("The selected line quantities must total the billing milestone amount.");
        }

        var invoiceNo = await GenerateInvoiceNumberAsync();
        var profile = await _db.CompanyProfiles.FirstOrDefaultAsync(item => item.IsActive);
        var totalBeforeRounding = exactInvoiceTotal;
        var completesSalesOrder = salesOrder.Items.All(item =>
        {
            var billedQuantity = invoiceLines
                .Where(line => line.Item.Id == item.Id)
                .Sum(line => line.Quantity);
            return item.QtyInvoiced + billedQuantity >= item.Quantity;
        });
        var roundOff = completesSalesOrder
            ? salesOrder.GrandTotal - salesOrder.InvoicedAmount - totalBeforeRounding
            : 0m;
        var grandTotal = totalBeforeRounding + roundOff;
        var invoice = new InvoiceEntity
        {
            CustomerId = salesOrder.CustomerId,
            PoId = salesOrder.PurchaseOrderId,
            SalesOrderId = salesOrder.Id,
            QuotationId = salesOrder.QuotationId,
            InvoiceNo = invoiceNo,
            InvoiceDate = DateTime.UtcNow.AddHours(5.5).Date,
            PlaceOfSupply = salesOrder.PlaceOfSupplyState,
            HsnCode = invoiceLines.Select(line => line.Item.HsnSacCode).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
            SgstPct = taxableTotal == 0 ? 0 : sgstTotal * 100m / taxableTotal,
            CgstPct = taxableTotal == 0 ? 0 : cgstTotal * 100m / taxableTotal,
            IgstPct = taxableTotal == 0 ? 0 : igstTotal * 100m / taxableTotal,
            Subtotal = taxableTotal,
            GrandTotal = grandTotal,
            Status = "draft",
            TermsOfSale = salesOrder.TermsAndConditions,
            CreatedAt = DateTime.UtcNow,
            CompanyProfileId = profile?.Id,
            SellerName = profile?.Name,
            SellerAddress = profile?.Address,
            SellerState = profile?.State,
            SellerGstn = profile?.Gstn,
            BuyerName = salesOrder.Customer?.Name,
            BuyerAddress = salesOrder.BillingAddress,
            BuyerState = salesOrder.PlaceOfSupplyState,
            BuyerGstn = salesOrder.CustomerGstin,
            ShipToAddress = salesOrder.ShippingAddress,
            Items = invoiceLines.Select(line => new InvoiceItemEntity
            {
                ModuleId = line.Item.ModuleId,
                Description = line.Item.ItemDescription,
                Qty = line.Quantity,
                Uom = line.Item.Uom ?? "Nos.",
                Rate = line.Item.UnitPrice,
                DiscountPercentage = line.Item.DiscountPercent,
                DiscountAmount = line.Discount
            }).ToList(),
            BankDetails = new InvoiceBankDetailEntity
            {
                BankName = salesOrder.BankNameSnapshot,
                AccountNo = salesOrder.BankAccountNoSnapshot,
                AccountType = salesOrder.BankAccountTypeSnapshot,
                Ifsc = salesOrder.BankIfscSnapshot,
                MsmeNo = salesOrder.BankMsmeNoSnapshot,
                CreatedAt = DateTime.UtcNow
            }
        };
        _db.Invoices.Add(invoice);

        foreach (var line in invoiceLines)
        {
            line.Item.QtyInvoiced += line.Quantity;
        }
        salesOrder.InvoicedAmount += grandTotal;
        var previousStatus = salesOrder.Status;
        salesOrder.Status = salesOrder.Items.All(item => item.QtyInvoiced >= item.Quantity)
            ? "Invoiced"
            : "PartiallyInvoiced";
        salesOrder.UpdatedBy = userId;
        salesOrder.UpdatedOn = DateTime.UtcNow;
        if (schedule is not null)
        {
            schedule.Status = "Invoiced";
            schedule.Invoice = invoice;
        }
        if (!string.Equals(previousStatus, salesOrder.Status, StringComparison.Ordinal))
        {
            AddStatusHistory(salesOrder, previousStatus, salesOrder.Status, $"Invoice {invoiceNo} generated.", userId);
        }

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
        return new SalesOrderInvoiceResultDto
        {
            InvoiceId = invoice.Id,
            InvoiceNo = invoice.InvoiceNo,
            GrandTotal = invoice.GrandTotal
        };
    }

    public async Task<List<int>?> CreateSubscriptionAsync(int id, int userId)
    {
        if (userId <= 0)
        {
            throw new InvalidOperationException("A valid user is required to create a subscription.");
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var salesOrder = await _db.SalesOrders
            .Include(order => order.Items)
            .FirstOrDefaultAsync(order => order.Id == id && !order.IsDeleted);
        if (salesOrder is null)
        {
            return null;
        }
        if (!salesOrder.IsSubscription)
        {
            throw new InvalidOperationException("This sales order is not configured as a subscription.");
        }
        if (salesOrder.Status is not ("Confirmed" or "PartiallyInvoiced" or "Invoiced"))
        {
            throw new InvalidOperationException("A subscription can only be created from a confirmed sales order.");
        }
        if (!salesOrder.SubscriptionStart.HasValue)
        {
            throw new ArgumentException("A subscription start date is required on the sales order.");
        }
        if (await _db.CustomerModuleSubscriptions.AnyAsync(subscription => subscription.SalesOrderId == id))
        {
            throw new InvalidOperationException("A subscription has already been created for this sales order.");
        }

        var moduleItems = salesOrder.Items
            .Where(item => item.ModuleId.HasValue)
            .GroupBy(item => item.ModuleId!.Value)
            .Select(group => group.First())
            .ToList();
        if (moduleItems.Count == 0)
        {
            throw new InvalidOperationException("At least one sales order item must be linked to a module to create a subscription.");
        }

        var subscriptionStart = salesOrder.SubscriptionStart.Value.Date;
        var subscriptionEnd = salesOrder.SubscriptionEnd?.Date
            ?? subscriptionStart.AddMonths(salesOrder.RenewalTermMonths ?? 12);
        var sourceInvoiceId = await _db.Invoices
            .Where(invoice => invoice.SalesOrderId == salesOrder.Id)
            .OrderByDescending(invoice => invoice.InvoiceDate)
            .ThenByDescending(invoice => invoice.Id)
            .Select(invoice => (int?)invoice.Id)
            .FirstOrDefaultAsync();
        foreach (var item in moduleItems)
        {
            var pricing = await _db.ModulePricing
                .Where(entry => entry.ModuleId == item.ModuleId && entry.IsActive)
                .OrderByDescending(entry => entry.PricingEffectiveFrom)
                .FirstOrDefaultAsync();
            _db.CustomerModuleSubscriptions.Add(new CustomerModuleSubscriptionEntity
            {
                CustomerId = salesOrder.CustomerId,
                ModuleId = item.ModuleId!.Value,
                QuotationId = salesOrder.QuotationId,
                SalesOrderId = salesOrder.Id,
                InvoiceId = sourceInvoiceId,
                PurchaseDate = salesOrder.SoDate,
                SubscriptionStartDate = subscriptionStart,
                SubscriptionEndDate = salesOrder.SubscriptionEnd?.Date,
                CurrentYear = 1,
                InitialPurchasePrice = item.UnitPrice,
                RenewalPercentage = pricing?.RenewalPercentage ?? 0m,
                AnnualEscalationPercentage = pricing?.AnnualEscalationPercentage ?? 0m,
                BillingCycle = salesOrder.BillingCycle,
                RenewalTermMonths = salesOrder.RenewalTermMonths,
                AutoRenew = salesOrder.AutoRenew,
                RenewalReminderDays = salesOrder.RenewalReminderDays,
                Status = "active",
                NextRenewalDate = subscriptionEnd,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync();
        var subscriptionIds = await _db.CustomerModuleSubscriptions
            .Where(subscription => subscription.SalesOrderId == id)
            .Select(subscription => subscription.Id)
            .ToListAsync();
        await transaction.CommitAsync();
        return subscriptionIds;
    }

    private static void AddStatusHistory(SalesOrderEntity salesOrder, string? fromStatus, string toStatus, string? remarks, int userId)
    {
        salesOrder.StatusHistory ??= new List<SalesOrderStatusHistoryEntity>();
        salesOrder.StatusHistory.Add(new SalesOrderStatusHistoryEntity
        {
            SalesOrderId = salesOrder.Id,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            Remarks = remarks,
            ChangedBy = userId,
            ChangedOn = DateTime.UtcNow
        });
    }

    private static string GetFinancialYear(DateTime date)
    {
        var fiscalYearStart = date.Month >= 4 ? date.Year : date.Year - 1;
        var fiscalYearEnd = fiscalYearStart + 1;
        return $"{fiscalYearStart}-{fiscalYearEnd.ToString()[2..]}";
    }

    private static bool IsInterState(string? customerState, string? companyState) =>
        !string.IsNullOrWhiteSpace(customerState)
        && !string.IsNullOrWhiteSpace(companyState)
        && !string.Equals(customerState.Trim(), companyState.Trim(), StringComparison.OrdinalIgnoreCase);

    private static void RecalculateSummary(SalesOrderEntity salesOrder)
    {
        var items = salesOrder.Items.ToList();
        if (items.Count == 0)
        {
            salesOrder.SubTotal = 0m;
            salesOrder.DiscountTotal = 0m;
            salesOrder.TaxableAmount = 0m;
            salesOrder.CgstAmount = 0m;
            salesOrder.SgstAmount = 0m;
            salesOrder.IgstAmount = 0m;
            salesOrder.GrandTotal = 0m;
            salesOrder.RoundOff = 0m;
            return;
        }

        foreach (var item in items)
        {
            var gross = item.Quantity * item.UnitPrice;
            item.DiscountAmount = item.DiscountPercent > 0
                ? Math.Round(gross * item.DiscountPercent / 100m, 2, MidpointRounding.AwayFromZero)
                : Math.Min(item.DiscountAmount, gross);
            item.TaxableAmount = Math.Max(gross - item.DiscountAmount, 0m);
            var tax = Math.Round(item.TaxableAmount * item.GstPercent / 100m, 2, MidpointRounding.AwayFromZero);
            item.CgstAmount = salesOrder.IsInterState ? 0m : Math.Round(tax / 2m, 2, MidpointRounding.AwayFromZero);
            item.SgstAmount = salesOrder.IsInterState ? 0m : tax - item.CgstAmount;
            item.IgstAmount = salesOrder.IsInterState ? tax : 0m;
            item.LineTotal = item.TaxableAmount + tax;
        }

        salesOrder.SubTotal = items.Sum(i => i.Quantity * i.UnitPrice);
        salesOrder.DiscountTotal = items.Sum(i => i.DiscountAmount);
        salesOrder.TaxableAmount = items.Sum(i => i.TaxableAmount);
        salesOrder.CgstAmount = items.Sum(i => i.CgstAmount);
        salesOrder.SgstAmount = items.Sum(i => i.SgstAmount);
        salesOrder.IgstAmount = items.Sum(i => i.IgstAmount);
        var totalBeforeRounding = salesOrder.TaxableAmount + salesOrder.CgstAmount + salesOrder.SgstAmount + salesOrder.IgstAmount;
        salesOrder.GrandTotal = Math.Round(totalBeforeRounding, 0, MidpointRounding.AwayFromZero);
        salesOrder.RoundOff = salesOrder.GrandTotal - totalBeforeRounding;
    }

    private async Task<string> GetNextSalesOrderNumberAsync(string financialYear)
    {
        await using var command = _db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "dbo.usp_GetNextSoNumber";
        command.CommandType = CommandType.StoredProcedure;
        command.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction();

        var financialYearParameter = command.CreateParameter();
        financialYearParameter.ParameterName = "@FinancialYear";
        financialYearParameter.DbType = DbType.AnsiString;
        financialYearParameter.Size = 9;
        financialYearParameter.Value = financialYear;
        command.Parameters.Add(financialYearParameter);

        var numberParameter = command.CreateParameter();
        numberParameter.ParameterName = "@SoNumber";
        numberParameter.DbType = DbType.AnsiString;
        numberParameter.Size = 30;
        numberParameter.Direction = ParameterDirection.Output;
        command.Parameters.Add(numberParameter);

        await command.ExecuteNonQueryAsync();
        return numberParameter.Value as string
            ?? throw new InvalidOperationException("The sales order number procedure did not return a number.");
    }

    private static string FormatAmount(decimal amount) =>
        amount.ToString("0.##", CultureInfo.InvariantCulture);

    private static decimal? GetQuotationUnitPrice(QuotationModuleEntity? quotationModule)
    {
        if (quotationModule is null)
        {
            return null;
        }

        var totalPrice = quotationModule.FinalPrice
            ?? quotationModule.ModuleSubtotal
            ?? (quotationModule.ModulePrice ?? 0m) + (quotationModule.ImplementationPrice ?? 0m);
        return totalPrice / Math.Max(quotationModule.NoOfUsers.GetValueOrDefault(1), 1);
    }

    private async Task<string> GenerateInvoiceNumberAsync()
    {
        var now = DateTime.UtcNow.AddHours(5.5);
        var financialYear = $"FY{GetFinancialYear(now)}";
        var prefix = $"BTSS/{financialYear}/INV-";
        var existing = await _db.Invoices
            .Where(invoice => invoice.InvoiceNo.StartsWith(prefix))
            .Select(invoice => invoice.InvoiceNo)
            .ToListAsync();
        var nextNumber = existing
            .Select(number => int.TryParse(number[prefix.Length..], out var value) ? value : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;
        return $"{prefix}{nextNumber:0000}";
    }
}
