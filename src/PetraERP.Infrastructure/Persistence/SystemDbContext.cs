using Microsoft.EntityFrameworkCore;
using PetraERP.Infrastructure.Currencies;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Persistence.Registry;
using PetraERP.Infrastructure.Security;

namespace PetraERP.Infrastructure.Persistence;

/// <summary>
/// Central system database context (PetraERP_System).
/// Exclusively responsible for users, roles, permissions, scopes, sessions, companies, branches,
/// currencies, security audit, and the global database registry.
/// Does NOT contain transactional journals, invoices, or stock movements.
/// </summary>
public sealed class SystemDbContext(DbContextOptions<SystemDbContext> options) : DbContext(options)
{
    // system schema — organization & currencies
    internal DbSet<CurrencyRow> Currencies => Set<CurrencyRow>();
    internal DbSet<CompanyRow> Companies => Set<CompanyRow>();
    internal DbSet<BranchRow> Branches => Set<BranchRow>();
    internal DbSet<CompanyAdditionalCurrencyRow> CompanyAdditionalCurrencies => Set<CompanyAdditionalCurrencyRow>();
    internal DbSet<ExchangeRateRow> ExchangeRates => Set<ExchangeRateRow>();
    internal DbSet<CurrencyAuditRow> CurrencyAudit => Set<CurrencyAuditRow>();

    // security schema — identity, RBAC, scopes, sessions and security audit
    internal DbSet<UserRow> Users => Set<UserRow>();
    internal DbSet<RoleRow> Roles => Set<RoleRow>();
    internal DbSet<PermissionRow> Permissions => Set<PermissionRow>();
    internal DbSet<RolePermissionRow> RolePermissions => Set<RolePermissionRow>();
    internal DbSet<UserRoleRow> UserRoles => Set<UserRoleRow>();
    internal DbSet<UserCompanyScopeRow> UserCompanyScopes => Set<UserCompanyScopeRow>();
    internal DbSet<UserBranchScopeRow> UserBranchScopes => Set<UserBranchScopeRow>();
    internal DbSet<SessionRow> Sessions => Set<SessionRow>();
    internal DbSet<RefreshTokenRow> RefreshTokens => Set<RefreshTokenRow>();
    internal DbSet<SecurityAuditEventRow> SecurityAuditEvents => Set<SecurityAuditEventRow>();
    internal DbSet<BootstrapStateRow> BootstrapState => Set<BootstrapStateRow>();

    // system schema — central multi-database and workspace registry
    internal DbSet<CompanyDatabaseRegistryRow> CompanyDatabases => Set<CompanyDatabaseRegistryRow>();
    internal DbSet<FiscalDatabaseRegistryRow> FiscalDatabases => Set<FiscalDatabaseRegistryRow>();
    internal DbSet<ProvisioningOperationRow> ProvisioningOperations => Set<ProvisioningOperationRow>();
    internal DbSet<DatabaseVersionRegistryRow> DatabaseVersions => Set<DatabaseVersionRegistryRow>();
    internal DbSet<BackupCatalogRow> BackupCatalog => Set<BackupCatalogRow>();
    internal DbSet<GlobalEntityMappingRow> GlobalEntityMappings => Set<GlobalEntityMappingRow>();
    internal DbSet<IntercompanyOperationRegistryRow> IntercompanyOperations => Set<IntercompanyOperationRegistryRow>();
    internal DbSet<MigrationRunRow> MigrationRuns => Set<MigrationRunRow>();
    internal DbSet<ModuleRegistryRow> ModuleRegistry => Set<ModuleRegistryRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.UseCollation(StorageConventions.OrdinalCollation);

        // Apply only System, Security, and Registry configurations
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SystemDbContext).Assembly, type =>
            type.Namespace is not null
            // Branch account mappings and the branch audit belong to the annual ledger databases only (they reference ledger accounts).
            && type != typeof(BranchAccountLinkConfiguration) && type != typeof(BranchAuditConfiguration)
            && (type.Namespace.Contains(".Security", StringComparison.Ordinal)
                || type.Namespace.Contains(".Registry", StringComparison.Ordinal)
                || type.Namespace.Contains(".Organizations", StringComparison.Ordinal)
                || type.Namespace.Contains(".Currencies", StringComparison.Ordinal)
                || type.Namespace.Contains(".Productivity", StringComparison.Ordinal)));
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampChanges();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampChanges();
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken).ConfigureAwait(false);
    }

    private void StampChanges()
    {
        if (ChangeTracker.Entries<ExchangeRateRow>().Any(e => e.State is EntityState.Modified or EntityState.Deleted)
            || ChangeTracker.Entries<CurrencyAuditRow>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Currency history is append-only; register a new correction with a reason.");

        var now = DateTimeOffset.UtcNow;
        foreach (var entry in ChangeTracker.Entries<ITrackedStorageRow>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAtUtc = now;
                entry.Entity.UpdatedAtUtc = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                var created = entry.Property(static row => row.CreatedAtUtc);
                if (created.CurrentValue != created.OriginalValue)
                {
                    throw new InvalidOperationException("Storage creation timestamps cannot be rewritten.");
                }

                created.IsModified = false;
                entry.Entity.UpdatedAtUtc = now;
            }
        }
    }
}
