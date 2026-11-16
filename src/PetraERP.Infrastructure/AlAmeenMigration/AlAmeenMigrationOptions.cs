namespace PetraERP.Infrastructure.AlAmeenMigration;

/// <summary>
/// Operator switches for the Al-Ameen migration engine. Both default to off: reading a legacy database and writing into a
/// company are deliberate acts that an administrator enables for a controlled migration window (configuration section
/// "AlAmeenMigration"), and a production host without the section stays blocked.
/// </summary>
public sealed class AlAmeenMigrationOptions
{
    public const string SectionName = "AlAmeenMigration";

    /// <summary>Allows preview and reconciliation to read a legacy Al-Ameen database.</summary>
    public bool ReadsEnabled { get; set; }

    /// <summary>Allows execute and activate to write opening data into the target company.</summary>
    public bool WritesEnabled { get; set; }

    /// <summary>ISO code the legacy base currency (the rate-1 currency) is mapped to when the source stores a symbol or local name.</summary>
    public string? BaseCurrencyCode { get; set; }
}
