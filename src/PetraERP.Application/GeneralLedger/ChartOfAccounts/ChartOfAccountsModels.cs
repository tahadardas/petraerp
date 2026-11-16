using PetraERP.Application.Common;

namespace PetraERP.Application.GeneralLedger.ChartOfAccounts;

/// <summary>Authenticated company scope for chart-of-accounts master data. Branch is the caller's working context only.</summary>
public sealed record LedgerMasterScope
{
    private LedgerMasterScope(Guid companyId, Guid actorUserId, Guid? workingBranchId, Guid correlationId)
    {
        CompanyId = companyId;
        ActorUserId = actorUserId;
        WorkingBranchId = workingBranchId;
        CorrelationId = correlationId;
    }

    public Guid CompanyId { get; }
    public Guid ActorUserId { get; }
    public Guid? WorkingBranchId { get; }
    public Guid CorrelationId { get; }

    public static LedgerMasterScope Create(Guid companyId, Guid actorUserId, Guid? workingBranchId = null, Guid? correlationId = null)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("Company ID is required.", nameof(companyId));
        if (actorUserId == Guid.Empty) throw new ArgumentException("Actor user ID is required.", nameof(actorUserId));
        if (workingBranchId == Guid.Empty) throw new ArgumentException("A supplied branch ID must not be empty.", nameof(workingBranchId));
        if (correlationId == Guid.Empty) throw new ArgumentException("A supplied correlation ID must not be empty.", nameof(correlationId));
        return new LedgerMasterScope(companyId, actorUserId, workingBranchId, correlationId ?? Guid.NewGuid());
    }
}

/// <summary>Stable codes for chart-of-accounts and calendar management rules.</summary>
public static class LedgerMasterErrorCodes
{
    public const string AccountCodeTaken = "ledger.account_code_taken";
    public const string CostCenterCodeTaken = "ledger.cost_center_code_taken";
    public const string FiscalYearOverlap = "ledger.fiscal_year_overlap";
    public const string FiscalYearCodeTaken = "ledger.fiscal_year_code_taken";
    public const string FiscalYearInvalid = "ledger.fiscal_year_invalid";
    public const string ReportRangeInvalid = "ledger.report_range_invalid";
}

public enum AccountTypeCode
{
    Assets = 1,
    Liabilities = 2,
    Equity = 3,
    Revenue = 4,
    Expenses = 5,
}

/// <summary>Record navigation over the company's accounts in stable code order (codes are unique per company).</summary>
public enum AccountNavigationCode
{
    First = 1,
    Previous = 2,
    Next = 3,
    Last = 4,
}

public enum CostCenterPolicyCode
{
    NotAllowed = 1,
    Optional = 2,
    Required = 3,
}

public enum CurrencyModeCode
{
    BaseCurrencyOnly = 1,
    AnyEnabledCurrency = 2,
    FixedCurrency = 3,
}

public sealed record AccountInput
{
    public required string Code { get; init; }
    public required string NameArabic { get; init; }
    public string? NameEnglish { get; init; }
    public required AccountTypeCode AccountType { get; init; }
    public required bool PostingAllowed { get; init; }
    public required CostCenterPolicyCode CostCenterPolicy { get; init; }
    public required CurrencyModeCode CurrencyMode { get; init; }
    public string? FixedCurrencyCode { get; init; }
    public Guid? ParentId { get; init; }
}

public sealed record AccountDto
{
    public required Guid Id { get; init; }
    public required Guid CompanyId { get; init; }
    public required string Code { get; init; }
    public required string NameArabic { get; init; }
    public string? NameEnglish { get; init; }
    public required AccountTypeCode AccountType { get; init; }
    public required bool PostingAllowed { get; init; }
    public required CostCenterPolicyCode CostCenterPolicy { get; init; }
    public required CurrencyModeCode CurrencyMode { get; init; }
    public string? FixedCurrencyCode { get; init; }
    public Guid? ParentId { get; init; }
    public required int Level { get; init; }
    public required bool HasChildren { get; init; }
    public required bool HasJournalLines { get; init; }
    public required bool IsActive { get; init; }
    public required string VersionBase64 { get; init; }
}

/// <summary>Audit snapshot of a deleted account (the account row itself no longer exists).</summary>
public sealed record AccountAuditDto
{
    public required Guid EventId { get; init; }
    public required Guid AccountId { get; init; }
    public required string Code { get; init; }
    public required string NameArabic { get; init; }
    public string? NameEnglish { get; init; }
    public required AccountTypeCode AccountType { get; init; }
    public Guid? ParentId { get; init; }
    public required Guid ActorUserId { get; init; }
    public required string Reason { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }
}

public sealed record AccountQuery(string? Search, AccountTypeCode? AccountType, bool? IsActive, bool? PostingAllowed, Guid? ParentId, PageRequest Page);

public sealed record CostCenterInput
{
    public required string Code { get; init; }
    public required string NameArabic { get; init; }
    public string? NameEnglish { get; init; }
    public Guid? ParentId { get; init; }
    public byte CenterType { get; init; } = 1; // 1 = Ordinary, 2 = Parent, 3 = Aggregation, 4 = Distribution
    public string? Description { get; init; }
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public string? ResponsiblePerson { get; init; }
}

