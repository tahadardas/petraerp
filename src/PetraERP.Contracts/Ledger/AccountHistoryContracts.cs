namespace PetraERP.Contracts.Ledger;

public enum AccountAuditAction { Deleted = 1, Created = 2, Updated = 3, Activated = 4, Deactivated = 5 }

public sealed record AccountStateResponse
{
    public required string Code { get; init; }
    public required string NameArabic { get; init; }
    public string? NameEnglish { get; init; }
    public required AccountType AccountType { get; init; }
    public required bool PostingAllowed { get; init; }
    public required CostCenterPolicy CostCenterPolicy { get; init; }
    public required AccountCurrencyMode CurrencyMode { get; init; }
    public string? FixedCurrencyCode { get; init; }
    public Guid? ParentId { get; init; }
    public required bool IsActive { get; init; }
}

public sealed record AccountHistoryResponse
{
    public required Guid EventId { get; init; }
    public required Guid AccountId { get; init; }
    public required string Code { get; init; }
    public required string NameArabic { get; init; }
    public string? NameEnglish { get; init; }
    public required AccountAuditAction Action { get; init; }
    public required Guid ActorUserId { get; init; }
    public string? ActorNameArabic { get; init; }
    public string? ActorNameEnglish { get; init; }
    public Guid? WorkingBranchId { get; init; }
    public required Guid CorrelationId { get; init; }
    public string? Reason { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }
    public AccountStateResponse? Before { get; init; }
    public AccountStateResponse? After { get; init; }
}

public sealed record AccountHistoryPageResponse
{
    public required IReadOnlyList<AccountHistoryResponse> Items { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required long TotalCount { get; init; }
}
