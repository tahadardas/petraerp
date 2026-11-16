namespace PetraERP.Application.Currencies;

public sealed record FxAccountRevaluationItemResult(
    Guid AccountId,
    string AccountCode,
    string AccountNameArabic,
    string CurrencyCode,
    decimal ForeignBalance,
    decimal BookBaseBalance,
    decimal ClosingRate,
    decimal RevaluedBaseBalance,
    decimal UnrealizedDifference,
    bool IsGain);

public sealed record FxRevaluationPreviewCommand(
    DateOnly AsOfDate,
    string? CurrencyCode = null,
    decimal? OverrideClosingRate = null);

public sealed record FxRevaluationPreviewResult(
    DateOnly AsOfDate,
    string BaseCurrencyCode,
    IReadOnlyList<FxAccountRevaluationItemResult> Items,
    decimal TotalUnrealizedGains,
    decimal TotalUnrealizedLosses,
    decimal NetDifference);

public sealed record ExecuteFxRevaluationCommand(
    DateOnly AsOfDate,
    Guid GainAccountId,
    Guid LossAccountId,
    string IdempotencyKey,
    string? CurrencyCode = null,
    decimal? OverrideClosingRate = null,
    string? Description = null,
    IReadOnlyList<Guid>? SelectedAccountIds = null);

public sealed record FxRevaluationExecutionResult(
    Guid JournalEntryId,
    string JournalEntryNumber,
    int RevaluedAccountsCount,
    decimal TotalGainAmount,
    decimal TotalLossAmount,
    decimal NetRevaluationAmount);

/// <summary>One posted foreign-currency journal line whose own rate differs from the exchange-rate bulletin in force on its date.</summary>
public sealed record FxRateAuditItemResult(
    Guid JournalId,
    long? PostingSequence,
    DateOnly AccountingDate,
    Guid AccountId,
    string AccountCode,
    string AccountNameArabic,
    string Description,
    string CurrencyCode,
    decimal ForeignAmount,
    decimal BookedRate,
    decimal BulletinRate,
    decimal BookedBase,
    decimal BulletinBase,
    decimal Difference);

public sealed record FxRateAuditCommand(
    DateOnly From,
    DateOnly To,
    string? CurrencyCode = null);

public sealed record FxRateAuditResult(
    DateOnly From,
    DateOnly To,
    string BaseCurrencyCode,
    int LinesChecked,
    int LinesWithoutBulletinRate,
    IReadOnlyList<FxRateAuditItemResult> Items,
    decimal TotalDifference);

public interface IFxRevaluationService
{
    /// <summary>
    /// Read-only audit: compares each posted foreign line's own rate with the bulletin rate effective on its accounting date.
    /// Posts nothing and changes nothing; settlement of balances stays with the revaluation workflow.
    /// </summary>
    Task<FxRateAuditResult> AuditRatesAsync(
        Guid companyId,
        Guid branchId,
        FxRateAuditCommand command,
        CancellationToken cancellationToken = default);

    Task<FxRevaluationPreviewResult> PreviewRevaluationAsync(
        Guid companyId,
        Guid branchId,
        FxRevaluationPreviewCommand command,
        CancellationToken cancellationToken = default);

    Task<FxRevaluationExecutionResult> ExecuteRevaluationAsync(
        Guid companyId,
        Guid branchId,
        Guid userId,
        ExecuteFxRevaluationCommand command,
        CancellationToken cancellationToken = default);
}
