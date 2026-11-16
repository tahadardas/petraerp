using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PetraERP.Application.Security;
using PetraERP.Application.Workspaces;
using PetraERP.Contracts.Authentication;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Tests;
using ClientKind = PetraERP.Contracts.Authentication.ClientKind;

namespace PetraERP.Api.Tests;

/// <summary>An isolated SQL central database, migrated and bootstrapped through the real services and hosted by the
/// real API. <see cref="CreateWithWorkspaceAsync"/> additionally provisions an independent annual accounting database;
/// financial HTTP endpoints resolve it by selecting the provisioned workspace with X-Petra-Workspace.</summary>
internal sealed class ApiSqlHost : IAsyncDisposable
{
    internal const string AdminUser = "admin";
    internal const string AdminPassword = "Petra-Secure-2026-آمنة";
    internal const string WorkspaceHeader = "X-Petra-Workspace";

    private readonly string? annualConnectionString;

    private ApiSqlHost(SqlTestDatabase database, PetraApiFactory factory, BootstrapResult bootstrap,
        Guid workspaceId, Guid companyId, Guid fiscalYearId, Guid branchId, string? annualConnectionString)
    {
        Database = database;
        Factory = factory;
        Bootstrap = bootstrap;
        WorkspaceId = workspaceId;
        CompanyId = companyId;
        FiscalYearId = fiscalYearId;
        BranchId = branchId;
        UserId = bootstrap.AdministratorUserId;
        this.annualConnectionString = annualConnectionString;
    }

    internal SqlTestDatabase Database { get; }
    internal PetraApiFactory Factory { get; }
    internal BootstrapResult Bootstrap { get; }

    /// <summary>The provisioned, authorized annual workspace (Guid.Empty unless created with a workspace).</summary>
    internal Guid WorkspaceId { get; }
    internal Guid CompanyId { get; }
    internal Guid FiscalYearId { get; }
    internal Guid BranchId { get; }
    internal Guid UserId { get; }

    /// <summary>Bootstrap only: a central database and first administrator, no independent annual workspace.</summary>
    internal static async Task<ApiSqlHost> CreateAsync(int authenticationPermitsPerMinute = 10_000, Action<IServiceCollection>? overrides = null)
    {
        var (database, factory, bootstrap) = await BootstrapAsync(authenticationPermitsPerMinute, overrides).ConfigureAwait(false);
        return new ApiSqlHost(database, factory, bootstrap, Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty, null);
    }

    /// <summary>Bootstrap plus an independently provisioned annual accounting workspace, selected over HTTP via
    /// X-Petra-Workspace. The annual database is named under the disposable test database for deterministic cleanup.</summary>
    internal static async Task<ApiSqlHost> CreateWithWorkspaceAsync(int authenticationPermitsPerMinute = 10_000, Action<IServiceCollection>? overrides = null)
    {
        var (database, factory, bootstrap) = await BootstrapAsync(authenticationPermitsPerMinute, overrides).ConfigureAwait(false);
        ProvisioningResult result;
        using (var scope = factory.Services.CreateScope())
        {
            var provisioning = scope.ServiceProvider.GetRequiredService<ICompanyProvisioningService>();
            result = await provisioning.ProvisionCompanyAsync(
                new ProvisionCompanyInput("WS", "شركة مساحة العمل", "Workspace company", "JOD", 2026,
                    new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
                bootstrap.AdministratorUserId, TestContext.Current.CancellationToken).ConfigureAwait(false);
        }
        if (!result.Succeeded) throw new InvalidOperationException($"Workspace provisioning failed: {result.Error}");
        await database.RegisterAnnualDatabaseAsync(result.DatabaseName).ConfigureAwait(false);

        Guid branchId;
        using (var context = database.Context())
            // Public Database API only: ApiSqlHost is shared into projects without internal access to the EF model.
            branchId = await context.Database.SqlQuery<Guid>(
                $"SELECT [Id] AS [Value] FROM [system].[Branches] WHERE [CompanyId] = {result.CompanyId}")
                .SingleAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);

        var annual = new SqlConnectionStringBuilder(database.ConnectionString) { InitialCatalog = result.DatabaseName }.ConnectionString;
        return new ApiSqlHost(database, factory, bootstrap, result.WorkspaceId, result.CompanyId, result.FiscalYearId, branchId, annual);
    }

