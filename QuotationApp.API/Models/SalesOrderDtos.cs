using System.ComponentModel.DataAnnotations;

namespace QuotationApp.API.Models;

public class SalesOrderListItemDto
{
    public int Id { get; set; }
    public string SoNumber { get; set; } = string.Empty;
    public DateTime SoDate { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string PurchaseOrderNo { get; set; } = string.Empty;
    public decimal GrandTotal { get; set; }
    public decimal InvoicedAmount { get; set; }
    public decimal BalanceToInvoice => Math.Max(GrandTotal - InvoicedAmount, 0m);
    public string Status { get; set; } = string.Empty;
}

public class PagedSalesOrderResultDto
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / Math.Max(PageSize, 1));
    public List<SalesOrderListItemDto> Items { get; set; } = new();
}

public class SalesOrderItemDto
{
    public int Id { get; set; }
    public int LineNo { get; set; }
    public int? ModuleId { get; set; }
    public string ModuleName { get; set; } = string.Empty;
    public string ItemDescription { get; set; } = string.Empty;
    public string? HsnSacCode { get; set; }
    public decimal Quantity { get; set; }
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
}

public class SalesOrderBillingScheduleDto
{
    public int Id { get; set; }
    public int SequenceNo { get; set; }
    public string MilestoneName { get; set; } = string.Empty;
    public decimal? Percentage { get; set; }
    public decimal Amount { get; set; }
    public DateTime? DueDate { get; set; }
    public int? InvoiceId { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class SalesOrderDocumentDto
{
    public int Id { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public long? FileSizeBytes { get; set; }
    public int UploadedBy { get; set; }
    public DateTime UploadedOn { get; set; }
}

public class SalesOrderStatusHistoryDto
{
    public int Id { get; set; }
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = string.Empty;
    public string? Remarks { get; set; }
    public int ChangedBy { get; set; }
    public string ChangedByName { get; set; } = string.Empty;
    public DateTime ChangedOn { get; set; }
}

public class SalesOrderDetailDto
{
    public int Id { get; set; }
    public string SoNumber { get; set; } = string.Empty;
    public DateTime SoDate { get; set; }
    public string? QuotationId { get; set; }
    public int PurchaseOrderId { get; set; }
    public string PurchaseOrderNo { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
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
    public int? RenewalReminderDays { get; set; }
    public bool HasMismatch { get; set; }
    public string? MismatchRemarks { get; set; }
    public int? VerifiedBy { get; set; }
    public DateTime? VerifiedOn { get; set; }
    public string? VerificationRemarks { get; set; }
    public string Status { get; set; } = "Draft";
    public string? CancelReason { get; set; }
    public string? InternalRemarks { get; set; }
    public int CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }
    public int? UpdatedBy { get; set; }
    public DateTime? UpdatedOn { get; set; }
    public byte[] RowVer { get; set; } = Array.Empty<byte>();

    public List<SalesOrderItemDto> Items { get; set; } = new();
    public List<SalesOrderBillingScheduleDto> BillingSchedule { get; set; } = new();
    public List<SalesOrderDocumentDto> Documents { get; set; } = new();
    public List<SalesOrderStatusHistoryDto> StatusHistory { get; set; } = new();
    public List<int> InvoiceIds { get; set; } = new();
    public List<int> SubscriptionIds { get; set; } = new();
}

public class SalesOrderPreviewDto
{
    public int PurchaseOrderId { get; set; }
    public string PurchaseOrderNo { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPoNumber { get; set; } = string.Empty;
    public string? BillingAddress { get; set; }
    public string? ShippingAddress { get; set; }
    public string? CustomerGstin { get; set; }
    public string? PlaceOfSupplyState { get; set; }
    public bool IsInterState { get; set; }
    public decimal SubTotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal RoundOff { get; set; }
    public decimal GrandTotal { get; set; }
    public bool HasMismatch { get; set; }
    public string? MismatchRemarks { get; set; }
    public string? PaymentTermsText { get; set; }
    public string? TermsAndConditions { get; set; }
    public List<SalesOrderItemDto> Items { get; set; } = new();
    public List<PoComparisonRowDto> ComparisonRows { get; set; } = new();
}

public class PoComparisonRowDto
{
    public string ItemName { get; set; } = string.Empty;
    public string QuotationValue { get; set; } = string.Empty;
    public string PoValue { get; set; } = string.Empty;
    public string SalesOrderValue { get; set; } = string.Empty;
    public bool HasDifference { get; set; }
}

public class SalesOrderCreateRequest
{
    [Required]
    public int PurchaseOrderId { get; set; }
    public string? CustomerPoNumber { get; set; }
    public DateTime? CustomerPoDate { get; set; }
    public string? BillingAddress { get; set; }
    public string? ShippingAddress { get; set; }
    public string? CustomerGstin { get; set; }
    public string? PaymentTermsText { get; set; }
    public string? BillingType { get; set; }
    public int? BankAccountId { get; set; }
    public int? TermsTemplateId { get; set; }
    public string? TermsAndConditions { get; set; }
    public bool IsSubscription { get; set; }
    public DateTime? SubscriptionStart { get; set; }
    public DateTime? SubscriptionEnd { get; set; }
    public string? BillingCycle { get; set; }
    public int? RenewalTermMonths { get; set; }
    public bool AutoRenew { get; set; }
    public int? RenewalReminderDays { get; set; }
    public List<SalesOrderItemRequestDto> Items { get; set; } = new();
}

public class SalesOrderItemRequestDto
{
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
    public int? GstRateId { get; set; }
    public decimal GstPercent { get; set; }
}

public class SalesOrderVerifyRequest
{
    public string Action { get; set; } = string.Empty;
    public string? Remarks { get; set; }
}

public class SalesOrderReasonRequest
{
    public string Reason { get; set; } = string.Empty;
}

public class SalesOrderBillingScheduleRequest
{
    public List<SalesOrderBillingScheduleItemRequest> Items { get; set; } = new();
}

public class SalesOrderBillingScheduleItemRequest
{
    public string MilestoneName { get; set; } = string.Empty;
    public decimal? Percentage { get; set; }
    public decimal Amount { get; set; }
    public DateTime? DueDate { get; set; }
}

public class SalesOrderGenerateInvoiceRequest
{
    public int? BillingScheduleId { get; set; }
    public List<SalesOrderInvoiceItemRequest> Items { get; set; } = new();
}

public class SalesOrderInvoiceItemRequest
{
    public int SalesOrderItemId { get; set; }
    public decimal Quantity { get; set; }
}

public class SalesOrderInvoiceResultDto
{
    public int InvoiceId { get; set; }
    public string InvoiceNo { get; set; } = string.Empty;
    public decimal GrandTotal { get; set; }
}

public class SalesOrderUpdateRequest
{
    public string? CustomerPoNumber { get; set; }
    public DateTime? CustomerPoDate { get; set; }
    public string? BillingAddress { get; set; }
    public string? ShippingAddress { get; set; }
    public string? CustomerGstin { get; set; }
    public string? PaymentTermsText { get; set; }
    public string? MismatchRemarks { get; set; }
    public string? BillingType { get; set; }
    public int? BankAccountId { get; set; }
    public int? TermsTemplateId { get; set; }
    public string? TermsAndConditions { get; set; }
    public bool IsSubscription { get; set; }
    public DateTime? SubscriptionStart { get; set; }
    public DateTime? SubscriptionEnd { get; set; }
    public string? BillingCycle { get; set; }
    public int? RenewalTermMonths { get; set; }
    public bool AutoRenew { get; set; }
    public int? RenewalReminderDays { get; set; }
    public List<SalesOrderItemRequestDto> Items { get; set; } = new();
}
