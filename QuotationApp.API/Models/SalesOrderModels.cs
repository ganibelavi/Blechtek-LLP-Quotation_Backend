using Microsoft.EntityFrameworkCore;
using QuotationApp.API.Data;
using System.ComponentModel.DataAnnotations.Schema;

namespace QuotationApp.API.Models;

public class SalesOrderEntity
{
    public int Id { get; set; }
    public string SoNumber { get; set; } = string.Empty;
    public DateTime SoDate { get; set; } = DateTime.UtcNow;
    public string? QuotationId { get; set; }
    public int PurchaseOrderId { get; set; }
    public int CustomerId { get; set; }
    public string CustomerPoNumber { get; set; } = string.Empty;
    public DateTime? CustomerPoDate { get; set; }
    public string? BillingAddress { get; set; }
    public string? ShippingAddress { get; set; }
    public string? CustomerGstin { get; set; }
    public string? PlaceOfSupplyState { get; set; }
    public bool IsInterState { get; set; }
    public string CurrencyCode { get; set; } = "INR";
    public decimal SubTotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal RoundOff { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal InvoicedAmount { get; set; }
    public int? PaymentTermsDays { get; set; }
    public string? PaymentTermsText { get; set; }
    public string BillingType { get; set; } = "OneTime";
    public int? BankAccountId { get; set; }
    public string? BankNameSnapshot { get; set; }
    public string? BankAccountNoSnapshot { get; set; }
    public string? BankAccountTypeSnapshot { get; set; }
    public string? BankIfscSnapshot { get; set; }
    public string? BankMsmeNoSnapshot { get; set; }
    public int? TermsTemplateId { get; set; }
    public string? TermsAndConditions { get; set; }
    public bool IsSubscription { get; set; }
    public DateTime? SubscriptionStart { get; set; }
    public DateTime? SubscriptionEnd { get; set; }
    public string? BillingCycle { get; set; }
    public int? RenewalTermMonths { get; set; }
    public bool AutoRenew { get; set; }
    public int? RenewalReminderDays { get; set; } = 30;
    public bool HasMismatch { get; set; }
    public string? MismatchRemarks { get; set; }
    public int? VerifiedBy { get; set; }
    public DateTime? VerifiedOn { get; set; }
    public string? VerificationRemarks { get; set; }
    public string Status { get; set; } = "Draft";
    public string? CancelReason { get; set; }
    public string? InternalRemarks { get; set; }
    public int CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
    public int? UpdatedBy { get; set; }
    public DateTime? UpdatedOn { get; set; }
    public bool IsDeleted { get; set; }

    public byte[] RowVer { get; set; } = Array.Empty<byte>();

    public QuotationEntity? Quotation { get; set; }
    public PurchaseOrderEntity? PurchaseOrder { get; set; }
    public CustomerEntity? Customer { get; set; }
    public CompanyBankAccountEntity? BankAccount { get; set; }
    public TermsTemplateEntity? TermsTemplate { get; set; }
    public UserEntity? VerifiedByUser { get; set; }
    public UserEntity? CreatedByUser { get; set; }

    public ICollection<SalesOrderItemEntity> Items { get; set; } = new List<SalesOrderItemEntity>();
    public ICollection<SalesOrderBillingScheduleEntity> BillingSchedule { get; set; } = new List<SalesOrderBillingScheduleEntity>();
    public ICollection<SalesOrderDocumentEntity> Documents { get; set; } = new List<SalesOrderDocumentEntity>();
    public ICollection<SalesOrderStatusHistoryEntity> StatusHistory { get; set; } = new List<SalesOrderStatusHistoryEntity>();
    public ICollection<InvoiceEntity> Invoices { get; set; } = new List<InvoiceEntity>();
    public ICollection<CustomerModuleSubscriptionEntity> Subscriptions { get; set; } = new List<CustomerModuleSubscriptionEntity>();
}

public class SalesOrderItemEntity
{
    public int Id { get; set; }
    public int SalesOrderId { get; set; }
    public int LineNo { get; set; }
    public int? ModuleId { get; set; }
    public string ItemDescription { get; set; } = string.Empty;
    public string? HsnSacCode { get; set; }
    public decimal Quantity { get; set; } = 1m;
    public string? Uom { get; set; }
    public decimal? QuotedUnitPrice { get; set; }
    public decimal? PoUnitPrice { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxableAmount { get; set; }
    public int? GstRateId { get; set; }
    public decimal GstPercent { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal LineTotal { get; set; }
    public decimal QtyInvoiced { get; set; }

    public SalesOrderEntity? SalesOrder { get; set; }
    public ModuleEntity? Module { get; set; }
    public GstRateEntity? GstRate { get; set; }
}

public class SalesOrderBillingScheduleEntity
{
    public int Id { get; set; }
    public int SalesOrderId { get; set; }
    public int SequenceNo { get; set; }
    public string MilestoneName { get; set; } = string.Empty;
    public decimal? Percentage { get; set; }
    public decimal Amount { get; set; }
    public DateTime? DueDate { get; set; }
    public int? InvoiceId { get; set; }
    public string Status { get; set; } = "Pending";

    public SalesOrderEntity? SalesOrder { get; set; }
    public InvoiceEntity? Invoice { get; set; }
}

public class SalesOrderDocumentEntity
{
    public int Id { get; set; }
    public int SalesOrderId { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public long? FileSizeBytes { get; set; }
    public int UploadedBy { get; set; }
    public DateTime UploadedOn { get; set; } = DateTime.UtcNow;

    public SalesOrderEntity? SalesOrder { get; set; }
    public UserEntity? User { get; set; }
}

public class SalesOrderStatusHistoryEntity
{
    public int Id { get; set; }
    public int SalesOrderId { get; set; }
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = string.Empty;
    public string? Remarks { get; set; }
    public int ChangedBy { get; set; }
    public DateTime ChangedOn { get; set; } = DateTime.UtcNow;

    public SalesOrderEntity? SalesOrder { get; set; }
    public UserEntity? ChangedByUser { get; set; }
}

public class SalesOrderNumberSeriesEntity
{
    public int Id { get; set; }
    public string FinancialYear { get; set; } = string.Empty;
    public string Prefix { get; set; } = "SO";
    public int LastNumber { get; set; }
}
