using PetraERP.Application.Common;

namespace PetraERP.Application.Sales;

public interface ISalesInvoiceService
{
    Task<SalesInvoicePageResult> GetInvoicesAsync(
        Guid companyId,
        Guid branchId,
        SalesInvoiceSearchQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the id of the invoice adjacent to <paramref name="currentInvoiceId"/> in a fixed (date, created-at, id) order
    /// over the whole branch, or null when there is none in that direction.
    /// </summary>
    Task<Guid?> GetNeighborInvoiceIdAsync(
        Guid companyId,
        Guid branchId,
        Guid? currentInvoiceId,
        SalesInvoiceNavigationDirection direction,
        SalesInvoiceSearchQuery? filter = null,
        CancellationToken cancellationToken = default);

    Task<SalesInvoiceDetailResult> GetInvoiceAsync(
        Guid companyId,
        Guid branchId,
        Guid invoiceId,
        CancellationToken cancellationToken = default);

    Task<SalesInvoiceDetailResult> CreateInvoiceAsync(
        Guid companyId,
        Guid branchId,
        Guid userId,
        CreateSalesInvoiceCommand command,
        CancellationToken cancellationToken = default);

    Task<SalesInvoiceDetailResult> UpdateInvoiceAsync(
        Guid companyId,
        Guid branchId,
        Guid invoiceId,
        UpdateSalesInvoiceCommand command,
        CancellationToken cancellationToken = default);

    Task<SalesInvoiceDetailResult> ApproveInvoiceAsync(
        Guid companyId,
        Guid branchId,
        Guid invoiceId,
        Guid userId,
        ConcurrencyToken expectedVersion,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a draft invoice (never an approved/posted one) under optimistic concurrency.</summary>
    Task DeleteDraftAsync(
        Guid companyId,
        Guid branchId,
        Guid invoiceId,
        ConcurrencyToken expectedVersion,
        CancellationToken cancellationToken = default);

    Task<SalesInvoiceDetailResult> PostInvoiceAsync(
        Guid companyId,
        Guid branchId,
        Guid invoiceId,
        Guid userId,
        PostSalesInvoiceCommand command,
        CancellationToken cancellationToken = default);

    Task<SalesInvoiceDetailResult> ReverseInvoiceAsync(
        Guid companyId,
        Guid branchId,
        Guid invoiceId,
        Guid userId,
        ReverseSalesInvoiceCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Posts a return of the given original invoice and returns the new return document.</summary>
    Task<SalesInvoiceDetailResult> ReturnInvoiceAsync(
        Guid companyId,
        Guid branchId,
        Guid invoiceId,
        Guid userId,
        CreateSalesReturnCommand command,
        CancellationToken cancellationToken = default);
}
