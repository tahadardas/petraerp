namespace PetraERP.Modules.Abstractions;

/// <summary>
/// Reserved identities for the optional business modules that are planned but NOT implemented yet (fixed assets, employees, payroll).
/// Nothing here runs: the constants only keep the ids, the dependency direction and the posting reference convention stable, so a
/// module added later can be installed, disabled or upgraded through the existing module manager without touching core tables.
/// <para>
/// Rules a future module follows (the same ones every module already obeys): all financial effects go through
/// <see cref="IFinancialPostingGateway"/> with an idempotency key; stock effects through <see cref="IInventoryMovementGateway"/>;
/// reference data is read through <see cref="IReferenceDataQuery"/>; its own data lives in its own store so disabling it keeps every
/// posted journal and its audit trail intact (posted records are immutable and reversed only by their linked reversal).
/// </para>
/// </summary>
public static class WellKnownModules
{
    public const string FixedAssets = "petra.fixed-assets";
    public const string Employees = "petra.employees";
    public const string Payroll = "petra.payroll";

    /// <summary>Payroll needs employee records; fixed assets and employees are independent of each other.</summary>
    public static IReadOnlyList<ModuleDependency> RequiredDependenciesOf(string moduleId) => moduleId switch
    {
        Payroll => [new ModuleDependency(Employees, ">=1.0.0", Optional: false)],
        _ => [],
    };

    /// <summary>
    /// The <see cref="PostingProposal.ExternalReference"/> of an entry a module posts: "module-id:document-id". It is how a module
    /// finds its own entries again and how reports tell a module's entries from core documents.
    /// </summary>
    public static string PostingReference(string moduleId, string documentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        return $"{moduleId}:{documentId}";
    }

    /// <summary>Permission names a module declares in its descriptor: "Module.{id}.{action}", so they never collide with core permissions.</summary>
    public static string Permission(string moduleId, string action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        return $"Module.{moduleId}.{action}";
    }
}
