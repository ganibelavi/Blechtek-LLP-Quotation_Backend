using QuotationApp.API.Models;

namespace QuotationApp.API.Services;

public interface ISalesOrderService
{
    Task<PagedSalesOrderResultDto> GetPagedAsync(
        int page,
        int pageSize,
        string? status,
        int? customerId,
        DateTime? fromDate,
        DateTime? toDate,
        string? search);

    Task<SalesOrderDetailDto?> GetByIdAsync(int id);
    Task<SalesOrderPreviewDto> GetPreviewAsync(int poId);
    Task<SalesOrderDetailDto> CreateFromPurchaseOrderAsync(int poId, int createdByUserId);
    Task<SalesOrderDetailDto?> UpdateAsync(int id, SalesOrderUpdateRequest request, int updatedByUserId);
    Task<SalesOrderDetailDto?> SubmitAsync(int id, int userId);
    Task<SalesOrderDetailDto?> VerifyAsync(int id, string action, string? remarks, int userId);
    Task<SalesOrderDetailDto?> ConfirmAsync(int id, int userId);
    Task<SalesOrderDetailDto?> HoldAsync(int id, string reason, int userId);
    Task<SalesOrderDetailDto?> CancelAsync(int id, string reason, int userId);
    Task<SalesOrderDetailDto?> UpdateBillingScheduleAsync(int id, SalesOrderBillingScheduleRequest request, int userId);
    Task<SalesOrderInvoiceResultDto?> GenerateInvoiceAsync(int id, SalesOrderGenerateInvoiceRequest request, int userId);
    Task<List<int>?> CreateSubscriptionAsync(int id, int userId);
}
