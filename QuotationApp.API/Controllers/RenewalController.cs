using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuotationApp.API.Data;
using QuotationApp.API.Models;

namespace QuotationApp.API.Controllers;

[ApiController]
[Route("api")]
public sealed class RenewalController(QuotationDbContext db) : ControllerBase
{
    [HttpGet("pricing-history")]
    public async Task<IActionResult> PricingHistory([FromQuery] string? moduleName, CancellationToken ct)
    {
        var query = db.ModulePricing.AsNoTracking().Include(x => x.Module).AsQueryable();
        if (!string.IsNullOrWhiteSpace(moduleName))
            query = query.Where(x => x.Module!.ModuleName == moduleName);

        var rows = await query.OrderByDescending(x => x.PricingEffectiveFrom).ToListAsync(ct);
        return Ok(rows.Select((x, index) => new
        {
            x.Id,
            ModuleName = x.Module!.ModuleName,
            OldPrice = index + 1 < rows.Count && rows[index + 1].ModuleId == x.ModuleId
                ? rows[index + 1].InitialPurchasePrice
                : (decimal?)null,
            NewPrice = x.InitialPurchasePrice,
            ChangeDate = x.PricingEffectiveFrom,
            ChangedBy = (string?)null,
            Reason = (string?)null
        }));
    }

    [HttpPost("pricing-history")]
    public async Task<IActionResult> CreatePricingHistory(PricingHistoryRequest request, CancellationToken ct)
    {
        var module = await FindModule(request.ModuleName, ct);
        if (module is null) return BadRequest(new { error = "The selected module was not found." });

        var effectiveDate = request.ChangeDate?.Date ?? DateTime.UtcNow.Date;
        var current = await db.ModulePricing
            .Where(x => x.ModuleId == module.Id && x.IsActive)
            .ToListAsync(ct);
        foreach (var entry in current) entry.IsActive = false;

        var entity = new ModulePricingEntity
        {
            ModuleId = module.Id,
            InitialPurchasePrice = request.NewPrice,
            RenewalPercentage = 0,
            AnnualEscalationPercentage = 0,
            PricingEffectiveFrom = effectiveDate,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.ModulePricing.Add(entity);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(PricingHistory), new { moduleName = module.ModuleName }, new
        {
            entity.Id,
            ModuleName = module.ModuleName,
            OldPrice = request.OldPrice,
            NewPrice = entity.InitialPurchasePrice,
            ChangeDate = entity.PricingEffectiveFrom,
            ChangedBy = request.ChangedBy,
            Reason = request.Reason
        });
    }

    [HttpGet("customer-subscriptions")]
    public async Task<IActionResult> Subscriptions(CancellationToken ct)
    {
        await MarkExpiredSubscriptionsAsync(DateTime.UtcNow.Date, ct);
        var rows = await db.CustomerModuleSubscriptions.AsNoTracking()
            .Include(x => x.Customer).Include(x => x.Module)
            .OrderBy(x => x.NextRenewalDate).ToListAsync(ct);
        return Ok(rows.Select(ToSubscriptionResponse));
    }

