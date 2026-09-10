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
        var query = db.CustomerModuleSubscriptions.AsNoTracking()
            .Include(x => x.Customer).Include(x => x.Module).AsQueryable();
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
            x.Id, CustomerName = x.Customer?.Name, ModuleName = x.Module?.ModuleName,
            x.SubscriptionEndDate, x.NextRenewalDate,
            DaysToRenewal = x.NextRenewalDate.HasValue ? (x.NextRenewalDate.Value.Date - today).Days : (int?)null,
            Status = x.Status.Equals("active", StringComparison.OrdinalIgnoreCase) ? "Due" : x.Status
        }));
    }

    [HttpPost("renewals/{id:int}/mark-renewed")]
    public async Task<IActionResult> MarkRenewed(int id, CancellationToken ct)
    {
        var entity = await db.CustomerModuleSubscriptions.FindAsync([id], ct);
        if (entity is null) return NotFound(new { error = "Subscription not found." });
        entity.CurrentYear = (entity.CurrentYear ?? 1) + 1;
        entity.Status = "active";
        entity.NextRenewalDate = (entity.NextRenewalDate ?? DateTime.UtcNow.Date).AddYears(1);
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

    [HttpPost("renewals/{id:int}/generate-quotation")]
    public async Task<IActionResult> GenerateQuotation(int id, CancellationToken ct) =>
        await SetRenewalStatus(id, "quoted", ct);

    [HttpPost("renewals/{id:int}/generate-invoice")]
    public async Task<IActionResult> GenerateInvoice(int id, CancellationToken ct) =>
        await SetRenewalStatus(id, "invoiced", ct);

    [HttpGet("customer-subscriptions/{id:int}/details")]
    public async Task<IActionResult> GetSubscriptionDetails(int id, CancellationToken ct)
    {
        var subscription = await db.CustomerModuleSubscriptions.AsNoTracking()
            .Include(x => x.Customer).Include(x => x.Module)
            .Include(x => x.Renewals).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (subscription is null) return NotFound(new { error = "Subscription not found." });
        var pricing = await db.ModulePricing.AsNoTracking()
            .Where(x => x.ModuleId == subscription.ModuleId).OrderBy(x => x.PricingEffectiveFrom).ToListAsync(ct);
        return Ok(new
        {
            CustomerName = subscription.Customer?.Name,
            ModuleName = subscription.Module?.ModuleName,
            InitialPurchaseAmount = subscription.InitialPurchasePrice,
            RenewalPercentage = subscription.RenewalPercentage,
            EscalationPercentage = subscription.AnnualEscalationPercentage,
            subscription.Status, subscription.SubscriptionStartDate,
            CurrentSubscriptionYear = subscription.CurrentYear,
            YearWisePricing = pricing.Select((x, i) => new { Id = x.Id, Year = i + 1, Price = x.InitialPurchasePrice, EffectiveDate = x.PricingEffectiveFrom }),
            RenewalQuotations = subscription.Renewals.Where(x => x.Status != "pending").Select(ToRenewalResponse),
            RenewalInvoices = Array.Empty<object>(),
            PaymentHistory = subscription.Renewals.Select(x => new { x.Id, Date = x.CreatedAt, Status = x.Status, Amount = x.RenewalAmount, Notes = (string?)null })
        });
    }

    [HttpPost("customer-subscriptions/{id:int}/payment-history")]
    public async Task<IActionResult> AddPaymentHistory(int id, PaymentHistoryRequest request, CancellationToken ct)
    {
        var subscription = await db.CustomerModuleSubscriptions.FindAsync([id], ct);
        if (subscription is null) return NotFound(new { error = "Subscription not found." });
        var year = (await db.SubscriptionRenewals.Where(x => x.SubscriptionId == id).MaxAsync(x => (int?)x.RenewalYear, ct) ?? subscription.CurrentYear ?? 1) + 1;
        var entity = new SubscriptionRenewalEntity
        {
            SubscriptionId = id, RenewalYear = year, PeriodStartDate = request.Date.Date,
            PeriodEndDate = request.Date.Date, PreviousAmount = request.Amount ?? 0,
            RenewalAmount = request.Amount ?? 0, Status = (request.Status ?? "pending").ToLowerInvariant(),
            CreatedAt = DateTime.UtcNow
        };
        db.SubscriptionRenewals.Add(entity);
        await db.SaveChangesAsync(ct);
        return Ok(new { entity.Id, Date = entity.CreatedAt, entity.Status, Amount = entity.RenewalAmount, Notes = request.Notes });
    }

    private async Task<(CustomerModuleSubscriptionEntity? Entity, string? Error)> ResolveSubscription(
        SubscriptionRequest request, CancellationToken ct, CustomerModuleSubscriptionEntity? existing = null)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(x => x.Name == request.CustomerName, ct);
        var module = await db.Modules.FirstOrDefaultAsync(x => x.ModuleName == request.ModuleName, ct);
        if (customer is null) return (null, "The selected customer was not found.");
        if (module is null) return (null, "The selected module was not found.");
        var entity = existing ?? new CustomerModuleSubscriptionEntity { CreatedAt = DateTime.UtcNow };
        entity.CustomerId = customer.Id;
        entity.ModuleId = module.Id;
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

    private static object ToSubscriptionResponse(CustomerModuleSubscriptionEntity x) => new
    {
        x.Id, CustomerName = x.Customer?.Name, ModuleName = x.Module?.ModuleName,
        x.PurchaseDate, SubscriptionStartDate = x.SubscriptionStartDate,
        x.SubscriptionEndDate, CurrentSubscriptionYear = x.CurrentYear,
        x.NextRenewalDate, Status = x.Status
    };

    private static object ToRenewalResponse(SubscriptionRenewalEntity x) => new
    {
        x.Id, QuotationNumber = $"REN-{x.Id:D6}",
        CustomerName = x.Subscription?.Customer?.Name,
        ModuleName = x.Subscription?.Module?.ModuleName,
        Year = x.RenewalYear, Amount = x.RenewalAmount,
        Date = x.CreatedAt, Status = x.Status
    };

    private async Task<IActionResult> SetRenewalStatus(int id, string status, CancellationToken ct)
    {
        var renewal = await db.SubscriptionRenewals.FindAsync([id], ct);
        if (renewal is null) return NotFound(new { error = "Renewal not found." });
        renewal.Status = status;
        await db.SaveChangesAsync(ct);
        return Ok(new { renewal.Id, renewal.Status });
    }
}
