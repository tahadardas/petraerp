using System.ComponentModel.DataAnnotations;
using PetraERP.Contracts.Financial;

namespace PetraERP.Contracts.Ledger;

public sealed record JournalLineDraftDto
{
    [Required]
    public required Guid AccountId { get; init; }

    public Guid? CostCenterId { get; init; }

    [Required]
    [StringLength(3, MinimumLength = 3)]
    public required string TransactionCurrencyCode { get; init; }

    [Required]
    [RegularExpression(FinancialDecimalText.MoneyPattern)]
    public required string TransactionDebit { get; init; }

    [Required]
    [RegularExpression(FinancialDecimalText.MoneyPattern)]
    public required string TransactionCredit { get; init; }

    public string? RateValue { get; init; }
    public string? RateSourceCurrencyCode { get; init; }
    public string? RateTargetCurrencyCode { get; init; }

    [RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string? BaseDebit { get; init; }

    [RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string? BaseCredit { get; init; }

    public byte Rounding { get; init; }

    [Required]
    [StringLength(1000, MinimumLength = 1)]
    public required string Description { get; init; }
}

public sealed record CreateJournalDraftRequest
{
    [Required]
    public required DateOnly AccountingDate { get; init; }

    [Required]
    [StringLength(1000, MinimumLength = 1)]
    public required string Description { get; init; }

    [Required]
    public required IReadOnlyList<JournalLineDraftDto> Lines { get; init; }
}

public sealed record ReviseJournalDraftRequest
{
    [Required]
    [StringLength(12, MinimumLength = 12)]
    public required string ExpectedVersion { get; init; }

    [Required]
    public required DateOnly AccountingDate { get; init; }

    [Required]
    [StringLength(1000, MinimumLength = 1)]
    public required string Description { get; init; }

    [Required]
    public required IReadOnlyList<JournalLineDraftDto> Lines { get; init; }

    [Required]
    [StringLength(500, MinimumLength = 1)]
    public required string Reason { get; init; }
}

public sealed record CreateReversalDraftRequest
{
    [Required]
    public required Guid OriginalJournalId { get; init; }

    [Required]
    [StringLength(12, MinimumLength = 12)]
    public required string OriginalExpectedVersion { get; init; }

    [Required]
    public required DateOnly AccountingDate { get; init; }

    [Required]
    [StringLength(500, MinimumLength = 1)]
    public required string Reason { get; init; }
}

public sealed record JournalActionRequest
{
    [Required]
    [StringLength(12, MinimumLength = 12)]
    public required string ExpectedVersion { get; init; }

    [Required]
    [StringLength(500, MinimumLength = 1)]
    public required string Reason { get; init; }
}

public sealed record ValidateJournalLinesRequest
{
    [Required]
    public required DateOnly AccountingDate { get; init; }

    [Required]
    public required IReadOnlyList<JournalLineDraftDto> Lines { get; init; }
}

public sealed record JournalLineResponse
{
    public required Guid Id { get; init; }
    public required Guid AccountId { get; init; }
    public required string AccountCode { get; init; }
    public required string AccountNameArabic { get; init; }
    public string? AccountNameEnglish { get; init; }
    public Guid? CostCenterId { get; init; }
    public string? CostCenterCode { get; init; }
    public string? CostCenterNameArabic { get; init; }
    public string? CostCenterNameEnglish { get; init; }
    public required string TransactionCurrencyCode { get; init; }
    public required string TransactionDebit { get; init; }
    public required string TransactionCredit { get; init; }
    public required string BaseCurrencyCode { get; init; }
    public required string BaseDebit { get; init; }
    public required string BaseCredit { get; init; }
    public required string RateSourceCurrencyCode { get; init; }
    public required string RateTargetCurrencyCode { get; init; }
    public required string RateValue { get; init; }
    public required byte Rounding { get; init; }
    public required string Description { get; init; }
    public Guid? ReversesLineId { get; init; }
}

public sealed record JournalAuditResponse
{
    public required Guid EventId { get; init; }
    public required long Revision { get; init; }
    public required byte Action { get; init; }
    public required string ActionName { get; init; }
    public JournalState? PreviousState { get; init; }
    public required JournalState NewState { get; init; }
    public required Guid ActorUserId { get; init; }
    public string? ActorNameArabic { get; init; }
    public string? ActorNameEnglish { get; init; }
    public required string Reason { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }
    public Guid? RelatedEntryId { get; init; }
}

public sealed record JournalDetailsResponse
{
    public required Guid Id { get; init; }
    public required Guid CompanyId { get; init; }
    public required Guid BranchId { get; init; }
    public required DateOnly AccountingDate { get; init; }
    public required string Description { get; init; }
    public required JournalState State { get; init; }
    public required string StateName { get; init; }
    public required long Revision { get; init; }
    public required string Version { get; init; }
    public long? PostingSequence { get; init; }
    public Guid? PostingFiscalYearId { get; init; }
    public Guid? PostingPeriodId { get; init; }
    public Guid? ReversesEntryId { get; init; }
    public Guid? ReversedByEntryId { get; init; }
    public Guid? CorrectsEntryId { get; init; }
    public required string TotalDebit { get; init; }
    public required string TotalCredit { get; init; }
    public required string BaseCurrencyCode { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required DateTimeOffset UpdatedAtUtc { get; init; }
    public required IReadOnlyList<JournalLineResponse> Lines { get; init; }
    public required IReadOnlyList<JournalAuditResponse> AuditTrail { get; init; }
}

public sealed record JournalSummaryItemResponse
{
    public required Guid Id { get; init; }
    public required DateOnly AccountingDate { get; init; }
    public required string Description { get; init; }
    public required JournalState State { get; init; }
    public required string StateName { get; init; }
    public long? PostingSequence { get; init; }
    public required string TotalDebit { get; init; }
    public required string TotalCredit { get; init; }
    public required string BaseCurrencyCode { get; init; }
    public required int LineCount { get; init; }
    public required string Version { get; init; }
}

public sealed record JournalPagedResponse
{
    public required IReadOnlyList<JournalSummaryItemResponse> Items { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required long TotalCount { get; init; }
}

public sealed record JournalValidationResponse
{
    public required bool IsValid { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }
    public required string TotalDebit { get; init; }
    public required string TotalCredit { get; init; }
    public required string Difference { get; init; }
    public required bool IsBalanced { get; init; }
}

public sealed record AccountLookupResponse
{
    public required Guid Id { get; init; }
    public required Guid CompanyId { get; init; }
    public required string Code { get; init; }
    public required string NameArabic { get; init; }
    public string? NameEnglish { get; init; }
    public required byte AccountType { get; init; }
    public required string AccountTypeName { get; init; }
    public required bool PostingAllowed { get; init; }
    public required byte CostCenterPolicy { get; init; }
    public required byte CurrencyMode { get; init; }
    public string? FixedCurrencyCode { get; init; }
    public Guid? ParentId { get; init; }
    public required bool IsActive { get; init; }
}

public sealed record CostCenterLookupResponse
{
    public required Guid Id { get; init; }
    public required Guid CompanyId { get; init; }
    public required string Code { get; init; }
    public required string NameArabic { get; init; }
    public string? NameEnglish { get; init; }
    public required bool IsActive { get; init; }
}
