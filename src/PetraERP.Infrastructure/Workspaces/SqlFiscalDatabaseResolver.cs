using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using PetraERP.Application.Common;
using PetraERP.Application.Workspaces;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Persistence.Registry;

namespace PetraERP.Infrastructure.Workspaces;

public sealed class SqlFiscalDatabaseResolver : IFiscalDatabaseResolver
{
    private readonly SystemDbContext systemContext;
    private readonly string baseConnectionString;
    private readonly FiscalDatabaseOptions databaseOptions;

    public SqlFiscalDatabaseResolver(
        SystemDbContext systemContext,
        IConfiguration configuration,
        IOptions<FiscalDatabaseOptions> databaseOptions)
    {
        ArgumentNullException.ThrowIfNull(systemContext);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(databaseOptions);

        this.systemContext = systemContext;
        this.databaseOptions = databaseOptions.Value;
        this.baseConnectionString = configuration.GetConnectionString("PetraERP")
            ?? throw new InvalidOperationException("ConnectionStrings:PetraERP is not configured.");
    }

    public async Task<ResolvedFiscalDatabase> ResolveAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        var record = await systemContext.FiscalDatabases.AsNoTracking()
            .FirstOrDefaultAsync(db => db.Id == workspaceId, cancellationToken)
            .ConfigureAwait(false);

        if (record is null)
        {
            throw new ResourceNotFoundException($"Fiscal workspace '{workspaceId}' is not registered.");
        }

