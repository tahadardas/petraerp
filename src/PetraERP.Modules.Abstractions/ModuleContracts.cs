namespace PetraERP.Modules.Abstractions;

/// <summary>
/// Stable identity of a module. It is the only handle Petra stores about a module across restarts, so it must never be
/// derived from an assembly path or a transient display name.
/// </summary>
public sealed record ModuleIdentity(string Id, int ContractVersion)
{
    public const int CurrentContractVersion = 1;
}

/// <summary>What the host must know before it decides whether a module may run against this core version.</summary>
public sealed record ModuleDescriptor(
    string Id,
    string DisplayName,
    string Version,
    int ContractVersion,
    string MinCoreVersion,
    string? MaxCoreVersion,
    string Publisher,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<ModuleDependency> Dependencies)
{
    public ModuleIdentity Identity => new(Id, ContractVersion);
}

public sealed record ModuleDependency(string ModuleId, string VersionRange, bool Optional);

/// <summary>
/// The scope a module is running under. Modules receive it; they never derive it from request data. When
/// <see cref="ModuleId"/> is set, the gateways record it on every entry the module posts and only let that module reverse
/// entries it posted itself.
/// </summary>
public sealed record ModuleRequestContext(
    Guid CompanyId,
    Guid FiscalYearId,
    Guid BranchId,
    Guid UserId,
    string LanguageCode,
    IReadOnlySet<string> GrantedPermissions,
    string? ModuleId = null)
{
    public bool Has(string permission) => GrantedPermissions.Contains(permission);
}

/// <summary>
/// The entry point every module assembly exposes. Activation must be idempotent: the host may re-activate a module
/// after a restart without the module assuming it is running for the first time.
/// </summary>
public interface IModule
{
    ModuleDescriptor Describe();

    Task InitializeAsync(ModuleActivationContext context, CancellationToken cancellationToken);

    Task ShutdownAsync(CancellationToken cancellationToken);
}

public sealed record ModuleActivationContext(
    string DataDirectory,
    IReadOnlyDictionary<string, string> Settings,
    ModuleServiceReferences Services,
    Action<string, Exception?> Log);

/// <summary>
/// The only services a module may reach. Every entry is a server-authoritative gateway: a module cannot obtain a raw
/// DbContext, a connection string or the accounting engine's internal types.
/// </summary>
public sealed record ModuleServiceReferences(
    IFinancialPostingGateway Posting,
    IInventoryMovementGateway Inventory,
    IReferenceDataQuery References,
    IAuditTrail Audit);
