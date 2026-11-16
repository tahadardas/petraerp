namespace PetraERP.Application.GeneralLedger.ChartOfAccounts;

/// <summary>Stable audit codes; Deleted=1 preserves the existing deletion history.</summary>
public enum AccountAuditActionCode { Deleted = 1, Created = 2, Updated = 3, Activated = 4, Deactivated = 5 }

/// <summary>Business values at one point in time; no balances, mutable directory labels or JSON payload.</summary>
public sealed record AccountStateDto(
    string Code, string NameArabic, string? NameEnglish, AccountTypeCode AccountType,
    bool PostingAllowed, CostCenterPolicyCode CostCenterPolicy, CurrencyModeCode CurrencyMode,
    string? FixedCurrencyCode, Guid? ParentId, bool IsActive);

public sealed record AccountHistoryDto
{
    public required Guid EventId { get; init; }
    public required Guid AccountId { get; init; }
    public required string Code { get; init; }
    public required string NameArabic { get; init; }
    public string? NameEnglish { get; init; }
    public required AccountAuditActionCode Action { get; init; }
    public required Guid ActorUserId { get; init; }
    /// <summary>Current authorized directory label, not a historical name snapshot.</summary>
    public string? ActorNameArabic { get; init; }
    public string? ActorNameEnglish { get; init; }
    public Guid? WorkingBranchId { get; init; }
    public required Guid CorrelationId { get; init; }
    public string? Reason { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }
    /// <summary>Null for creation or for legacy deletion events without detailed snapshots.</summary>
    public AccountStateDto? Before { get; init; }
    /// <summary>Null for deletion or for legacy deletion events without detailed snapshots.</summary>
    public AccountStateDto? After { get; init; }
}