    [HttpPost("customer-subscriptions")]
    public async Task<IActionResult> CreateSubscription(SubscriptionRequest request, CancellationToken ct)
    {
        var resolved = await ResolveSubscription(request, ct);
        if (resolved.Error is not null) return BadRequest(new { error = resolved.Error });
        var entity = resolved.Entity!;
        db.CustomerModuleSubscriptions.Add(entity);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(GetSubscriptionDetails), new { id = entity.Id }, ToSubscriptionResponse(entity));
    }

    [HttpPut("customer-subscriptions/{id:int}")]
    public async Task<IActionResult> UpdateSubscription(int id, SubscriptionRequest request, CancellationToken ct)
    {
        var existing = await db.CustomerModuleSubscriptions.FindAsync([id], ct);
        if (existing is null) return NotFound(new { error = "Subscription not found." });
        var resolved = await ResolveSubscription(request, ct, existing);
        if (resolved.Error is not null) return BadRequest(new { error = resolved.Error });
        await db.SaveChangesAsync(ct);
        return Ok(ToSubscriptionResponse(existing));
    }

    [HttpDelete("customer-subscriptions/{id:int}")]
    public async Task<IActionResult> DeleteSubscription(int id, CancellationToken ct)
    {
        var entity = await db.CustomerModuleSubscriptions.FindAsync([id], ct);
        if (entity is null) return NotFound(new { error = "Subscription not found." });
        db.CustomerModuleSubscriptions.Remove(entity);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("renewal-quotations")]
    public async Task<IActionResult> RenewalQuotations(CancellationToken ct)
    {
        var rows = await db.SubscriptionRenewals.AsNoTracking()
            .Include(x => x.Subscription!).ThenInclude(x => x.Customer)
            .Include(x => x.Subscription!).ThenInclude(x => x.Module)
            .Where(x => x.Status != "pending" && x.Status != "cancelled")
            .OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
        return Ok(rows.Select(ToRenewalResponse));
    }

    [HttpPost("renewal-quotations")]
    public async Task<IActionResult> CreateRenewalQuotation(RenewalQuotationRequest request, CancellationToken ct)
    {
        var subscription = await db.CustomerModuleSubscriptions.FindAsync([request.CustomerSubscriptionId], ct);
        if (subscription is null) return BadRequest(new { error = "Subscription not found." });
        if (request.Year < 2) return BadRequest(new { error = "Renewal year must be at least 2." });

        var exists = await db.SubscriptionRenewals.AnyAsync(
            x => x.SubscriptionId == request.CustomerSubscriptionId && x.RenewalYear == request.Year, ct);
        if (exists) return Conflict(new { error = "A renewal for this subscription year already exists." });

        var start = request.PeriodStart?.Date ?? subscription.SubscriptionStartDate.Date.AddYears(request.Year - 1);
        var end = request.PeriodEnd?.Date ?? start.AddYears(1).AddDays(-1);
        var amount = request.Amount ?? CalculateRenewalAmount(subscription, request.Year);
        var entity = new SubscriptionRenewalEntity
        {
            SubscriptionId = subscription.Id,
            RenewalYear = request.Year,
            PeriodStartDate = start,
            PeriodEndDate = end,
            PreviousAmount = subscription.InitialPurchasePrice,
            EscalationPercentage = subscription.AnnualEscalationPercentage,
            RenewalAmount = amount,
            Status = "quoted",
            CreatedAt = DateTime.UtcNow
        };
        db.SubscriptionRenewals.Add(entity);
        await db.SaveChangesAsync(ct);
        await db.Entry(entity).Reference(x => x.Subscription).Query().Include(x => x.Customer).Include(x => x.Module).LoadAsync(ct);
        return CreatedAtAction(nameof(RenewalQuotations), null, ToRenewalResponse(entity));
    }

    [HttpGet("renewals")]
    public async Task<IActionResult> Renewals([FromQuery] string? filter, CancellationToken ct)
    {
        var today = DateTime.UtcNow.Date;
        await MarkExpiredSubscriptionsAsync(today, ct);
        var query = db.CustomerModuleSubscriptions.AsNoTracking()
            .Include(x => x.Customer)
            .Include(x => x.Module)
            .Include(x => x.Renewals)
            .AsQueryable();
        query = filter switch
        {
            "expired" => query.Where(x => x.Status == "expired" || x.SubscriptionEndDate < today),
            "renewed" => query.Where(x => x.Status == "active" && x.CurrentYear > 1),
            "dueNextMonth" => query.Where(x => x.NextRenewalDate >= today.AddMonths(1) && x.NextRenewalDate < today.AddMonths(2)),
            _ => query.Where(x => x.NextRenewalDate <= today.AddMonths(1) && x.Status == "active")
        };
        var rows = await query.OrderBy(x => x.NextRenewalDate).ToListAsync(ct);
        return Ok(rows.Select(x => new
        {
            SubscriptionId = x.Id,
            RenewalId = x.Renewals
                .OrderByDescending(r => r.RenewalYear)
                .Select(r => (int?)r.Id)
                .FirstOrDefault(),
            QuotationId = x.Renewals
                .OrderByDescending(r => r.RenewalYear)
                .Select(r => r.QuotationId)
                .FirstOrDefault(),
            InvoiceId = x.Renewals
                .OrderByDescending(r => r.RenewalYear)
                .Select(r => r.InvoiceId)
                .FirstOrDefault(),
            CustomerName = x.Customer?.Name,
            ModuleName = x.Module?.ModuleName,
            x.SubscriptionEndDate,
            x.NextRenewalDate,
            DaysToRenewal = x.NextRenewalDate.HasValue ? (x.NextRenewalDate.Value.Date - today).Days : (int?)null,
            Status = filter == "renewed"
                ? "Renewed"
                : x.Status.Equals("active", StringComparison.OrdinalIgnoreCase) ? "Due" : x.Status
        }));
    }

    [HttpPost("renewals/{id:int}/mark-renewed")]
    public async Task<IActionResult> MarkRenewed(int id, CancellationToken ct)
    {
        var entity = await db.CustomerModuleSubscriptions
            .Include(x => x.Renewals)
            .SingleOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return NotFound(new { error = "Subscription not found." });

        var latestRenewal = entity.Renewals
            .OrderByDescending(x => x.RenewalYear)
            .FirstOrDefault();
        if (latestRenewal is null || !latestRenewal.Status.Equals("paid", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "A subscription can be renewed only after its renewal invoice is paid." });

        entity.CurrentYear = latestRenewal.RenewalYear;
        entity.SubscriptionEndDate = latestRenewal.PeriodEndDate;
        entity.NextRenewalDate = latestRenewal.PeriodEndDate.AddDays(1);
        entity.Status = "active";
        await db.SaveChangesAsync(ct);
        return Ok(ToSubscriptionResponse(entity));
    }

    [HttpPost("renewals/{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id, CancellationToken ct)
    {
        var entity = await db.CustomerModuleSubscriptions.FindAsync([id], ct);
        if (entity is null) return NotFound(new { error = "Subscription not found." });
        entity.Status = "cancelled";
        await db.SaveChangesAsync(ct);
        return Ok(ToSubscriptionResponse(entity));
    }

    [HttpPost("renewals/{subscriptionId:int}/prepare")]
    public async Task<IActionResult> PrepareRenewal(
        int subscriptionId,
        [FromQuery] int? year,
        CancellationToken ct)
    {
        var subscription = await db.CustomerModuleSubscriptions
            .Include(x => x.Customer)
            .Include(x => x.Module)
            .Include(x => x.Renewals)
            .SingleOrDefaultAsync(x => x.Id == subscriptionId, ct);
        if (subscription is null) return NotFound(new { error = "Subscription not found." });
        if (subscription.Status.Equals("cancelled", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "A cancelled subscription cannot be renewed." });

        var openRenewal = subscription.Renewals
            .Where(x => x.Status != "paid" && x.Status != "cancelled")
            .OrderByDescending(x => x.RenewalYear)
            .FirstOrDefault();
        if (openRenewal is not null)
            return Ok(ToRenewalResponse(openRenewal));

        var nextRenewalYear = Math.Max(
            subscription.CurrentYear ?? 1,
            subscription.Renewals.Select(x => x.RenewalYear).DefaultIfEmpty(1).Max()) + 1;
        var renewalYear = year ?? nextRenewalYear;
        if (renewalYear < nextRenewalYear)
            return BadRequest(new { error = $"The next valid renewal year is {nextRenewalYear}." });
        var periodStart = subscription.NextRenewalDate?.Date
            ?? subscription.SubscriptionEndDate?.Date.AddDays(1)
            ?? subscription.SubscriptionStartDate.Date.AddYears(renewalYear - 1);
        var periodEnd = periodStart.AddYears(1).AddDays(-1);
        var renewal = new SubscriptionRenewalEntity
        {
            SubscriptionId = subscription.Id,
            RenewalYear = renewalYear,
            PeriodStartDate = periodStart,
            PeriodEndDate = periodEnd,
            PreviousAmount = subscription.InitialPurchasePrice,
            EscalationPercentage = subscription.AnnualEscalationPercentage,
            RenewalAmount = CalculateRenewalAmount(subscription, renewalYear),
            Status = "pending",
            CreatedAt = DateTime.UtcNow,
            Subscription = subscription
        };
        db.SubscriptionRenewals.Add(renewal);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(RenewalQuotations), null, ToRenewalResponse(renewal));
    }

    [HttpPost("renewals/{id:int}/generate-quotation")]
    public async Task<IActionResult> GenerateQuotation(int id, CancellationToken ct) =>
        await SetRenewalStatus(id, "quoted", ct);

    [HttpPost("renewals/{id:int}/link-quotation")]
    public async Task<IActionResult> LinkQuotation(
        int id,
        LinkRenewalQuotationRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.QuotationId))
            return BadRequest(new { error = "QuotationId is required." });

        var quotationId = request.QuotationId.Trim();
        var renewal = await db.SubscriptionRenewals
            .Include(x => x.Subscription!).ThenInclude(x => x.Customer)
            .Include(x => x.Subscription!).ThenInclude(x => x.Module)
            .SingleOrDefaultAsync(x => x.Id == id, ct);
        if (renewal is null) return NotFound(new { error = "Renewal not found." });

        var quotation = await db.Quotations.AsNoTracking()
            .Include(x => x.QuotationModules)
            .SingleOrDefaultAsync(x => x.Id == quotationId, ct);
        if (quotation is null)
            return BadRequest(new { error = "The selected quotation was not found." });

        if (renewal.QuotationId is not null &&
            !string.Equals(renewal.QuotationId, quotationId, StringComparison.Ordinal))
            return Conflict(new { error = "A different quotation is already linked to this renewal." });

        var subscription = renewal.Subscription;
        if (subscription?.Customer is null || subscription.Module is null)
            return BadRequest(new { error = "The renewal subscription is missing customer or module details." });

        if (!string.Equals(
                quotation.QuotationToName.Trim(),
                subscription.Customer.Name.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new
            {
                error = "The quotation customer does not match the renewal subscription customer."
            });
        }

        var hasSubscriptionModule = quotation.QuotationModules.Any(module =>
            string.Equals(
                module.ModuleName.Trim(),
                subscription.Module.ModuleName.Trim(),
                StringComparison.OrdinalIgnoreCase));
        if (!hasSubscriptionModule)
        {
            return BadRequest(new
            {
                error = "The quotation does not contain the renewal subscription module."
            });
        }

        renewal.QuotationId = quotationId;
        if (renewal.Status == "pending")
            renewal.Status = "quoted";
        await db.SaveChangesAsync(ct);
        return Ok(new
        {
            renewalId = renewal.Id,
            subscriptionId = renewal.SubscriptionId,
            quotationId = renewal.QuotationId,
            status = renewal.Status
        });
    }

    [HttpPost("renewals/{id:int}/generate-invoice")]
    public async Task<IActionResult> GenerateInvoice(int id, CancellationToken ct) =>
        await SetRenewalStatus(id, "invoiced", ct);

    [HttpPost("renewals/{id:int}/link-invoice")]
    public async Task<IActionResult> LinkInvoice(
        int id,
        LinkRenewalInvoiceRequest request,
        CancellationToken ct)
    {
        if (!request.InvoiceId.HasValue)
            return BadRequest(new { error = "InvoiceId is required." });

        var renewal = await db.SubscriptionRenewals
            .Include(x => x.Subscription)
            .SingleOrDefaultAsync(x => x.Id == id, ct);
        if (renewal is null) return NotFound(new { error = "Renewal not found." });

        var invoice = await db.Invoices.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == request.InvoiceId.Value, ct);
        if (invoice is null)
            return BadRequest(new { error = "The selected invoice was not found." });

        if (string.IsNullOrWhiteSpace(renewal.QuotationId))
            return BadRequest(new { error = "A renewal quotation must be linked before linking an invoice." });

        if (!string.Equals(renewal.QuotationId, invoice.QuotationId, StringComparison.Ordinal))
            return BadRequest(new { error = "The invoice is not linked to this renewal quotation." });

        if (renewal.Subscription is null)
            return BadRequest(new { error = "The renewal subscription was not found." });

        if (invoice.CustomerId != renewal.Subscription.CustomerId)
            return BadRequest(new
            {
                error = "The invoice customer does not match the renewal subscription customer."
            });

        if (renewal.InvoiceId.HasValue && renewal.InvoiceId != request.InvoiceId.Value)
            return Conflict(new { error = "A different invoice is already linked to this renewal." });

        var linkedToAnotherRenewal = await db.SubscriptionRenewals
            .AnyAsync(x => x.Id != renewal.Id && x.InvoiceId == request.InvoiceId.Value, ct);
        if (linkedToAnotherRenewal)
            return Conflict(new { error = "The selected invoice is already linked to another renewal." });

        renewal.InvoiceId = request.InvoiceId.Value;
        renewal.Status = "invoiced";
        await db.SaveChangesAsync(ct);
        return Ok(new
        {
            renewalId = renewal.Id,
            subscriptionId = renewal.SubscriptionId,
            quotationId = renewal.QuotationId,
            invoiceId = renewal.InvoiceId,
            status = renewal.Status
        });
    }

    [HttpGet("customer-subscriptions/{id:int}/details")]
    public async Task<IActionResult> GetSubscriptionDetails(int id, CancellationToken ct)
    {
        var subscription = await db.CustomerModuleSubscriptions.AsNoTracking()
            .Include(x => x.Customer).Include(x => x.Module)
            .Include(x => x.Renewals).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (subscription is null) return NotFound(new { error = "Subscription not found." });
        var pricing = await db.ModulePricing.AsNoTracking()
            .Where(x => x.ModuleId == subscription.ModuleId).OrderBy(x => x.PricingEffectiveFrom).ToListAsync(ct);
        var invoiceIds = subscription.Renewals
            .Where(x => x.InvoiceId.HasValue)
            .Select(x => x.InvoiceId!.Value)
            .Distinct()
            .ToList();
        var invoicesById = await db.Invoices.AsNoTracking()
            .Where(x => invoiceIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
        return Ok(new
        {
            CustomerName = subscription.Customer?.Name,
            ModuleName = subscription.Module?.ModuleName,
            InitialPurchaseAmount = subscription.InitialPurchasePrice,
            RenewalPercentage = subscription.RenewalPercentage,
            EscalationPercentage = subscription.AnnualEscalationPercentage,
            subscription.Status,
            subscription.SubscriptionStartDate,
            CurrentSubscriptionYear = subscription.CurrentYear,
            YearWisePricing = pricing.Select((x, i) => new { Id = x.Id, Year = i + 1, Price = x.InitialPurchasePrice, EffectiveDate = x.PricingEffectiveFrom }),
            RenewalQuotations = subscription.Renewals.Where(x => x.Status != "pending").Select(ToRenewalResponse),
            RenewalInvoices = subscription.Renewals
                .Where(x => x.InvoiceId.HasValue && invoicesById.ContainsKey(x.InvoiceId.Value))
                .Select(x =>
                {
                    var invoice = invoicesById[x.InvoiceId!.Value];
                    return new
                    {
                        Id = invoice.Id,
                        InvoiceNumber = invoice.InvoiceNo,
                        Year = x.RenewalYear,
                        Amount = invoice.GrandTotal,
                        Date = invoice.InvoiceDate,
                        Status = invoice.Status
                    };
                }),
            PaymentHistory = subscription.Renewals
                .Select(x => new
                {
                    x.Id,
                    Date = x.CreatedAt,
                    x.Status,
                    Amount = (decimal?)x.RenewalAmount,
                    Notes = (string?)null,
                    PaymentMode = (string?)null,
                    TransactionReference = (string?)null
                })
                .Cast<object>()
                .Concat(db.SubscriptionPaymentHistory.AsNoTracking()
                    .Where(x => x.SubscriptionId == id)
                    .OrderBy(x => x.PaymentDate)
                    .Select(x => new
                    {
                        x.Id,
                        Date = x.PaymentDate,
                        x.Status,
                        x.Amount,
                        x.Notes,
                        x.PaymentMode,
                        x.TransactionReference
                    })
                    .Cast<object>()),
        });
    }

    [HttpPost("customer-subscriptions/{id:int}/payment-history")]
    public async Task<IActionResult> AddPaymentHistory(int id, PaymentHistoryRequest request, CancellationToken ct)
    {
        var subscription = await db.CustomerModuleSubscriptions.FindAsync([id], ct);
        if (subscription is null) return NotFound(new { error = "Subscription not found." });

        if (request.Date == default)
            return BadRequest(new { error = "Payment date is required." });
        if (request.Amount < 0)
            return BadRequest(new { error = "Payment amount cannot be negative." });
        var status = (request.Status ?? "pending").Trim().ToLowerInvariant();
        var validHistoryStatuses = new[] { "paid", "pending", "overdue", "refunded" };
        if (!validHistoryStatuses.Contains(status))
            return BadRequest(new { error = "Invalid payment history status." });

        if (request.InvoiceId.HasValue)
        {
            var invoiceBelongsToSubscription = await db.SubscriptionRenewals
                .AnyAsync(x => x.SubscriptionId == id && x.InvoiceId == request.InvoiceId.Value, ct);
            if (!invoiceBelongsToSubscription)
                return BadRequest(new { error = "The selected invoice is not linked to this subscription." });
        }

        var entity = new SubscriptionPaymentHistoryEntity
        {
            SubscriptionId = id,
            InvoiceId = request.InvoiceId,
            PaymentDate = request.Date.Date,
            Amount = request.Amount,
            Status = status,
            PaymentMode = string.IsNullOrWhiteSpace(request.PaymentMode) ? null : request.PaymentMode.Trim(),
            TransactionReference = string.IsNullOrWhiteSpace(request.TransactionReference) ? null : request.TransactionReference.Trim(),
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            CreatedAt = DateTime.UtcNow
        };
        db.SubscriptionPaymentHistory.Add(entity);
        await db.SaveChangesAsync(ct);
        return Ok(new
        {
            entity.Id,
            Date = entity.PaymentDate,
            entity.Status,
            entity.Amount,
            entity.Notes,
            entity.PaymentMode,
            entity.TransactionReference
        });
    }

    private async Task<(CustomerModuleSubscriptionEntity? Entity, string? Error)> ResolveSubscription(
        SubscriptionRequest request, CancellationToken ct, CustomerModuleSubscriptionEntity? existing = null)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(x => x.Name == request.CustomerName, ct);
        var module = await db.Modules.FirstOrDefaultAsync(x => x.ModuleName == request.ModuleName, ct);
        if (customer is null) return (null, "The selected customer was not found.");
        if (module is null) return (null, "The selected module was not found.");

        string? quotationId = null;
        if (!string.IsNullOrWhiteSpace(request.QuotationId))
        {
            quotationId = request.QuotationId.Trim();
            var quotation = await db.Quotations.AsNoTracking()
                .Include(x => x.QuotationModules)
                .SingleOrDefaultAsync(x => x.Id == quotationId, ct);
            if (quotation is null)
                return (null, "The selected quotation was not found.");
            if (!string.Equals(quotation.QuotationToName.Trim(), customer.Name.Trim(), StringComparison.OrdinalIgnoreCase))
                return (null, "The selected quotation does not belong to the selected customer.");
            if (!quotation.QuotationModules.Any(x =>
                    string.Equals(x.ModuleName.Trim(), module.ModuleName.Trim(), StringComparison.OrdinalIgnoreCase)))
                return (null, "The selected quotation does not contain the selected module.");
        }

        var entity = existing ?? new CustomerModuleSubscriptionEntity { CreatedAt = DateTime.UtcNow };
        entity.CustomerId = customer.Id;
        entity.ModuleId = module.Id;
        entity.QuotationId = quotationId ?? existing?.QuotationId;
        entity.PurchaseDate = ParseDate(request.PurchaseDate) ?? DateTime.UtcNow.Date;
        entity.SubscriptionStartDate = ParseDate(request.SubscriptionStartDate) ?? entity.PurchaseDate;
        entity.SubscriptionEndDate = ParseDate(request.SubscriptionEndDate);
        entity.CurrentYear = request.CurrentSubscriptionYear;
        entity.NextRenewalDate = request.NextRenewalDate;
        entity.Status = (request.Status ?? "active").ToLowerInvariant();
        entity.InitialPurchasePrice = request.InitialPurchasePrice ?? module.Price ?? 0;
        entity.RenewalPercentage = request.RenewalPercentage ?? 0;
        entity.AnnualEscalationPercentage = request.EscalationPercentage ?? 0;
        return (entity, null);
    }

    private async Task<ModuleEntity?> FindModule(string? name, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(name) ? null : await db.Modules.FirstOrDefaultAsync(x => x.ModuleName == name, ct);

    private static DateTime? ParseDate(string? value) =>
        DateTime.TryParse(value, out var result) ? result.Date : null;

    private static decimal CalculateRenewalAmount(CustomerModuleSubscriptionEntity x, int year) =>
        Math.Round(x.InitialPurchasePrice * (1 + x.RenewalPercentage / 100m) *
            (decimal)Math.Pow((double)(1 + x.AnnualEscalationPercentage / 100m), year - 2), 2);

    private async Task MarkExpiredSubscriptionsAsync(DateTime today, CancellationToken ct)
    {
        var expired = await db.CustomerModuleSubscriptions
            .Where(x => x.Status == "active" &&
                x.SubscriptionEndDate.HasValue &&
                x.SubscriptionEndDate.Value.Date < today)
            .ToListAsync(ct);

        if (expired.Count == 0) return;

        foreach (var subscription in expired)
            subscription.Status = "expired";

        await db.SaveChangesAsync(ct);
    }

    private static object ToSubscriptionResponse(CustomerModuleSubscriptionEntity x) => new
    {
        x.Id,
        CustomerName = x.Customer?.Name,
        ModuleName = x.Module?.ModuleName,
        QuotationId = x.QuotationId,
        RenewalPercentage = x.RenewalPercentage,
        EscalationPercentage = x.AnnualEscalationPercentage,
        x.PurchaseDate,
        SubscriptionStartDate = x.SubscriptionStartDate,
        x.SubscriptionEndDate,
        CurrentSubscriptionYear = x.CurrentYear,
        x.NextRenewalDate,
        Status = x.Status
    };

    private static object ToRenewalResponse(SubscriptionRenewalEntity x) => new
    {
        RenewalId = x.Id,
        SubscriptionId = x.SubscriptionId,
        QuotationId = x.QuotationId,
        InvoiceId = x.InvoiceId,
        QuotationNumber = $"REN-{x.Id:D6}",
        CustomerName = x.Subscription?.Customer?.Name,
        CustomerAddress = x.Subscription?.Customer?.Address,
        CustomerContactNumber = x.Subscription?.Customer?.ContactNumber,
        CustomerEmail = x.Subscription?.Customer?.Email,
        ModuleName = x.Subscription?.Module?.ModuleName,
        Year = x.RenewalYear,
        Amount = x.RenewalAmount,
        Date = x.CreatedAt,
        Status = x.Status
    };

    private async Task<IActionResult> SetRenewalStatus(int id, string status, CancellationToken ct)
    {
        var renewal = await db.SubscriptionRenewals
            .Include(x => x.Subscription!).ThenInclude(x => x.Customer)
            .Include(x => x.Subscription!).ThenInclude(x => x.Module)
            .SingleOrDefaultAsync(x => x.Id == id, ct);
        if (renewal is null) return NotFound(new { error = "Renewal not found." });
        if (status == "quoted" && renewal.Status == "paid")
            return BadRequest(new { error = "A paid renewal cannot be changed back to quoted." });
        renewal.Status = status;
        await db.SaveChangesAsync(ct);
        return Ok(ToRenewalResponse(renewal));
    }
}
