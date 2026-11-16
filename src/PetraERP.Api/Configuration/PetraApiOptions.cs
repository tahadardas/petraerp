namespace PetraERP.Api.Configuration;

/// <summary>Host behaviour bound from <c>Petra:Api</c>.</summary>
internal sealed class PetraApiOptions
{
    public const string SectionName = "Petra:Api";

    /// <summary>Publishes /openapi/v1.json. Enabled in Development; production exposure is a deployment decision.</summary>
    public bool ExposeOpenApi { get; set; }

    /// <summary>Redirects HTTP to HTTPS and enables HSTS outside Development.</summary>
    public bool RequireHttps { get; set; } = true;

    public long MaxRequestBodyBytes { get; set; } = 1_048_576;

    internal void Validate()
    {
        if (MaxRequestBodyBytes is < 16_384 or > 104_857_600)
        {
            throw new InvalidOperationException("Petra:Api:MaxRequestBodyBytes must be between 16 KiB and 100 MiB.");
        }
    }
}

/// <summary>Database settings bound from <c>Petra:Database</c>. The connection string comes from <c>ConnectionStrings:PetraERP</c>.</summary>
internal sealed class PetraDatabaseOptions
{
    public const string SectionName = "Petra:Database";

    public int CompatibilityLevel { get; set; } = 160;

    public int CommandTimeoutSeconds { get; set; } = 30;

    internal void Validate()
    {
        if (CompatibilityLevel is not (160 or 170))
        {
            throw new InvalidOperationException("Petra:Database:CompatibilityLevel must be 160 (SQL Server 2022) or 170 (SQL Server 2025).");
        }

        if (CommandTimeoutSeconds is < 5 or > 600)
        {
            throw new InvalidOperationException("Petra:Database:CommandTimeoutSeconds must be between 5 and 600.");
        }
    }
}
