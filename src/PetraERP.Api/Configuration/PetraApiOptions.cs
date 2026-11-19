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

    /// <summary>
    /// Extra browser origins allowed to call the API (exact scheme://host[:port], no wildcards), added to <see cref="BuiltInUiOrigins"/>.
    /// The React UI runs in a Tauri webview or the Vite dev server, which is a different origin from the API.
    /// </summary>
    public string[] UiOrigins { get; set; } = [];

    /// <summary>The desktop shell (Tauri on Windows serves from http(s)://tauri.localhost) and the local Vite dev server only.</summary>
    internal static readonly string[] BuiltInUiOrigins =
    [
        "http://tauri.localhost", "https://tauri.localhost", "tauri://localhost",
        "http://localhost:1420", "http://127.0.0.1:1420",
    ];

    internal void Validate()
    {
        if (MaxRequestBodyBytes is < 16_384 or > 104_857_600)
        {
            throw new InvalidOperationException("Petra:Api:MaxRequestBodyBytes must be between 16 KiB and 100 MiB.");
        }

        if (UiOrigins.Any(origin => !Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.PathAndQuery != "/" || origin.Contains('*', StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Petra:Api:UiOrigins must be exact origins such as https://erp.example.com (no paths, no wildcards).");
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
