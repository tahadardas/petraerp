namespace PetraERP.Infrastructure.Persistence;

/// <summary>
/// Database settings for fiscal and system database connections.
/// </summary>
public sealed class FiscalDatabaseOptions
{
    public const string SectionName = "Petra:Database";
    public int CompatibilityLevel { get; set; } = 160;
    public int CommandTimeoutSeconds { get; set; } = 30;
    public string DatabaseNamePrefix { get; set; } = "PetraERP";

    /// <summary>
    /// How long a successful workspace readiness check (schema, migrations, branch registration of the annual database) is trusted for
    /// the same registry facts. Every request resolves its workspace, and the full check used to run each time; drift is still detected,
    /// at most this many seconds late. 0 re-verifies on every request. Failures are never cached.
    /// </summary>
    public int ReadinessCacheSeconds { get; set; } = 5;
}
