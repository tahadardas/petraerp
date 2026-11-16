namespace PetraERP.Application.GeneralLedger.ChartOfAccounts;

public enum CostCenterAuditActionCode { Created = 1, Updated = 2, Activated = 3, Deactivated = 4 }

public sealed record CostCenterStateDto(string Code, string NameArabic, string? NameEnglish, bool IsActive);

public sealed record CostCenterHistoryDto
{
    public required Guid EventId { get; init; }
    public required Guid CostCenterId { get; init; }
    public required CostCenterAuditActionCode Action { get; init; }
    public required Guid ActorUserId { get; init; }
    /// <summary>Current directory labels, not historical actor-name snapshots.</summary>
    public string? ActorNameArabic { get; init; }
    public string? ActorNameEnglish { get; init; }
    public Guid? WorkingBranchId { get; init; }
    public required Guid CorrelationId { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }
    public CostCenterStateDto? Before { get; init; }
    public required CostCenterStateDto After { get; init; }
}
