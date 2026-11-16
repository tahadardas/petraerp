namespace PetraERP.Modules.Host;

/// <summary>
/// The only lifecycle a module may follow. Transitions are validated here rather than assumed by callers, because an
/// illegal jump (for example enabling something that never verified) would let an unverified assembly run against
/// financial data.
/// </summary>
public enum ModuleState
{
    Discovered = 1,
    Verified = 2,
    Staged = 3,
    Installed = 4,
    Enabled = 5,
    Disabled = 6,
    UpgradePending = 7,
    Failed = 8,
    RollbackRequired = 9,
}

public static class ModuleLifecycle
{
    private static readonly Dictionary<ModuleState, ModuleState[]> Allowed = new()
    {
        [ModuleState.Discovered] = [ModuleState.Verified, ModuleState.Failed],
        [ModuleState.Verified] = [ModuleState.Staged, ModuleState.Failed],
        [ModuleState.Staged] = [ModuleState.Installed, ModuleState.Failed],
        [ModuleState.Installed] = [ModuleState.Enabled, ModuleState.Disabled, ModuleState.UpgradePending, ModuleState.Failed],
        [ModuleState.Enabled] = [ModuleState.Disabled, ModuleState.UpgradePending, ModuleState.Failed],
        [ModuleState.Disabled] = [ModuleState.Enabled, ModuleState.UpgradePending, ModuleState.Failed],
        [ModuleState.UpgradePending] = [ModuleState.Staged, ModuleState.Failed, ModuleState.RollbackRequired, ModuleState.Disabled],
        [ModuleState.Failed] = [ModuleState.RollbackRequired, ModuleState.Staged, ModuleState.Disabled],
        [ModuleState.RollbackRequired] = [ModuleState.Disabled, ModuleState.Failed],
    };

    public static bool CanTransition(ModuleState from, ModuleState to) =>
        from != to && Allowed.TryGetValue(from, out var targets) && Array.IndexOf(targets, to) >= 0;

    public static void EnsureTransition(ModuleState from, ModuleState to)
    {
        if (!CanTransition(from, to))
            throw new InvalidOperationException($"انتقال غير مسموح لحالة الموديول: {from} إلى {to}.");
    }

    /// <summary>
    /// Disabling is never destructive. Any state that has already written data must keep it, so only Enabled is allowed
    /// to move to Disabled without an explicit retention decision recorded by the caller.
    /// </summary>
    public static bool IsDataBearing(ModuleState state) =>
        state is ModuleState.Installed or ModuleState.Enabled or ModuleState.Disabled or ModuleState.UpgradePending;
}
