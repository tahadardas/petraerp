namespace PetraERP.Contracts.Sales;

public sealed class SalesDocumentLinkPageResponse
{
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public IReadOnlyList<SalesDocumentLinkDto> Items { get; init; } = [];
}

/// <summary>Invoice links, or stock movement links when requested from an invoice. Decimals use invariant strings.</summary>
public sealed class SalesDocumentLinkDto
{
    public Guid Id { get; init; }
    public Guid? ItemId { get; init; }
    public string? Number { get; init; }
    public DateTimeOffset DateUtc { get; init; }
    public string Description { get; init; } = string.Empty;
    public string Amount { get; init; } = string.Empty;
    public string? CurrencyCode { get; init; }
    public string? Quantity { get; init; }
}
