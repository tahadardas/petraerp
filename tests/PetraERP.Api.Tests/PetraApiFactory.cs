using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace PetraERP.Api.Tests;

/// <summary>
/// In-process API host for tests. Each factory gets a fresh random signing key; the connection string
/// points at an isolated SQL test database, or at an unreachable placeholder for tests that never touch SQL.
/// </summary>
internal sealed class PetraApiFactory : WebApplicationFactory<Program>
{
    internal const string UnreachableConnection =
        "Server=tcp:127.0.0.1,1;Database=PetraERP_NoDatabase;Integrated Security=true;Encrypt=true;TrustServerCertificate=true;Connect Timeout=1";

    private readonly string connectionString;
    private readonly Action<IServiceCollection>? overrides;
    private readonly int compatibilityLevel;
    private readonly int authenticationPermitsPerMinute;

    internal PetraApiFactory(string? connectionString = null, int compatibilityLevel = 170, int authenticationPermitsPerMinute = 10_000, Action<IServiceCollection>? overrides = null)
    {
        this.connectionString = connectionString ?? UnreachableConnection;
        this.overrides = overrides;
        this.compatibilityLevel = compatibilityLevel;
        this.authenticationPermitsPerMinute = authenticationPermitsPerMinute;
        SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    }

    internal string SigningKey { get; }

    internal const string Issuer = "PetraERP.Tests";
    internal const string Audience = "PetraERP.Tests.Clients";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseEnvironment("Testing");
        if (overrides is not null) builder.ConfigureServices(overrides);
        builder.UseSetting("ConnectionStrings:PetraERP", connectionString);
        builder.UseSetting("Petra:Database:CompatibilityLevel", compatibilityLevel.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var catalog = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString).InitialCatalog;
        if (!string.IsNullOrWhiteSpace(catalog) && System.Text.RegularExpressions.Regex.IsMatch(catalog, "^[A-Za-z][A-Za-z0-9_]{0,79}$"))
            builder.UseSetting("Petra:Database:DatabaseNamePrefix", catalog);
        builder.UseSetting("Petra:Api:RequireHttps", "false");
        builder.UseSetting("Petra:Api:ExposeOpenApi", "true");
        builder.UseSetting("Petra:Security:Tokens:SigningKey", SigningKey);
        builder.UseSetting("Petra:Security:Tokens:Issuer", Issuer);
        builder.UseSetting("Petra:Security:Tokens:Audience", Audience);
        builder.UseSetting("Petra:Security:RateLimits:AuthenticationPermitsPerMinute",
            authenticationPermitsPerMinute.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.UseSetting("Petra:Security:RateLimits:ApiPermitsPerMinute", "100000");
        builder.UseSetting("Logging:LogLevel:Default", "Warning");
    }
}
