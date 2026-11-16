namespace PetraERP.Application.Common;

/// <summary>A bounded server-side page. Totals are counts of matching rows, never financial sums.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long TotalCount);
