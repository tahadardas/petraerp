namespace PetraERP.Contracts.Ledger;

public sealed record ReconciliationFindingResponse
{
    public required Guid ReconciliationId { get; init; }
    public required Guid CompanyId { get; init; }
    public required Guid FiscalYearId { get; init; }
    public Guid? BranchId { get; init; }
    public required string CheckCode { get; init; }
    public Guid? AccountId { get; init; }
    public Guid? PartyId { get; init; }
    public Guid? ItemId { get; init; }
    public Guid? WarehouseId { get; init; }
    public Guid? DocumentId { get; init; }
    public string? CurrencyCode { get; init; }
    public string? SourceAmount { get; init; }
    public string? CalculatedAmount { get; init; }
    public string? Difference { get; init; }
    public required string Severity { get; init; }
    public required string Explanation { get; init; }
    public string? SuggestedAction { get; init; }
    public string? EvidenceReference { get; init; }
}

public sealed record ReconciliationReportResponse
{
    public required Guid CompanyId { get; init; }
    public required Guid FiscalYearId { get; init; }
    public required DateTimeOffset GeneratedAtUtc { get; init; }
    public required int TotalChecks { get; init; }
    public required int PassedChecks { get; init; }
    public required int WarningChecks { get; init; }
    public required int BlockingChecks { get; init; }
    public required bool HasBlockingDifferences { get; init; }
    public required IReadOnlyList<ReconciliationFindingResponse> Findings { get; init; }
}