        var company = await systemContext.Companies.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == record.CompanyId, cancellationToken)
            .ConfigureAwait(false);

        return await ResolveReadyAsync(record, company, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ResolvedFiscalDatabase> ResolveByCompanyAndYearAsync(Guid companyId, Guid fiscalYearId, CancellationToken cancellationToken = default)
    {
        var company = await systemContext.Companies.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ResourceNotFoundException($"Company '{companyId}' does not exist.");

        var record = await systemContext.FiscalDatabases.AsNoTracking()
            .FirstOrDefaultAsync(db => db.CompanyId == companyId && db.FiscalYearId == fiscalYearId, cancellationToken)
            .ConfigureAwait(false);

        if (record is null)
        {
            throw new ResourceNotFoundException(
                $"Fiscal year '{fiscalYearId}' is not registered for company '{companyId}'.");
        }

        return await ResolveReadyAsync(record, company, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ResolvedFiscalDatabase>> ListCompanyWorkspacesAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        var company = await systemContext.Companies.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken)
            .ConfigureAwait(false);

        var records = await systemContext.FiscalDatabases.AsNoTracking()
            .Where(db => db.CompanyId == companyId && (db.State == "Active" || db.State == "OpeningBalancesPending"))
            .OrderByDescending(db => db.YearNumber)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var list = new List<ResolvedFiscalDatabase>();
        foreach (var record in records)
            list.Add(await ResolveReadyAsync(record, company, cancellationToken).ConfigureAwait(false));
        return list.AsReadOnly();
    }

    public async Task<IReadOnlyList<ResolvedFiscalDatabase>> ListAllActiveWorkspacesAsync(CancellationToken cancellationToken = default)
    {
        var records = await systemContext.FiscalDatabases.AsNoTracking()
            .Where(db => (db.State == "Active" || db.State == "OpeningBalancesPending") && systemContext.Companies.Any(c => c.Id == db.CompanyId && c.IsActive))
            .OrderBy(db => db.CompanyId)
            .ThenByDescending(db => db.YearNumber)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var companyIds = records.Select(r => r.CompanyId).Distinct().ToList();
        var companies = await systemContext.Companies.AsNoTracking()
            .Where(c => companyIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, cancellationToken)
            .ConfigureAwait(false);

        var list = new List<ResolvedFiscalDatabase>();
        foreach (var r in records)
        {
            companies.TryGetValue(r.CompanyId, out var comp);
            list.Add(await ResolveReadyAsync(r, comp, cancellationToken).ConfigureAwait(false));
        }

        return list.AsReadOnly();
    }

    // Process-wide and short-lived. Every request resolves its workspace once, and the full readiness check (an extra connection plus
    // catalog/migration queries on the annual database) used to run for every one of them, which dominated latency under load. A
    // successful check is therefore trusted for ReadinessCacheSeconds for the SAME registry facts; schema drift is still detected,
    // at most that many seconds late. Failures are never cached and any change to the registry row, company or branch set changes the key.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> readyUntil = new(StringComparer.Ordinal);

    /// <summary>
    /// Forgets every cached readiness verdict so the next resolution re-verifies the annual database. Called after in-process schema
    /// work (provisioning, migration, year rollover); a registry state change needs no call because it changes the cache key.
    /// </summary>
    public static void InvalidateReadiness() => readyUntil.Clear();

    private async Task<ResolvedFiscalDatabase> ResolveReadyAsync(FiscalDatabaseRegistryRow row, CompanyRow? company,
        CancellationToken cancellationToken)
    {
        var resolved = BuildResolved(row, company);
        var registeredBranchIds = await systemContext.Branches.AsNoTracking()
            .Where(branch => branch.CompanyId == row.CompanyId).Select(branch => branch.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var cacheTicks = TimeSpan.FromSeconds(Math.Max(0, databaseOptions.ReadinessCacheSeconds)).Ticks;
        var key = cacheTicks == 0 ? string.Empty : string.Join('|', resolved.ConnectionString, row.Id, row.State, row.SchemaVersion, row.RangeStart, row.RangeEnd,
            row.YearCode, company!.BaseCurrencyCode, string.Join(',', registeredBranchIds.Order()));
        if (cacheTicks != 0 && readyUntil.TryGetValue(key, out var until) && until > Environment.TickCount64) return resolved;
        using var accounting = new AccountingDbContext(new DbContextOptionsBuilder<AccountingDbContext>()
            .UseSqlServer(resolved.ConnectionString, sql => sql.UseCompatibilityLevel(databaseOptions.CompatibilityLevel)
                .CommandTimeout(databaseOptions.CommandTimeoutSeconds))
            .AddInterceptors(PetraERP.Infrastructure.Diagnostics.SqlTimingInterceptor.Instance, PetraERP.Infrastructure.Diagnostics.ReadCommittedConnectionInterceptor.Instance).Options);
        try
        {
            using var probe = new SqlConnection(resolved.ConnectionString);
            await probe.OpenAsync(cancellationToken).ConfigureAwait(false);
            await AccountingDatabaseReadiness.ValidateWorkspaceAsync(accounting, row.CompanyId, row.FiscalYearId,
                row.RangeStart!.Value, row.RangeEnd!.Value, row.YearCode!, cancellationToken,
                company!.BaseCurrencyCode, registeredBranchIds).ConfigureAwait(false);
            if (cacheTicks != 0)
            {
                if (readyUntil.Count > 512) readyUntil.Clear();
                readyUntil[key] = Environment.TickCount64 + (long)TimeSpan.FromTicks(cacheTicks).TotalMilliseconds;
            }
        }
        catch (Exception exception) when (exception is SqlException || exception is InvalidOperationException { InnerException: SqlException })
        {
            throw new BusinessRuleException("workspace.database_unavailable", "The registered fiscal database could not be verified; workspace access is blocked.");
        }
        return resolved;
    }

    private ResolvedFiscalDatabase BuildResolved(FiscalDatabaseRegistryRow row, CompanyRow? company)
    {
        if (company is null || company.Id != row.CompanyId || !company.IsActive)
            throw new BusinessRuleException("workspace.company_inactive", "The registered company must exist and be active.");
        if (row.FiscalYearId == Guid.Empty || row.State is not ("Active" or "OpeningBalancesPending") || row.SchemaVersion != 1
            || row.RangeStart is null || row.RangeEnd is null || row.RangeStart > row.RangeEnd
            || row.YearNumber != row.RangeStart.Value.Year || string.IsNullOrWhiteSpace(row.YearCode))
            throw new BusinessRuleException("workspace.not_ready", "The fiscal database is not active at the supported schema version.");
        var centralDatabase = new SqlConnectionStringBuilder(baseConnectionString).InitialCatalog;
        if (string.IsNullOrWhiteSpace(row.DatabaseName)
            || string.Equals(row.DatabaseName, centralDatabase, StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException("workspace.database_invalid", "An independent fiscal database is required.");

        var builder = new SqlConnectionStringBuilder(baseConnectionString)
        {
            InitialCatalog = row.DatabaseName
        };

        return new ResolvedFiscalDatabase(
            WorkspaceId: row.Id,
            CompanyId: row.CompanyId,
            CompanyCode: company.Code,
            CompanyNameArabic: company.NameArabic,
            CompanyNameEnglish: company.NameEnglish,
            BaseCurrencyCode: company.BaseCurrencyCode,
            FiscalYearId: row.FiscalYearId,
            YearNumber: row.YearNumber,
            SafeDatabaseKey: row.SafeDatabaseKey,
            DatabaseName: row.DatabaseName,
            ConnectionString: builder.ConnectionString,
            State: row.State,
            SchemaVersion: row.SchemaVersion,
            IsDefault: row.IsDefault);
    }
}
