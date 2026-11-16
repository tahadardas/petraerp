namespace PetraERP.Contracts.Ledger;

public enum CostCenterAuditAction { Created = 1, Updated = 2, Activated = 3, Deactivated = 4 }

public sealed record CostCenterStateResponse
{
    public required string Code { get; init; }
    public required string NameArabic { get; init; }
    public string? NameEnglish { get; init; }
    public required bool IsActive { get; init; }
}

public sealed record CostCenterHistoryResponse
{
    public required Guid EventId { get; init; }
    public required Guid CostCenterId { get; init; }
    public required CostCenterAuditAction Action { get; init; }
    public required Guid ActorUserId { get; init; }
    public string? ActorNameArabic { get; init; }
    public string? ActorNameEnglish { get; init; }
    public Guid? WorkingBranchId { get; init; }
    public required Guid CorrelationId { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }
    public CostCenterStateResponse? Before { get; init; }
    public required CostCenterStateResponse After { get; init; }
}

public sealed record CostCenterHistoryPageResponse
{
    public required IReadOnlyList<CostCenterHistoryResponse> Items { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required long TotalCount { get; init; }
}
