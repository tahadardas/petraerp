namespace PetraERP.Application.AlAmeenMigration.Mapping;

/// <summary>Al-Ameen master-data categories whose source identities must be resolved to Petra identities before any comparison or write.</summary>
public enum MigrationEntityKind
{
    Account = 1,
    Party = 2,
    Item = 3,
    Warehouse = 4,
    Currency = 5,
    Unit = 6,
    Branch = 7,
}

/// <summary>A source record that cannot be resolved, or resolves to more than one Petra record. Both are blocking: a skipped row would make reconciliation silently incomplete.</summary>
public sealed record IdentityIssue(MigrationEntityKind Kind, string SourceCode, IdentityIssueKind IssueKind, string? Detail);

public enum IdentityIssueKind
{
    MissingFromTarget = 1,
    AmbiguousInTarget = 2,
    EmptySourceCode = 3,
    ConflictingExistingMapping = 4,
    DuplicateSourceCode = 5,
}

/// <summary>One resolved source-identity to Petra-identity binding.</summary>
public sealed record IdentityBinding(MigrationEntityKind Kind, Guid SourceId, Guid TargetId, string SourceCode);

public sealed record IdentityResolution(
    Guid CompanyId,
    Guid FiscalYearId,
    DateOnly CutoffDate,
    Guid BranchId,
    IReadOnlyList<IdentityBinding> Bindings,
    IReadOnlyDictionary<Guid, string> CurrencyCodes,
    IReadOnlyList<IdentityIssue> Issues)
{
    public bool IsComplete => Issues.Count == 0;

    public IReadOnlyDictionary<Guid, Guid> BindingsOf(MigrationEntityKind kind) =>
        Bindings.Where(b => b.Kind == kind).ToDictionary(b => b.SourceId, b => b.TargetId);

    public IdentityBinding? Find(MigrationEntityKind kind, Guid sourceId) =>
        Bindings.FirstOrDefault(b => b.Kind == kind && b.SourceId == sourceId);

    /// <summary>Resolves or throws. Migration code must never fall back to a default identity.</summary>
    public Guid Require(MigrationEntityKind kind, Guid sourceId, string sourceCode)
    {
        var binding = Find(kind, sourceId)
            ?? throw new InvalidOperationException($"Source identity {sourceCode} of kind {kind} has no resolved Petra binding.");
        return binding.TargetId;
    }
}
