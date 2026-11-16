using PetraERP.ApiClient;
using PetraERP.Desktop.Sales;

namespace PetraERP.Desktop.Connection;

public sealed class SalesDocumentLinksClient(DesktopSession session) : ISalesDocumentLinksClient
{
    public Task<SalesDocumentLinkPageResponse> ListAsync(Guid companyId, Guid branchId, SalesLinkSource source,
        Guid sourceId, int page, int pageSize, CancellationToken cancellationToken) => source switch
    {
        SalesLinkSource.Customer => session.Api.ListCustomerSalesLinksAsync(companyId, branchId, sourceId, page, pageSize, cancellationToken),
        SalesLinkSource.Item => session.Api.ListItemSalesLinksAsync(companyId, branchId, sourceId, page, pageSize, cancellationToken),
        SalesLinkSource.Journal => session.Api.ListJournalSalesLinksAsync(companyId, branchId, sourceId, page, pageSize, cancellationToken),
        SalesLinkSource.Movement => session.Api.ListMovementSalesLinksAsync(companyId, branchId, sourceId, page, pageSize, cancellationToken),
        SalesLinkSource.Invoice => session.Api.ListInvoiceMovementLinksAsync(companyId, branchId, sourceId, page, pageSize, cancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(source)),
    };
}