    /// <summary>Bootstrap plus an independently provisioned annual accounting workspace FOR THE BOOTSTRAP company, so the
    /// administrator holds exactly one company scope that ALSO owns a financial workspace — the real single-company
    /// journey a desktop operator follows (administer users and keep the books for the same company). The shell
    /// auto-selects the lone workspace, so financial pages (accounts, journals) establish context without a picker.</summary>
    internal static async Task<ApiSqlHost> CreateBootstrapWorkspaceAsync(int authenticationPermitsPerMinute = 10_000)
    {
        var (database, factory, bootstrap) = await BootstrapAsync(authenticationPermitsPerMinute).ConfigureAwait(false);

        Guid companyId, branchId;
        using (var context = database.Context())
        {
            // Public Database API only: ApiSqlHost is shared into projects without internal access to the EF model.
            companyId = await context.Database.SqlQuery<Guid>(
                $"SELECT [Id] AS [Value] FROM [system].[Companies] WHERE [Code] = {"PETRA"}")
                .SingleAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
            branchId = await context.Database.SqlQuery<Guid>(
                $"SELECT [Id] AS [Value] FROM [system].[Branches] WHERE [CompanyId] = {companyId}")
                .SingleAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
        }

        ProvisioningResult result;
        using (var scope = factory.Services.CreateScope())
        {
            var provisioning = scope.ServiceProvider.GetRequiredService<ICompanyProvisioningService>();
            result = await provisioning.ProvisionFiscalYearDatabaseAsync(
                new ProvisionFiscalYearInput(companyId, 2026, "FY2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), false, false, false),
                bootstrap.AdministratorUserId, TestContext.Current.CancellationToken).ConfigureAwait(false);
        }
        if (!result.Succeeded) throw new InvalidOperationException($"Bootstrap workspace provisioning failed: {result.Error}");
        await database.RegisterAnnualDatabaseAsync(result.DatabaseName).ConfigureAwait(false);

        var annual = new SqlConnectionStringBuilder(database.ConnectionString) { InitialCatalog = result.DatabaseName }.ConnectionString;
        return new ApiSqlHost(database, factory, bootstrap, result.WorkspaceId, result.CompanyId, result.FiscalYearId, branchId, annual);
    }

    private static async Task<(SqlTestDatabase Database, PetraApiFactory Factory, BootstrapResult Bootstrap)> BootstrapAsync(int authenticationPermitsPerMinute, Action<IServiceCollection>? overrides = null)
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(false);
        var factory = new PetraApiFactory(database.ConnectionString, database.CompatibilityLevel, authenticationPermitsPerMinute, overrides);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PetraDbContext>();
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
        var bootstrap = await scope.ServiceProvider.GetRequiredService<ISecurityBootstrapper>().BootstrapAsync(
            new BootstrapAdministratorCommand(AdminUser, "مدير النظام", "Administrator", AdminPassword,
                new BootstrapOrganization("PETRA", "شركة البتراء للتجارة", "Petra Trading", "JOD", "دينار أردني", "HQ",
                    "المقر الرئيسي", "Head office")),
            TestContext.Current.CancellationToken).ConfigureAwait(false);
        return (database, factory, bootstrap);
    }

    /// <summary>Central/legacy database context (users, companies, branches, preferences).</summary>
    internal PetraDbContext CreateContext() => Database.Context();

    /// <summary>A context bound to the independent annual database for raw-SQL historical seeding and reads
    /// (the EF model maps the same ledger/inventory tables). Application authorization is exercised over HTTP.</summary>
    internal PetraDbContext AnnualContext() => new(new DbContextOptionsBuilder<PetraDbContext>()
        .UseSqlServer(annualConnectionString ?? throw new InvalidOperationException("No workspace was provisioned for this host."),
            sql => sql.UseCompatibilityLevel(Database.CompatibilityLevel)).Options);

