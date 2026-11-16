namespace PetraERP.Application.Common;

/// <summary>Validated, bounded paging request.</summary>
public sealed record PageRequest
{
    public const int MaximumPageSize = 200;
    public const int DefaultPageSize = 50;

    private PageRequest(int page, int pageSize)
    {
        Page = page;
        PageSize = pageSize;
    }

    public int Page { get; }
    public int PageSize { get; }
    public int Skip => (Page - 1) * PageSize;

    public static PageRequest Create(int? page, int? pageSize)
    {
        var resolvedPage = page ?? 1;
        var resolvedSize = pageSize ?? DefaultPageSize;
        if (resolvedPage < 1 || resolvedPage > 1_000_000)
        {
            throw new RequestValidationException("request.page_out_of_range", "Page must be between 1 and 1000000.", "page");
        }

        if (resolvedSize < 1 || resolvedSize > MaximumPageSize)
        {
            throw new RequestValidationException("request.page_size_out_of_range", "Page size must be between 1 and 200.", "pageSize");
        }

        return new PageRequest(resolvedPage, resolvedSize);
    }
}
