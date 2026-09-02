namespace QuotationApp.API.Models;

public class CustomerEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? State { get; set; }
    public string? StateCode { get; set; }
    public string? Gstn { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<PurchaseOrderEntity> PurchaseOrders { get; set; } = new List<PurchaseOrderEntity>();
    public ICollection<InvoiceEntity> Invoices { get; set; } = new List<InvoiceEntity>();
}

public class SupplierEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? State { get; set; }
    public string? StateCode { get; set; }
    public string? Gstn { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<PurchaseOrderEntity> PurchaseOrders { get; set; } = new List<PurchaseOrderEntity>();
}

public class ProductEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? HsnSac { get; set; }
    public string Uom { get; set; } = "Nos.";
    public decimal DefaultRate { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class PurchaseOrderEntity
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public int? SupplierId { get; set; }
    public int? QuotationId { get; set; }
    public string? QuotationRefNo { get; set; }
    public DateTime? QuotationRefDate { get; set; }
    public string PoNo { get; set; } = string.Empty;
    public DateTime PoDate { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = "open";
    public string? DeliveryTerms { get; set; }
    public string? PaymentTerms { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<PurchaseOrderItemEntity> Items { get; set; } = new List<PurchaseOrderItemEntity>();
}

public class PurchaseOrderItemEntity
{
    public int Id { get; set; }
    public int PoId { get; set; }
    public int? ProductId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Qty { get; set; } = 1m;
    public string Uom { get; set; } = "Nos.";
    public decimal Rate { get; set; } = 0m;
    public decimal LineTotal => Qty * Rate;

    public PurchaseOrderEntity? PurchaseOrder { get; set; }
}

public class InvoiceEntity
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public int? PoId { get; set; }
    public string InvoiceNo { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; } = DateTime.UtcNow;
    public string? PlaceOfSupply { get; set; }
    public string? HsnCode { get; set; }
    public string? SacCode { get; set; }
    public decimal SgstPct { get; set; }
    public decimal CgstPct { get; set; }
    public decimal IgstPct { get; set; }
    public decimal TdsPct { get; set; }
    public decimal Insurance { get; set; }
    public bool ReverseCharge { get; set; }
    public decimal Subtotal { get; set; }
    public decimal GrandTotal { get; set; }
    public string Status { get; set; } = "unpaid";
    public string? AmountInWords { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<InvoiceItemEntity> Items { get; set; } = new List<InvoiceItemEntity>();
}

public class InvoiceItemEntity
{
    public int Id { get; set; }
    public int InvoiceId { get; set; }
    public int? ProductId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Qty { get; set; } = 1m;
    public string Uom { get; set; } = "Nos.";
    public decimal Rate { get; set; } = 0m;
    public decimal LineTotal => Qty * Rate;

    public InvoiceEntity? Invoice { get; set; }
}

public class CreatePurchaseOrderRequest
{
    public int? CustomerId { get; set; }
    public int? SupplierId { get; set; }
    public string? CompanyName { get; set; }
    public string? PoNo { get; set; }
    public string? PoDate { get; set; }
    public string? Status { get; set; }
    public string? QuotationRefNo { get; set; }
    public string? QuotationRefDate { get; set; }
    public string? BuyerName { get; set; }
    public string? BuyerAddress { get; set; }
    public string? BuyerState { get; set; }
    public string? BuyerStateCode { get; set; }
    public string? BuyerGSTN { get; set; }
    public string? SupplierName { get; set; }
    public string? SupplierAddress { get; set; }
    public string? SupplierState { get; set; }
    public string? SupplierStateCode { get; set; }
    public string? SupplierGSTN { get; set; }
    public string? DeliveryTerms { get; set; }
    public string? PaymentTerms { get; set; }
    public string? ExpectedDeliveryDate { get; set; }
    public string? Notes { get; set; }
    public int? QuotationId { get; set; }
    public decimal TotalAmount { get; set; }
    public List<PurchaseOrderItemRequest> Items { get; set; } = new();
}

public class PurchaseOrderItemRequest
{
    public string? Description { get; set; }
    public decimal Qty { get; set; } = 1m;
    public string? Uom { get; set; } = "Nos.";
    public decimal Rate { get; set; }
}

public class CreateInvoiceRequest
{
    public string? OriginalFor { get; set; }
    public string? CompanyName { get; set; }
    public string? InvoiceNo { get; set; }
    public string? DateOfIssue { get; set; }
    public string? TimeOfIssue { get; set; }
    public string? PlaceOfService { get; set; }
    public string? SupplierName { get; set; }
    public string? SupplierAddress { get; set; }
    public string? SupplierState { get; set; }
    public string? SupplierStateCode { get; set; }
    public string? SupplierGSTN { get; set; }
    public string? BankName { get; set; }
    public string? AccountNo { get; set; }
    public string? AccountType { get; set; }
    public string? Ifsc { get; set; }
    public string? MsmeNo { get; set; }
    public string? ReceiverName { get; set; }
    public string? ReceiverAddress { get; set; }
    public string? ReceiverState { get; set; }
    public string? ReceiverStateCode { get; set; }
    public string? ReceiverGSTN { get; set; }
    public string? ConsigneeName { get; set; }
    public string? ConsigneeAddress { get; set; }
    public string? ConsigneeState { get; set; }
    public string? ConsigneeStateCode { get; set; }
    public string? ConsigneeGSTN { get; set; }
    public string? PoNoDate { get; set; }
    public string? HsnCode { get; set; }
    public string? SacCode { get; set; }
    public string? ReverseCharge { get; set; }
    public string? AmountInWords { get; set; }
    public string? TermsOfSale { get; set; }
    public int? PoId { get; set; }
    public int? QuotationId { get; set; }
    public decimal SgstPct { get; set; }
    public decimal CgstPct { get; set; }
    public decimal IgstPct { get; set; }
    public decimal TdsPct { get; set; }
    public decimal Insurance { get; set; }
    public decimal TotalAmount { get; set; }
    public List<InvoiceItemRequest> Items { get; set; } = new();
}

public class InvoiceItemRequest
{
    public string? Description { get; set; }
    public decimal Qty { get; set; } = 1m;
    public string? Uom { get; set; } = "Nos.";
    public decimal Rate { get; set; }
}
