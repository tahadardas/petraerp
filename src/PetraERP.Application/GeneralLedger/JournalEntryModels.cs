using PetraERP.Application.Common;
using PetraERP.Domain.FinancialValues;

namespace PetraERP.Application.GeneralLedger;

public sealed record JournalEntryScope
{
    public Guid CompanyId { get; }
    public Guid BranchId { get; }
    public Guid ActorUserId { get; }

    private JournalEntryScope(Guid companyId, Guid branchId, Guid actorUserId)
    {
        CompanyId = companyId;
        BranchId = branchId;
        ActorUserId = actorUserId;
    }

    public static JournalEntryScope Create(Guid companyId, Guid branchId, Guid actorUserId)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("Company ID is required.", nameof(companyId));
        if (branchId == Guid.Empty) throw new ArgumentException("Branch ID is required.", nameof(branchId));
        if (actorUserId == Guid.Empty) throw new ArgumentException("Actor user ID is required.", nameof(actorUserId));
        return new JournalEntryScope(companyId, branchId, actorUserId);
    }
}

public sealed record JournalDraftLineInput
{
    public required Guid AccountId { get; init; }
    public Guid? CostCenterId { get; init; }
    public required string TransactionCurrencyCode { get; init; }
    public required decimal TransactionDebit { get; init; }
    public required decimal TransactionCredit { get; init; }
    public decimal RateValue { get; init; } = 1.0m;
    public string? RateSourceCurrencyCode { get; init; }
    public string? RateTargetCurrencyCode { get; init; }
    public decimal? BaseDebit { get; init; }
    public decimal? BaseCredit { get; init; }
    public MidpointRounding Rounding { get; init; } = MidpointRounding.ToEven;
    public required string Description { get; init; }
}

public sealed record CreateJournalDraftCommand
{
    public required DateOnly AccountingDate { get; init; }
    public required string Description { get; init; }
    public required IReadOnlyList<JournalDraftLineInput> Lines { get; init; }
}

public sealed record ReviseJournalDraftCommand
{
    public required ConcurrencyToken ExpectedVersion { get; init; }
    public required DateOnly AccountingDate { get; init; }
    public required string Description { get; init; }
    public required IReadOnlyList<JournalDraftLineInput> Lines { get; init; }
    public required string Reason { get; init; }
}

public sealed record JournalDraftActionCommand
{
    public required ConcurrencyToken ExpectedVersion { get; init; }
    public required string Reason { get; init; }
}

public sealed record CreateReversalDraftCommand
{
    public required Guid OriginalJournalId { get; init; }
    public required ConcurrencyToken OriginalExpectedVersion { get; init; }
    public required DateOnly AccountingDate { get; init; }
    public required string Reason { get; init; }
}

public sealed record JournalLineDto
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
    public required decimal TransactionDebit { get; init; }
    public required decimal TransactionCredit { get; init; }
    public required string BaseCurrencyCode { get; init; }
    public required decimal BaseDebit { get; init; }
    public required decimal BaseCredit { get; init; }
    public required string RateSourceCurrencyCode { get; init; }
    public required string RateTargetCurrencyCode { get; init; }
    public required decimal RateValue { get; init; }
    public required byte Rounding { get; init; }
    public required string Description { get; init; }
    public Guid? ReversesLineId { get; init; }
}

public sealed record JournalAuditDto
{
    public required Guid EventId { get; init; }
    public required long Revision { get; init; }
    public required byte Action { get; init; }
    public required string ActionName { get; init; }
    public byte? PreviousState { get; init; }
    public required byte NewState { get; init; }
    public required Guid ActorUserId { get; init; }
    public string? ActorNameArabic { get; init; }
    public string? ActorNameEnglish { get; init; }
    public required string Reason { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }
    public Guid? RelatedEntryId { get; init; }
}

public sealed record JournalDetailsDto
{
    public required Guid Id { get; init; }
    public required Guid CompanyId { get; init; }
    public required Guid BranchId { get; init; }
    public required DateOnly AccountingDate { get; init; }
    public required string Description { get; init; }
    public required byte State { get; init; }
    public required string StateName { get; init; }
    public required long Revision { get; init; }
    public required string VersionBase64 { get; init; }
    public long? PostingSequence { get; init; }
    public Guid? PostingFiscalYearId { get; init; }
    public Guid? PostingPeriodId { get; init; }
    public Guid? ReversesEntryId { get; init; }
    public Guid? ReversedByEntryId { get; init; }
    public Guid? CorrectsEntryId { get; init; }
    public required decimal TotalDebit { get; init; }
    public required decimal TotalCredit { get; init; }
    public required string BaseCurrencyCode { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required DateTimeOffset UpdatedAtUtc { get; init; }
    public required IReadOnlyList<JournalLineDto> Lines { get; init; }
    public required IReadOnlyList<JournalAuditDto> AuditTrail { get; init; }
}

public sealed record JournalSummaryDto
{
    public required Guid Id { get; init; }
    public required DateOnly AccountingDate { get; init; }
    public required string Description { get; init; }
    public required byte State { get; init; }
    public required string StateName { get; init; }
    public long? PostingSequence { get; init; }
    public required decimal TotalDebit { get; init; }
    public required decimal TotalCredit { get; init; }
    public required string BaseCurrencyCode { get; init; }
    public required int LineCount { get; init; }
    public required string VersionBase64 { get; init; }
}

public sealed record AccountLookupDto
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

public sealed record CostCenterLookupDto
{
    public required Guid Id { get; init; }
    public required Guid CompanyId { get; init; }
    public required string Code { get; init; }
    public required string NameArabic { get; init; }
    public string? NameEnglish { get; init; }
    public required bool IsActive { get; init; }
}

public sealed record JournalDraftValidationResult
{
    public required bool IsValid { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }
    public required decimal TotalDebit { get; init; }
    public required decimal TotalCredit { get; init; }
    public required decimal Difference { get; init; }
    public required bool IsBalanced { get; init; }
}
