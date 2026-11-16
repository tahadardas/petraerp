using System.ComponentModel.DataAnnotations;

namespace PetraERP.Application.AlAmeenMigration;

public enum MigrationMode
{
    FastCutover = 1,
    FullHistorical = 2
}

public enum MigrationState
{
    Created = 1,
    SourceVerified = 2,
    Extracted = 3,
    Mapped = 4,
    Validated = 5,
    ImportedToIsolatedTarget = 6,
    Reconciled = 7,
    AwaitingApproval = 8,
    Approved = 9,
    Activated = 10,
    FailedRecoverable = 11
}

public static class MigrationValidationPatterns
{
    public const string MoneyPattern = "^-?(0|[1-9][0-9]{0,14})(\\.[0-9]{1,4})?$";
}

public sealed record MigrationPreviewRequest(
    string SourceConnectionString,
    Guid TargetCompanyId,
    string TargetFiscalYearCode,
    DateOnly TargetFiscalYearStart,
    DateOnly TargetFiscalYearEnd,
    string TargetBaseCurrencyCode,
    MigrationMode Mode = MigrationMode.FastCutover,
    DateOnly? CutoffDate = null);

public sealed record MigrationPreviewResponse(
    Guid MigrationRunId,
    MigrationState State,
    int AccountsCount,
    int PartiesCount,
    int ItemsCount,
    int WarehousesCount,
    int CurrenciesCount,
    [property: RegularExpression(MigrationValidationPatterns.MoneyPattern)] string OpeningBalanceDebit,
    [property: RegularExpression(MigrationValidationPatterns.MoneyPattern)] string OpeningBalanceCredit,
    int CustomerOpenItemsCount,
    [property: RegularExpression(MigrationValidationPatterns.MoneyPattern)] string CustomerOpenItemsTotal,
    int SupplierOpenItemsCount,
    [property: RegularExpression(MigrationValidationPatterns.MoneyPattern)] string SupplierOpenItemsTotal,
    int InventoryItemsCount,
    [property: RegularExpression(MigrationValidationPatterns.MoneyPattern)] string InventoryValuationTotal,
    IReadOnlyList<string> ValidationIssues);

public sealed record MigrationExecuteRequest(
    Guid MigrationRunId,
    string AdministratorPassword,
    string? Notes = null);

public sealed record MigrationExecuteResponse(
    Guid MigrationRunId,
    MigrationState State,
    string TargetDatabaseName,
    Guid TargetFiscalYearId,
    DateTimeOffset ExecutedAtUtc,
    bool Reconciled,
    bool Success,
    string Message);

public sealed record MigrationReconciliationResponse(
    Guid MigrationRunId,
    DateTimeOffset ReconciledAtUtc,
    [property: RegularExpression(MigrationValidationPatterns.MoneyPattern)] string TrialBalanceDifference,
    [property: RegularExpression(MigrationValidationPatterns.MoneyPattern)] string ArDifference,
    [property: RegularExpression(MigrationValidationPatterns.MoneyPattern)] string ApDifference,
    [property: RegularExpression(MigrationValidationPatterns.MoneyPattern)] string InventoryDifference,
    bool Passed,
    IReadOnlyList<MigrationCheckDto> Checks);

public sealed record MigrationCheckDto(
    string CheckName,
    [property: RegularExpression(MigrationValidationPatterns.MoneyPattern)] string SourceValue,
    [property: RegularExpression(MigrationValidationPatterns.MoneyPattern)] string TargetValue,
    [property: RegularExpression(MigrationValidationPatterns.MoneyPattern)] string Difference,
    bool Passed,
    string? Details);

public sealed record MigrationActivationRequest(
    Guid MigrationRunId,
    string AdministratorPassword);

public sealed record MigrationActivationResponse(
    Guid MigrationRunId,
    Guid TargetCompanyId,
    Guid TargetFiscalYearId,
    DateTimeOffset ActivatedAtUtc,
    bool Success,
    string Message);
