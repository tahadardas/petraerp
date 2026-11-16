using PetraERP.ApiClient;

namespace PetraERP.Desktop.Sales;

public interface ISalesInvoiceClient
{
    Task<SalesInvoicePageResponse> ListSalesInvoicesAsync(
        Guid companyId,
        Guid branchId,
        int page,
        int pageSize,
        Guid? customerId,
        Guid? warehouseId,
        Guid? patternRevisionId,
        SalesInvoiceStatus? status,
        DateTimeOffset? fromDate,
        DateTimeOffset? toDate,
        CancellationToken cancellationToken);

    Task<SalesInvoicePageResponse> SearchSalesInvoicesAsync(
        Guid companyId,
        Guid branchId,
        int page,
        int pageSize,
        string? search,
        Guid? customerId,
        Guid? warehouseId,
        Guid? patternRevisionId,
        SalesInvoiceStatus? status,
        DateTimeOffset? fromDate,
        DateTimeOffset? toDate,
        string? sortBy,
        bool? sortDescending,
        CancellationToken cancellationToken);

    /// <summary>Id of the first/previous/next/last invoice over the whole branch in a fixed order; null when none.</summary>
    Task<Guid?> NavigateSalesInvoiceAsync(
        Guid companyId,
        Guid branchId,
        string direction,
        Guid? fromInvoiceId,
        SalesInvoiceListScope? scope,
        CancellationToken cancellationToken);

    Task<SalesInvoiceDetailDto> GetSalesInvoiceAsync(
        Guid companyId,
        Guid branchId,
        Guid invoiceId,
        CancellationToken cancellationToken);

    Task<SalesInvoiceDetailDto> CreateSalesInvoiceAsync(
        Guid companyId,
        Guid branchId,
        CreateSalesInvoiceRequest request,
        CancellationToken cancellationToken);

    Task<SalesInvoiceDetailDto> UpdateSalesInvoiceAsync(
        Guid companyId,
        Guid branchId,
        Guid invoiceId,
        UpdateSalesInvoiceRequest request,
        CancellationToken cancellationToken);

    Task<DocumentProcessingResultDto> SaveAndProcessSalesInvoiceAsync(
        Guid companyId,
        Guid branchId,
        SaveAndProcessSalesInvoiceRequest request,
        CancellationToken cancellationToken);

    Task<DocumentProcessingResultDto> SaveAndProcessUpdateSalesInvoiceAsync(
        Guid companyId,
        Guid branchId,
        Guid invoiceId,
        SaveAndProcessSalesInvoiceRequest request,
        CancellationToken cancellationToken);

    Task<SalesInvoiceDetailDto> ApproveSalesInvoiceAsync(
        Guid companyId,
        Guid branchId,
        Guid invoiceId,
        string expectedVersion,
        CancellationToken cancellationToken);

    /// <summary>Deletes a never-approved draft invoice (posted invoices are reversed instead).</summary>
    Task DeleteSalesInvoiceAsync(
        Guid companyId,
        Guid branchId,
        Guid invoiceId,
        string expectedVersion,
        CancellationToken cancellationToken);

    Task<SalesInvoiceDetailDto> PostSalesInvoiceAsync(
        Guid companyId,
        Guid branchId,
        Guid invoiceId,
        PostSalesInvoiceRequest request,
        CancellationToken cancellationToken);

    Task<SalesInvoiceDetailDto> ReverseSalesInvoiceAsync(
        Guid companyId,
        Guid branchId,
        Guid invoiceId,
        ReverseSalesInvoiceRequest request,
        CancellationToken cancellationToken);

    Task<SalesInvoiceDetailDto> ReturnSalesInvoiceAsync(
        Guid companyId,
        Guid branchId,
        Guid invoiceId,
        CreateSalesReturnRequest request,
        CancellationToken cancellationToken);
}
