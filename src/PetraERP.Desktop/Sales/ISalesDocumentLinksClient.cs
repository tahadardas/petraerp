using PetraERP.ApiClient;

namespace PetraERP.Desktop.Sales;

public enum SalesLinkSource { Customer, Item, Journal, Movement, Invoice }

public interface ISalesDocumentLinksClient
{
    Task<SalesDocumentLinkPageResponse> ListAsync(Guid companyId, Guid branchId, SalesLinkSource source,
        Guid sourceId, int page, int pageSize, CancellationToken cancellationToken);
}
