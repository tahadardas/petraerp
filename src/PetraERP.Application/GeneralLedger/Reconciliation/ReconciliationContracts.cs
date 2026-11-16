namespace PetraERP.Application.GeneralLedger.Reconciliation;

public enum ReconciliationSeverity
{
    Pass = 1,
    Informational = 2,
    Warning = 3,
    Blocking = 4,
}

public sealed record ReconciliationFindingDto(
    Guid ReconciliationId,
    Guid CompanyId,
    Guid FiscalYearId,
    Guid? BranchId,
    string CheckCode,
    Guid? AccountId,
    Guid? PartyId,
    Guid? ItemId,
    Guid? WarehouseId,
    Guid? DocumentId,
    string? CurrencyCode,
    decimal? SourceAmount,
    decimal? CalculatedAmount,
    decimal? Difference,
    string Severity, // PASS, INFORMATIONAL, WARNING, BLOCKING
    string Explanation,
    string? SuggestedAction,
    string? EvidenceReference);

public sealed record ReconciliationReportDto(
    Guid CompanyId,
    Guid FiscalYearId,
    DateTimeOffset GeneratedAtUtc,
    int TotalChecks,
    int PassedChecks,
    int WarningChecks,
    int BlockingChecks,
    bool HasBlockingDifferences,
    IReadOnlyList<ReconciliationFindingDto> Findings);

public interface IAccountingReconciliationService
{
    Task<ReconciliationReportDto> RunAllReconciliationsAsync(Guid companyId, Guid fiscalYearId, Guid? branchId, CancellationToken cancellationToken = default);
    Task<string> ExportReportToJsonAsync(Guid companyId, Guid fiscalYearId, Guid? branchId, CancellationToken cancellationToken = default);
    Task<string> ExportReportToCsvAsync(Guid companyId, Guid fiscalYearId, Guid? branchId, CancellationToken cancellationToken = default);
}
