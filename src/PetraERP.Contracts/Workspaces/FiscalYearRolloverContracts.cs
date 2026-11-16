using System.ComponentModel.DataAnnotations;

namespace PetraERP.Contracts.Workspaces;

public sealed record RolloverPreviewRequest
{
    [Required]
    public required Guid CompanyId { get; init; }

    [Required]
    public required Guid SourceFiscalYearId { get; init; }

    [Required]
    public required Guid TargetFiscalYearId { get; init; }

    public Guid? RetainedEarningsAccountId { get; init; }
}

public sealed record RolloverAccountLineResponse
{
    public required Guid AccountId { get; init; }
    public required string AccountCode { get; init; }
    public required string AccountNameArabic { get; init; }
    public required byte AccountType { get; init; }
    public required string ClosingDebit { get; init; }
    public required string ClosingCredit { get; init; }
    public required string CurrencyCode { get; init; }
}

public sealed record RolloverPreviewResponse
{
    public required Guid CompanyId { get; init; }
    public required Guid SourceFiscalYearId { get; init; }
    public required string SourceYearCode { get; init; }
    public required Guid TargetFiscalYearId { get; init; }
    public required string TargetYearCode { get; init; }
    public required Guid RetainedEarningsAccountId { get; init; }
    public required string RetainedEarningsAccountCode { get; init; }
    public required string TotalRevenues { get; init; }
    public required string TotalExpenses { get; init; }
    public required string NetIncome { get; init; }
    public required string TotalBalanceSheetDebit { get; init; }
    public required string TotalBalanceSheetCredit { get; init; }
    public required bool IsTrialBalanceBalanced { get; init; }
    public required int OpenItemsCount { get; init; }
    public required string OpenItemsTotalAmount { get; init; }
    public required int StockItemsCount { get; init; }
    public required string StockTotalValue { get; init; }
    public required string SourceFingerprint { get; init; }
    public required IReadOnlyList<RolloverAccountLineResponse> AccountLines { get; init; }
}

public sealed record ExecuteRolloverRequest
{
    [Required]
    public required Guid CompanyId { get; init; }

    [Required]
    public required Guid SourceFiscalYearId { get; init; }

    [Required]
    public required Guid TargetFiscalYearId { get; init; }

    public Guid? RetainedEarningsAccountId { get; init; }

    [Required]
    [MaxLength(128)]
    public required string SourceFingerprint { get; init; }

    [Required]
    [MaxLength(500)]
    public required string Reason { get; init; }

    [Required]
    [MaxLength(128)]
    public required string IdempotencyKey { get; init; }
}

public sealed record RolloverResultResponse
{
    public required Guid RolloverBatchId { get; init; }
    public required Guid CompanyId { get; init; }
    public required Guid SourceFiscalYearId { get; init; }
    public required Guid TargetFiscalYearId { get; init; }
    public required string Status { get; init; }
    public required Guid OpeningBalanceBatchId { get; init; }
    public required Guid OpeningJournalId { get; init; }
    public required int OpenItemsCreated { get; init; }
    public required int StockMovementsCreated { get; init; }
    public required bool TargetDatabaseActivated { get; init; }
    public required string Message { get; init; }
}
