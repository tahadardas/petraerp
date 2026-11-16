namespace PetraERP.Application.Sales;

public enum SalesLinkSource { Customer, Item, Journal, Movement, Invoice }

/// <summary>Read-only links resolved from authoritative relationships in one company and branch.</summary>
public interface ISalesDocumentLinkService
{
    Task<SalesDocumentLinkPage> GetAsync(Guid companyId, Guid branchId, SalesLinkSource source,
        Guid sourceId, int page, int pageSize, CancellationToken cancellationToken = default);
}

public sealed record SalesDocumentLink(Guid Id, Guid? ItemId, string? Number, DateTimeOffset DateUtc,
    string Description, decimal Amount, string? CurrencyCode, decimal? Quantity);

public sealed record SalesDocumentLinkPage(int Page, int PageSize, int TotalCount,
    IReadOnlyList<SalesDocumentLink> Items);
