using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PetraERP.Application.Workspaces;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Persistence.Actors;

namespace PetraERP.Infrastructure.Workspaces;

public interface IAccountingDbContextFactory
{
    Task<AccountingDbContext> CreateDbContextAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task<AccountingDbContext> CreateDbContextAsync(Guid companyId, Guid fiscalYearId, CancellationToken cancellationToken = default);
    AccountingDbContext CreateDbContext(string connectionString);
}

public sealed class AccountingDbContextFactory : IAccountingDbContextFactory
{
    private readonly IFiscalDatabaseResolver resolver;
    private readonly FiscalDatabaseOptions databaseOptions;
    private readonly IAccountingActorAuthority actorAuthority;
    private readonly SystemDbContext? systemContext;

    public AccountingDbContextFactory(
        IFiscalDatabaseResolver resolver,
        IOptions<FiscalDatabaseOptions> databaseOptions,
        IAccountingActorAuthority actorAuthority, SystemDbContext? systemContext = null)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(databaseOptions);
        ArgumentNullException.ThrowIfNull(actorAuthority);

        this.resolver = resolver;
        this.databaseOptions = databaseOptions.Value;
        this.actorAuthority = actorAuthority;
        this.systemContext = systemContext;
    }

    public async Task<AccountingDbContext> CreateDbContextAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        var resolved = await resolver.ResolveAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        return CreateDbContext(resolved.ConnectionString);
    }

    public async Task<AccountingDbContext> CreateDbContextAsync(Guid companyId, Guid fiscalYearId, CancellationToken cancellationToken = default)
    {
        var resolved = await resolver.ResolveByCompanyAndYearAsync(companyId, fiscalYearId, cancellationToken).ConfigureAwait(false);
        return CreateDbContext(resolved.ConnectionString);
    }

    public AccountingDbContext CreateDbContext(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var builder = new DbContextOptionsBuilder<AccountingDbContext>();
        builder.UseSqlServer(connectionString, sql => sql
            .UseCompatibilityLevel(databaseOptions.CompatibilityLevel)
            .CommandTimeout(databaseOptions.CommandTimeoutSeconds))
            .AddInterceptors(PetraERP.Infrastructure.Diagnostics.SqlTimingInterceptor.Instance, PetraERP.Infrastructure.Diagnostics.ReadCommittedConnectionInterceptor.Instance);

        var context = new AccountingDbContext(builder.Options, actorAuthority);
        if (systemContext is not null) context.BindIdentity(systemContext);
        return context;
    }
}
