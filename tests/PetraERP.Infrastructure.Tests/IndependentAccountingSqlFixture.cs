using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PetraERP.Application.Workspaces;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Persistence.Actors;
using PetraERP.Infrastructure.Tests.Security;
using PetraERP.Infrastructure.Workspaces;

namespace PetraERP.Infrastructure.Tests;

/// <summary>Central authentication is migrated separately; annual targets start as nonexistent databases.</summary>
internal sealed class IndependentAccountingSqlFixture : IAsyncDisposable
{
    private IndependentAccountingSqlFixture(SqlTestDatabase database, Guid actor)
    {
        Database = database;
        Actor = actor;
        System = new(new DbContextOptionsBuilder<SystemDbContext>().UseSqlServer(database.ConnectionString,
            sql => sql.UseCompatibilityLevel(database.CompatibilityLevel)).Options);
        Authority = new(System);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:PetraERP"] = database.ConnectionString }).Build();
        var options = Options.Create(new FiscalDatabaseOptions
            {
                CompatibilityLevel = database.CompatibilityLevel, DatabaseNamePrefix = database.DatabaseName,
                // This fixture holds ONE resolver for its whole life and tampers with the database between calls, so each call stands for
                // "the next request". Production resolvers are scoped per request and always re-verify on a new request.
                ReadinessCacheSeconds = 0,
            });
        Resolver = new(System, configuration, options);
        Factory = new(Resolver, options, Authority, System);
        Fault = new();
        var faultFactory = new InterceptedAccountingFactory(Factory, database, Authority, Fault);
        Service = new(System, faultFactory, configuration, options, NullLogger<SqlCompanyProvisioningService>.Instance, Authority);
    }

    internal SqlTestDatabase Database { get; }
    internal Guid Actor { get; }
    internal SystemDbContext System { get; }
    internal SqlAccountingActorAuthority Authority { get; }
    internal SqlFiscalDatabaseResolver Resolver { get; }
    internal AccountingDbContextFactory Factory { get; }
    internal SqlCompanyProvisioningService Service { get; }
    internal SqlProvisioningFault Fault { get; }
    internal static ProvisionCompanyInput Input(string code = "ANNUAL") =>
        new(code, "شركة السنة المستقلة", "Independent annual company", "USD", 2026, new(2026, 1, 31), new(2026, 6, 30));

    internal static async Task<IndependentAccountingSqlFixture> CreateAsync()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        try
        {
            using var central = database.Context();
            var seed = await SecuritySqlFixture.BootstrapAsync(central, TestContext.Current.CancellationToken).ConfigureAwait(true);
            return new(database, seed.AdministratorUserId);
        }
        catch
        {
            await database.DisposeAsync().ConfigureAwait(true);
            throw;
        }
    }

    internal Task<ProvisioningResult> ProvisionAsync(string code = "ANNUAL") => ProvisionAsync(Input(code));

    internal async Task<ProvisioningResult> ProvisionAsync(ProvisionCompanyInput input)
    {
        var result = await Service.ProvisionCompanyAsync(input, Actor, TestContext.Current.CancellationToken).ConfigureAwait(true);
        // Mark only server-generated children under this run's random prefix, even after a SQL fault.
        if (await System.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.databases WHERE name={result.DatabaseName}")
            .SingleAsync(TestContext.Current.CancellationToken).ConfigureAwait(true) == 1)
            await Database.RegisterAnnualDatabaseAsync(result.DatabaseName).ConfigureAwait(true);
        else
        {
            var operation = await System.ProvisioningOperations.SingleAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
            throw new InvalidOperationException($"Annual target was not created: {operation.CurrentStep}: {operation.ErrorDetails}");
        }
        return result;
    }

    public async ValueTask DisposeAsync()
    {
        await System.DisposeAsync().ConfigureAwait(true);
        await Database.DisposeAsync().ConfigureAwait(true);
    }

    internal async Task TrackProvisioningTargetAsync(ProvisioningResult result)
    {
        if (!result.DatabaseName.StartsWith(Database.DatabaseName + "_", StringComparison.Ordinal))
            throw new InvalidOperationException("Provisioning target is outside this disposable test installation.");
        if (await System.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.databases WHERE name={result.DatabaseName}")
            .SingleAsync(CancellationToken.None).ConfigureAwait(true) == 0) return;
        using var connection = new SqlConnection(new SqlConnectionStringBuilder(Database.ConnectionString)
            { InitialCatalog = result.DatabaseName }.ConnectionString);
        await connection.OpenAsync(CancellationToken.None).ConfigureAwait(true);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT CONVERT(nvarchar(36),value) FROM sys.extended_properties WHERE class=0 AND name=N'PetraERP.ProvisioningOperationId';";
        var owner = await command.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(true) as string;
        if (!Guid.TryParse(owner, out var operation) || operation != result.OperationId)
            throw new InvalidOperationException("Refusing to adopt an annual target without this test operation's ownership marker.");
        await connection.CloseAsync().ConfigureAwait(true);
        await Database.RegisterAnnualDatabaseAsync(result.DatabaseName).ConfigureAwait(true);
    }
}

/// <summary>Injects an actual SQL THROW at the SQL boundary; no mock storage or authority is used.</summary>
internal sealed class SqlProvisioningFault : DbCommandInterceptor
{
    internal string? Match { get; set; }
    internal int Failures { get; private set; }
    private void Inject(DbCommand command)
    {
        if (Match is not null && command.CommandText.Contains(Match, StringComparison.Ordinal))
        {
            Failures++;
            command.CommandText = "THROW 51099, N'Controlled integration SQL failure', 1;";
        }
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Inject(command);
        return ValueTask.FromResult(result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Inject(command);
        return ValueTask.FromResult(result);
    }
}

internal sealed class InterceptedAccountingFactory(AccountingDbContextFactory inner, SqlTestDatabase database,
    IAccountingActorAuthority authority, SqlProvisioningFault fault) : IAccountingDbContextFactory
{
    public Task<AccountingDbContext> CreateDbContextAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
        inner.CreateDbContextAsync(workspaceId, cancellationToken);
    public Task<AccountingDbContext> CreateDbContextAsync(Guid companyId, Guid fiscalYearId, CancellationToken cancellationToken = default) =>
        inner.CreateDbContextAsync(companyId, fiscalYearId, cancellationToken);
    public AccountingDbContext CreateDbContext(string connectionString) => new(new DbContextOptionsBuilder<AccountingDbContext>()
        .UseSqlServer(connectionString, sql => sql.UseCompatibilityLevel(database.CompatibilityLevel)).AddInterceptors(fault).Options, authority);
}
