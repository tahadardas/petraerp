using PetraERP.ApiClient;
using PetraERP.Desktop.Sales;

namespace PetraERP.Desktop.Connection;

public sealed class SalesInvoiceClient(DesktopSession session) : ISalesInvoiceClient
{
    public Task<SalesInvoicePageResponse> ListSalesInvoicesAsync(
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
        CancellationToken cancellationToken) =>
        session.Api.ListSalesInvoicesAsync(companyId, branchId, page, pageSize, customerId, warehouseId, patternRevisionId, status, fromDate, toDate, cancellationToken: cancellationToken);

    public Task<SalesInvoicePageResponse> SearchSalesInvoicesAsync(
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
        CancellationToken cancellationToken) =>
        session.Api.ListSalesInvoicesAsync(companyId, branchId, page, pageSize, customerId, warehouseId, patternRevisionId, status, fromDate, toDate, null, search, sortBy, sortDescending, cancellationToken);

    public Task<Guid?> NavigateSalesInvoiceAsync(
        Guid companyId,
        Guid branchId,
        string direction,
        Guid? fromInvoiceId,
        SalesInvoiceListScope? scope,
        CancellationToken cancellationToken) =>
        NavigateAsync(companyId, branchId, direction, fromInvoiceId, scope, cancellationToken);

    private async Task<Guid?> NavigateAsync(Guid companyId, Guid branchId, string direction, Guid? from, SalesInvoiceListScope? scope, CancellationToken cancellationToken)
    {
        var result = await session.Api.NavigateSalesInvoiceAsync(companyId, branchId, direction, from, scope?.CustomerId, scope?.WarehouseId, scope?.Status,
            scope?.FromDate, scope?.ToDate, string.IsNullOrWhiteSpace(scope?.Search) ? null : scope!.Search!.Trim(),
            string.IsNullOrWhiteSpace(scope?.SortBy) ? null : scope!.SortBy, string.IsNullOrWhiteSpace(scope?.SortBy) ? null : scope!.SortDescending, cancellationToken).ConfigureAwait(false);
        return result.InvoiceId;
    }

    public Task<SalesInvoiceDetailDto> GetSalesInvoiceAsync(
        Guid companyId,
        Guid branchId,
        Guid invoiceId,
        CancellationToken cancellationToken) =>
        session.Api.GetSalesInvoiceAsync(companyId, branchId, invoiceId, cancellationToken);

    public Task<SalesInvoiceDetailDto> CreateSalesInvoiceAsync(
        Guid companyId,
        Guid branchId,
        CreateSalesInvoiceRequest request,
        CancellationToken cancellationToken) =>
        session.Api.CreateSalesInvoiceAsync(companyId, branchId, request, cancellationToken);

    public Task<SalesInvoiceDetailDto> UpdateSalesInvoiceAsync(
        Guid companyId,
        Guid branchId,
        Guid invoiceId,
        UpdateSalesInvoiceRequest request,
        CancellationToken cancellationToken) =>
        session.Api.UpdateSalesInvoiceAsync(companyId, branchId, invoiceId, request, cancellationToken);

    public Task<DocumentProcessingResultDto> SaveAndProcessSalesInvoiceAsync(
        Guid companyId,
        Guid branchId,
        SaveAndProcessSalesInvoiceRequest request,
        CancellationToken cancellationToken) =>
        session.PostWithOutcomeAsync(companyId, branchId, request.IdempotencyKey, () => session.Api.SaveAndProcessSalesInvoiceAsync(companyId, branchId, request, cancellationToken), cancellationToken);

    public Task<DocumentProcessingResultDto> SaveAndProcessUpdateSalesInvoiceAsync(
        Guid companyId,
        Guid branchId,
        Guid invoiceId,
        SaveAndProcessSalesInvoiceRequest request,
        CancellationToken cancellationToken) =>
        session.PostWithOutcomeAsync(companyId, branchId, request.IdempotencyKey, () => session.Api.SaveAndProcessUpdateSalesInvoiceAsync(companyId, branchId, invoiceId, request, cancellationToken), cancellationToken);

    public Task DeleteSalesInvoiceAsync(
        Guid companyId,
        Guid branchId,
        Guid invoiceId,
        string expectedVersion,
        CancellationToken cancellationToken) =>
        session.Api.DeleteSalesInvoiceAsync(companyId, branchId, invoiceId, expectedVersion, cancellationToken);

    public Task<SalesInvoiceDetailDto> ApproveSalesInvoiceAsync(
        Guid companyId,
        Guid branchId,
        Guid invoiceId,
        string expectedVersion,
        CancellationToken cancellationToken) =>
        session.Api.ApproveSalesInvoiceAsync(companyId, branchId, invoiceId,
            new ApproveSalesInvoiceRequest { ExpectedVersion = expectedVersion }, cancellationToken);

    public Task<SalesInvoiceDetailDto> PostSalesInvoiceAsync(
        Guid companyId,
        Guid branchId,
        Guid invoiceId,
        PostSalesInvoiceRequest request,
        CancellationToken cancellationToken) =>
        session.Api.PostSalesInvoiceAsync(companyId, branchId, invoiceId, request, cancellationToken);

    public Task<SalesInvoiceDetailDto> ReverseSalesInvoiceAsync(
        Guid companyId,
        Guid branchId,
        Guid invoiceId,
        ReverseSalesInvoiceRequest request,
        CancellationToken cancellationToken) =>
        session.Api.ReverseSalesInvoiceAsync(companyId, branchId, invoiceId, request, cancellationToken);

    public Task<SalesInvoiceDetailDto> ReturnSalesInvoiceAsync(
        Guid companyId,
        Guid branchId,
        Guid invoiceId,
        CreateSalesReturnRequest request,
        CancellationToken cancellationToken) =>
        session.Api.ReturnSalesInvoiceAsync(companyId, branchId, invoiceId, request, cancellationToken);
}
