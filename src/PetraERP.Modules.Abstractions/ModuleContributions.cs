namespace PetraERP.Modules.Abstractions;

/// <summary>
/// UI and API contributions are declared, never injected. The host renders these descriptions and re-checks the
/// permission of the current user before any contribution becomes reachable, so a module cannot add a route, a menu item
/// or a report that the operator is not allowed to see.
/// </summary>
public interface IModuleMenuContributor
{
    string MenuPath { get; }

    IReadOnlyList<ModuleMenuItem> GetMenuItems(ModuleRequestContext context);
}

public sealed record ModuleMenuItem(
    string Id,
    string TitleArabic,
    string TitleEnglish,
    string? CommandKey,
    string RequiredPermission,
    int Order,
    IReadOnlyList<ModuleMenuItem> Children);

/// <summary>A page a module wants to show inside the MDI workspace. The host resolves the content itself.</summary>
public sealed record ModulePageContribution(
    string RouteKey,
    string TitleArabic,
    string TitleEnglish,
    string RequiredPermission,
    int PreferredWidth,
    int PreferredHeight);

/// <summary>
/// Permissions a module declares at install time. The host stores them as proposals: a permission only becomes grantable
/// after an operator approves it, so a package cannot widen its own authority silently.
/// </summary>
public interface IModulePermissionContributor
{
    IReadOnlyList<ModulePermissionDefinition> GetPermissions();
}

public sealed record ModulePermissionDefinition(
    string Code,
    string DisplayNameArabic,
    string DisplayNameEnglish,
    string Category,
    bool IsFinancialWrite);

/// <summary>Feature flags let a module expose capabilities that the operator can switch per company and year.</summary>
public interface IModuleFeatureContributor
{
    IReadOnlyList<ModuleFeature> GetFeatures();
}

public sealed record ModuleFeature(string Key, string DisplayNameArabic, string DisplayNameEnglish, bool DefaultEnabled);

/// <summary>
/// How the host runs a module action (a menu item's <see cref="ModuleMenuItem.CommandKey"/>). The host checks the menu
/// item's permission and stamps the module identity into the context before calling, so a module cannot widen either.
/// </summary>
public interface IModuleCommandHandler
{
    Task<ModuleCommandResult> ExecuteAsync(
        ModuleRequestContext context,
        string commandKey,
        IReadOnlyDictionary<string, string> arguments,
        CancellationToken cancellationToken);
}

public sealed record ModuleCommandResult(bool Success, string Message, IReadOnlyDictionary<string, string> Data);
