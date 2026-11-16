namespace PetraERP.ApiClient;

/// <summary>The sales-list filters that previous/next/first/last navigation must respect (the list and /navigate share one filter definition on the server).</summary>
public sealed record SalesInvoiceListScope(string? Search = null, Guid? CustomerId = null, Guid? WarehouseId = null,
    SalesInvoiceStatus? Status = null, DateTimeOffset? FromDate = null, DateTimeOffset? ToDate = null,
    string? SortBy = null, bool? SortDescending = null);
