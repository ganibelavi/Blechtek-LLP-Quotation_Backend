using QuotationApp.API.Data;

namespace QuotationApp.API.Models;

public sealed class ModulePricingEntity
{
    public int Id { get; set; }
    public int ModuleId { get; set; }
    public decimal InitialPurchasePrice { get; set; }
    public decimal RenewalPercentage { get; set; }
    public decimal AnnualEscalationPercentage { get; set; }
    public DateTime PricingEffectiveFrom { get; set; }
    public DateTime? PricingEffectiveTo { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public ModuleEntity? Module { get; set; }
}

public sealed class CustomerModuleSubscriptionEntity
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public int ModuleId { get; set; }
    public string? QuotationId { get; set; }
    public DateTime PurchaseDate { get; set; }
    public DateTime SubscriptionStartDate { get; set; }
    public DateTime? SubscriptionEndDate { get; set; }
    public int? CurrentYear { get; set; }
    public decimal InitialPurchasePrice { get; set; }
    public decimal RenewalPercentage { get; set; }
    public decimal AnnualEscalationPercentage { get; set; }
    public string Status { get; set; } = "active";
    public DateTime? NextRenewalDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public CustomerEntity? Customer { get; set; }
    public ModuleEntity? Module { get; set; }
    public ICollection<SubscriptionRenewalEntity> Renewals { get; set; } = new List<SubscriptionRenewalEntity>();
}

public sealed class SubscriptionRenewalEntity
{
    public int Id { get; set; }
    public int SubscriptionId { get; set; }
    public int RenewalYear { get; set; }
    public DateTime PeriodStartDate { get; set; }
    public DateTime PeriodEndDate { get; set; }
    public decimal PreviousAmount { get; set; }
    public decimal EscalationPercentage { get; set; }
    public decimal RenewalAmount { get; set; }
    public string? QuotationId { get; set; }
    public int? InvoiceId { get; set; }
    public string Status { get; set; } = "pending";
    public DateTime CreatedAt { get; set; }
    public CustomerModuleSubscriptionEntity? Subscription { get; set; }
}

public sealed class SubscriptionRequest
{
    public string? CustomerName { get; set; }
    public string? ModuleName { get; set; }
    public string? PurchaseDate { get; set; }
    public string? SubscriptionStartDate { get; set; }
    public string? SubscriptionEndDate { get; set; }
    public int? CurrentSubscriptionYear { get; set; }
    public DateTime? NextRenewalDate { get; set; }
    public string? Status { get; set; }
    public decimal? InitialPurchasePrice { get; set; }
    public decimal? RenewalPercentage { get; set; }
    public decimal? EscalationPercentage { get; set; }
}

public sealed class PricingHistoryRequest
{
    public string? ModuleName { get; set; }
    public decimal? OldPrice { get; set; }
    public decimal NewPrice { get; set; }
    public DateTime? ChangeDate { get; set; }
    public string? ChangedBy { get; set; }
    public string? Reason { get; set; }
}

public sealed class RenewalQuotationRequest
{
    public int CustomerSubscriptionId { get; set; }
    public int Year { get; set; }
    public decimal? Amount { get; set; }
    public DateTime? PeriodStart { get; set; }
    public DateTime? PeriodEnd { get; set; }
}

public sealed class PaymentHistoryRequest
{
    public DateTime Date { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public string? Notes { get; set; }
}
