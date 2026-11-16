using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Tests;

internal sealed class SqlTestDatabase : IAsyncDisposable
{
    private const string Prefix = "PetraERP_SchemaTest_";
    private const string PreviewPrefix = "PetraERP_LocalPreview_";
    private readonly Guid runId;
    private readonly string name;
    private readonly string masterConnection;
    private readonly string testConnection;
    private readonly int compatibility;
    private readonly List<string> children = [];

    private SqlTestDatabase(Guid runId, SqlConnectionStringBuilder connection, int major, bool preview = false)
    {
        this.runId = runId;
        name = preview ? PreviewPrefix + runId.ToString("N")[..16] : Prefix + runId.ToString("N");
        masterConnection = connection.ConnectionString;
        connection.InitialCatalog = name;
        testConnection = connection.ConnectionString;
        compatibility = major == 17 ? 170 : 160;
    }

    internal static Task<SqlTestDatabase> CreateAsync() => CreateCoreAsync(null);

    // A new disposable database with production-equivalent preview naming and a separate test ownership marker.
    // CREATE DATABASE fails on collision; this helper never adopts an existing preview database.
    internal static Task<SqlTestDatabase> CreatePreviewAsync(Guid installationId) => CreateCoreAsync(installationId);

    private static async Task<SqlTestDatabase> CreateCoreAsync(Guid? previewInstallationId)
    {
        var server = Environment.GetEnvironmentVariable("PETRAERP_SQL_TEST_SERVER");
        if (string.IsNullOrWhiteSpace(server))
        {
            throw new InvalidOperationException("Explicit SQL tests require PETRAERP_SQL_TEST_SERVER. No default business database is used.");
        }

        var expected = int.Parse(Environment.GetEnvironmentVariable("PETRAERP_SQL_TEST_EXPECTED_MAJOR") ?? "16", CultureInfo.InvariantCulture);
        if (expected is not (16 or 17))
        {
            throw new InvalidOperationException("SQL tests support approved SQL Server 2022 (16) and explicit SQL Server 2025 (17) certification.");
        }

        var runId = previewInstallationId ?? Guid.NewGuid();
        if (runId == Guid.Empty) throw new ArgumentException("Installation ID must not be empty.", nameof(previewInstallationId));
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = server,
            InitialCatalog = "master",
            IntegratedSecurity = true,
            Encrypt = SqlConnectionEncryptOption.Mandatory,
            TrustServerCertificate = Environment.GetEnvironmentVariable("PETRAERP_SQL_TEST_TRUST_CERTIFICATE") == "1",
            ApplicationName = Prefix + runId.ToString("N"),
            ConnectTimeout = 10,
        };
        using var master = new SqlConnection(builder.ConnectionString);
        await master.OpenAsync().ConfigureAwait(false);
        using var version = master.CreateCommand();
        version.CommandText = "SELECT CAST(SERVERPROPERTY('ProductMajorVersion') AS int);";
        var actual = (int)(await version.ExecuteScalarAsync().ConfigureAwait(false))!;
        if (actual != expected)
        {
            throw new InvalidOperationException($"Expected SQL Server major {expected}, found {actual}; no test database was created.");
        }

        var database = new SqlTestDatabase(runId, builder, actual, previewInstallationId is not null);
        using var create = master.CreateCommand();
        create.CommandText = $"CREATE DATABASE [{database.name}];";
        await create.ExecuteNonQueryAsync().ConfigureAwait(false);
        using var test = new SqlConnection(database.testConnection);
        await test.OpenAsync().ConfigureAwait(false);
        using var marker = test.CreateCommand();
        marker.CommandText = "EXEC sys.sp_addextendedproperty @name=N'PetraERP.TestRunId', @value=@RunId;";
        marker.Parameters.AddWithValue("@RunId", runId.ToString("N"));
        await marker.ExecuteNonQueryAsync().ConfigureAwait(false);
        if (previewInstallationId is not null)
        {
            marker.CommandText = "EXEC sys.sp_addextendedproperty @name=N'PetraERP.LocalPreview.Owner', @value=@RunId;";
            await marker.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        return database;
    }

    /// <summary>Connection string of the isolated test database (used by API host tests).</summary>
    internal string ConnectionString => testConnection;

    /// <summary>Database name of the isolated test database.</summary>
    internal string DatabaseName => name;

    /// <summary>Master connection string for instance-level administrative tasks.</summary>
    internal string MasterConnectionString => masterConnection;

    /// <summary>EF compatibility level matching the verified engine major version.</summary>
    internal int CompatibilityLevel => compatibility;

    internal PetraDbContext Context() => new(new DbContextOptionsBuilder<PetraDbContext>()
        .UseSqlServer(testConnection, sql => sql.UseCompatibilityLevel(compatibility)).Options);

    public async ValueTask DisposeAsync()
    {
        if (!string.Equals(name, Prefix + runId.ToString("N"), StringComparison.Ordinal)
            && !string.Equals(name, PreviewPrefix + runId.ToString("N")[..16], StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Refusing cleanup of an unverified test database name.");
        }

        foreach (var child in children)
            await DropOwnedAsync(child).ConfigureAwait(false);
        await DropOwnedAsync(name).ConfigureAwait(false);
    }

    internal async Task RegisterAnnualDatabaseAsync(string annualName)
    {
        if (!annualName.StartsWith(name + "_", StringComparison.Ordinal)
            || annualName.Length > 128 || annualName.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
            throw new InvalidOperationException("Annual test database must belong to this disposable run.");
        var connection = new SqlConnectionStringBuilder(testConnection) { InitialCatalog = annualName };
        using var annual = new SqlConnection(connection.ConnectionString);
        await annual.OpenAsync().ConfigureAwait(false);
        using var marker = annual.CreateCommand();
        marker.CommandText = "EXEC sys.sp_addextendedproperty @name=N'PetraERP.TestRunId', @value=@RunId;";
        marker.Parameters.AddWithValue("@RunId", runId.ToString("N"));
        await marker.ExecuteNonQueryAsync().ConfigureAwait(false);
        children.Add(annualName);
    }

    private async Task DropOwnedAsync(string target)
    {
        var connection = new SqlConnectionStringBuilder(testConnection) { InitialCatalog = target };
        using (var test = new SqlConnection(connection.ConnectionString))
        {
            await test.OpenAsync().ConfigureAwait(false);
            using var marker = test.CreateCommand();
            marker.CommandText = "SELECT CAST(value AS nvarchar(32)) FROM sys.extended_properties WHERE class=0 AND name=N'PetraERP.TestRunId';";
            if (await marker.ExecuteScalarAsync().ConfigureAwait(false) is not string value || value != runId.ToString("N"))
            {
                throw new InvalidOperationException("Refusing cleanup: test database ownership marker does not match.");
            }
        }

        using var pool = new SqlConnection(connection.ConnectionString);
        SqlConnection.ClearPool(pool);
        using var master = new SqlConnection(masterConnection);
        await master.OpenAsync().ConfigureAwait(false);
        using var drop = master.CreateCommand();
        drop.CommandText = $"DROP DATABASE [{target}];";
        await drop.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}