    /// <summary>Provisions a SECOND independent company/year workspace on the same central database, so a test can prove
    /// that a search bound to one annual workspace never exposes another company's or year's financial rows. The admin
    /// receives company/branch grants during provisioning, so the same user can be pointed at either workspace.</summary>
    internal async Task<ProvisionedWorkspace> ProvisionAdditionalWorkspaceAsync(string code, int year, DateOnly start, DateOnly end)
    {
        ProvisioningResult result;
        using (var scope = Factory.Services.CreateScope())
        {
            var provisioning = scope.ServiceProvider.GetRequiredService<ICompanyProvisioningService>();
            result = await provisioning.ProvisionCompanyAsync(
                new ProvisionCompanyInput(code, $"شركة {code}", $"Company {code}", "JOD", year, start, end),
                UserId, TestContext.Current.CancellationToken).ConfigureAwait(false);
        }
        if (!result.Succeeded) throw new InvalidOperationException($"Additional workspace provisioning failed: {result.Error}");
        await Database.RegisterAnnualDatabaseAsync(result.DatabaseName).ConfigureAwait(false);
        Guid branchId;
        using (var context = Database.Context())
            branchId = await context.Database.SqlQuery<Guid>(
                $"SELECT [Id] AS [Value] FROM [system].[Branches] WHERE [CompanyId] = {result.CompanyId}")
                .SingleAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
        var annual = new SqlConnectionStringBuilder(Database.ConnectionString) { InitialCatalog = result.DatabaseName }.ConnectionString;
        return new ProvisionedWorkspace(result.WorkspaceId, result.CompanyId, result.FiscalYearId, branchId, annual, Database.CompatibilityLevel);
    }

    /// <summary>Provisions an ADDITIONAL fiscal year for THIS host's primary company (independent DB per year, no rollover),
    /// so a test can prove a search bound to one year never exposes another year of the same company.</summary>
    internal async Task<ProvisionedWorkspace> ProvisionAdditionalYearAsync(int year, DateOnly start, DateOnly end)
    {
        ProvisioningResult result;
        using (var scope = Factory.Services.CreateScope())
        {
            var provisioning = scope.ServiceProvider.GetRequiredService<ICompanyProvisioningService>();
            result = await provisioning.ProvisionFiscalYearDatabaseAsync(
                new ProvisionFiscalYearInput(CompanyId, year, $"FY{year}", start, end, false, false, false),
                UserId, TestContext.Current.CancellationToken).ConfigureAwait(false);
        }
        if (!result.Succeeded) throw new InvalidOperationException($"Additional year provisioning failed: {result.Error}");
        await Database.RegisterAnnualDatabaseAsync(result.DatabaseName).ConfigureAwait(false);
        var annual = new SqlConnectionStringBuilder(Database.ConnectionString) { InitialCatalog = result.DatabaseName }.ConnectionString;
        return new ProvisionedWorkspace(result.WorkspaceId, result.CompanyId, result.FiscalYearId, BranchId, annual, Database.CompatibilityLevel);
    }

    /// <summary>A secondary provisioned workspace and a factory for a context bound to its annual database.</summary>
    internal sealed record ProvisionedWorkspace(Guid WorkspaceId, Guid CompanyId, Guid FiscalYearId, Guid BranchId, string AnnualConnectionString, int CompatibilityLevel)
    {
        internal PetraDbContext AnnualContext() => new(new DbContextOptionsBuilder<PetraDbContext>()
            .UseSqlServer(AnnualConnectionString, sql => sql.UseCompatibilityLevel(CompatibilityLevel)).Options);
    }

    internal static async Task<TokenResponse> LoginAsync(HttpClient client, string userName, string password)
    {
        using var response = await client.PostAsJsonAsync(new Uri("/api/v1/auth/login", UriKind.Relative),
            new LoginRequest { UserName = userName, Password = password, ClientKind = ClientKind.Desktop, DeviceName = "TEST-PC" },
            ApiJson.Options, TestContext.Current.CancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TokenResponse>(ApiJson.Options, TestContext.Current.CancellationToken).ConfigureAwait(false))!;
    }

    internal static HttpRequestMessage Authorized(HttpMethod method, string path, string accessToken, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative)) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    /// <summary>An authorized request that also selects the provisioned annual workspace via X-Petra-Workspace.</summary>
    internal HttpRequestMessage WorkspaceRequest(HttpMethod method, string path, string accessToken, HttpContent? content = null)
    {
        if (WorkspaceId == Guid.Empty) throw new InvalidOperationException("No workspace was provisioned for this host.");
        var request = Authorized(method, path, accessToken, content);
        request.Headers.Add(WorkspaceHeader, WorkspaceId.ToString("D"));
        return request;
    }

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync().ConfigureAwait(false);
        await Database.DisposeAsync().ConfigureAwait(false);
    }
}
