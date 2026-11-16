namespace PetraERP.Application.Workspaces.Rollover;

public sealed record RolloverPreviewQuery(
    Guid CompanyId,
    Guid SourceFiscalYearId,
    Guid TargetFiscalYearId,
    Guid? RetainedEarningsAccountId);

public sealed record ExecuteRolloverCommand(
    Guid CompanyId,
    Guid SourceFiscalYearId,
    Guid TargetFiscalYearId,
    Guid? RetainedEarningsAccountId,
    string SourceFingerprint,
    string Reason,
    string IdempotencyKey);

public sealed record RolloverAccountLineDto(
    Guid AccountId,
    string AccountCode,
    string AccountNameArabic,
    byte AccountType,
    decimal ClosingDebit,
    decimal ClosingCredit,
    string CurrencyCode);

public sealed record RolloverPreviewDto(
    Guid CompanyId,
    Guid SourceFiscalYearId,
    string SourceYearCode,
    Guid TargetFiscalYearId,
    string TargetYearCode,
    Guid RetainedEarningsAccountId,
    string RetainedEarningsAccountCode,
    decimal TotalRevenues,
    decimal TotalExpenses,
    decimal NetIncome,
    decimal TotalBalanceSheetDebit,
    decimal TotalBalanceSheetCredit,
    bool IsTrialBalanceBalanced,
    int OpenItemsCount,
    decimal OpenItemsTotalAmount,
    int StockItemsCount,
    decimal StockTotalValue,
    string SourceFingerprint,
    IReadOnlyList<RolloverAccountLineDto> AccountLines);

public sealed record RolloverResultDto(
    Guid RolloverBatchId,
    Guid CompanyId,
    Guid SourceFiscalYearId,
    Guid TargetFiscalYearId,
    string Status,
    Guid OpeningBalanceBatchId,
    Guid OpeningJournalId,
    int OpenItemsCreated,
    int StockMovementsCreated,
    bool TargetDatabaseActivated,
    string Message);

public interface IFiscalYearRolloverService
{
    Task<RolloverPreviewDto> PreviewRolloverAsync(RolloverPreviewQuery query, CancellationToken cancellationToken = default);
    Task<RolloverResultDto> ExecuteRolloverAsync(Guid actorId, ExecuteRolloverCommand command, CancellationToken cancellationToken = default);
}
