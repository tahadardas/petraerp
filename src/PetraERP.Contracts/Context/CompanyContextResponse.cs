namespace PetraERP.Contracts.Context;

public sealed record CompanyContextResponse
{
    public required Guid Id { get; init; }
    public required string Code { get; init; }
    public required string NameArabic { get; init; }
    public string? NameEnglish { get; init; }
    public required string BaseCurrencyCode { get; init; }
}

public sealed record CompanyContextListResponse
{
    public required IReadOnlyList<CompanyContextResponse> Items { get; init; }
}

public sealed record BranchContextResponse
{
    public required Guid Id { get; init; }
    public required Guid CompanyId { get; init; }
    public required string Code { get; init; }
    public required string NameArabic { get; init; }
    public string? NameEnglish { get; init; }
}

public sealed record BranchContextListResponse
{
    public required IReadOnlyList<BranchContextResponse> Items { get; init; }
}