public sealed record CostCenterDto
{
    public required Guid Id { get; init; }
    public required Guid CompanyId { get; init; }
    public Guid? ParentId { get; init; }
    public byte CenterType { get; init; } = 1;
    public required string Code { get; init; }
    public required string NameArabic { get; init; }
    public string? NameEnglish { get; init; }
    public string? Description { get; init; }
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public string? ResponsiblePerson { get; init; }
    public required bool IsActive { get; init; }
    public required bool HasJournalLines { get; init; }
    public required string VersionBase64 { get; init; }
    public int ChildrenCount { get; init; }
}

public sealed record CostCenterAllocationDto
{
    public Guid Id { get; init; }
    public Guid SourceCostCenterId { get; init; }
    public Guid TargetCostCenterId { get; init; }
    public string TargetCode { get; init; } = string.Empty;
    public string TargetNameArabic { get; init; } = string.Empty;
    public decimal Percentage { get; init; }
}

public sealed record SetCostCenterAllocationInput
{
    public Guid TargetCostCenterId { get; init; }
    public decimal Percentage { get; init; }
}

public sealed record CostCenterQuery(string? Search, bool? IsActive, PageRequest Page);

public interface IChartOfAccountsService
{
    /// <summary>Only the root-to-account path and a bounded, ordinal-code page of immediate children.</summary>
    Task<AccountCardContextDto> GetAccountCardContextAsync(LedgerMasterScope scope, Guid accountId, PageRequest childrenPage, CancellationToken cancellationToken);
    /// <summary>Whole company tree ordered depth-first by code (bounded by the company's chart size).</summary>
    Task<IReadOnlyList<AccountDto>> GetAccountTreeAsync(LedgerMasterScope scope, CancellationToken cancellationToken);
    Task<PagedResult<AccountDto>> SearchAccountsAsync(LedgerMasterScope scope, AccountQuery query, CancellationToken cancellationToken);
    Task<AccountDto> GetAccountAsync(LedgerMasterScope scope, Guid accountId, CancellationToken cancellationToken);
    Task<AccountDto> CreateAccountAsync(LedgerMasterScope scope, AccountInput input, CancellationToken cancellationToken);
    Task<AccountDto> UpdateAccountAsync(LedgerMasterScope scope, Guid accountId, AccountInput input, ConcurrencyToken expectedVersion, CancellationToken cancellationToken);
    Task<AccountDto> SetAccountActiveAsync(LedgerMasterScope scope, Guid accountId, bool isActive, ConcurrencyToken expectedVersion, CancellationToken cancellationToken);
    /// <summary>
    /// Deletes an unused leaf account (no children, no journal lines in any state) under the expected version, and records an
    /// append-only audit snapshot with the reason in the same transaction. Used accounts are rejected; deactivate them instead.
    /// </summary>
    Task DeleteAccountAsync(LedgerMasterScope scope, Guid accountId, ConcurrencyToken expectedVersion, string reason, CancellationToken cancellationToken);
    /// <summary>
    /// The first/last account, or the one before/after <paramref name="fromAccountId"/>, in ordinal code order. One indexed
    /// row is read (keyset), never the whole chart. Returns null at either end.
    /// </summary>
    Task<AccountDto?> NavigateAccountAsync(LedgerMasterScope scope, AccountNavigationCode direction, Guid? fromAccountId, CancellationToken cancellationToken);
    /// <summary>Audit snapshots of accounts deleted in the company, newest first.</summary>
    Task<IReadOnlyList<AccountAuditDto>> GetDeletedAccountsAuditAsync(LedgerMasterScope scope, CancellationToken cancellationToken);
    /// <summary>Company-authorized account history, including retained history after deletion; stable newest-first paging.</summary>
    Task<PagedResult<AccountHistoryDto>> GetAccountHistoryAsync(LedgerMasterScope scope, Guid accountId, PageRequest page, CancellationToken cancellationToken);

    Task<PagedResult<CostCenterDto>> SearchCostCentersAsync(LedgerMasterScope scope, CostCenterQuery query, CancellationToken cancellationToken);
    Task<IReadOnlyList<CostCenterDto>> GetCostCenterTreeAsync(LedgerMasterScope scope, CancellationToken cancellationToken);
    Task<PagedResult<CostCenterHistoryDto>> GetCostCenterHistoryAsync(LedgerMasterScope scope, Guid costCenterId, PageRequest page, CancellationToken cancellationToken);
    Task<CostCenterDto> GetCostCenterAsync(LedgerMasterScope scope, Guid costCenterId, CancellationToken cancellationToken);
    Task<CostCenterDto> CreateCostCenterAsync(LedgerMasterScope scope, CostCenterInput input, CancellationToken cancellationToken);
    Task<CostCenterDto> UpdateCostCenterAsync(LedgerMasterScope scope, Guid costCenterId, CostCenterInput input, ConcurrencyToken expectedVersion, CancellationToken cancellationToken);
    Task<CostCenterDto> SetCostCenterActiveAsync(LedgerMasterScope scope, Guid costCenterId, bool isActive, ConcurrencyToken expectedVersion, CancellationToken cancellationToken);
    Task<IReadOnlyList<CostCenterAllocationDto>> GetCostCenterAllocationsAsync(LedgerMasterScope scope, Guid costCenterId, CancellationToken cancellationToken);
    Task SetCostCenterAllocationsAsync(LedgerMasterScope scope, Guid costCenterId, IReadOnlyList<SetCostCenterAllocationInput> allocations, CancellationToken cancellationToken);
}
