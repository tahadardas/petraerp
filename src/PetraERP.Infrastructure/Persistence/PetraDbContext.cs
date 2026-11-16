using Microsoft.EntityFrameworkCore;
using PetraERP.Infrastructure.AccountingCalendars;
using PetraERP.Infrastructure.Currencies;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Inventory;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Parties;
using PetraERP.Infrastructure.Patterns;
using PetraERP.Infrastructure.Sales;
using PetraERP.Infrastructure.Security;

namespace PetraERP.Infrastructure.Persistence;

public sealed class PetraDbContext(DbContextOptions<PetraDbContext> options) : FinancialDbContext(options)
{
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
    internal DbSet<Registry.CompanyDatabaseRegistryRow> CompanyDatabases => Set<Registry.CompanyDatabaseRegistryRow>();
    internal DbSet<Registry.FiscalDatabaseRegistryRow> FiscalDatabases => Set<Registry.FiscalDatabaseRegistryRow>();
    internal DbSet<Registry.ProvisioningOperationRow> ProvisioningOperations => Set<Registry.ProvisioningOperationRow>();
    internal DbSet<Registry.DatabaseVersionRegistryRow> DatabaseVersions => Set<Registry.DatabaseVersionRegistryRow>();
    internal DbSet<Registry.BackupCatalogRow> BackupCatalog => Set<Registry.BackupCatalogRow>();
    internal DbSet<Registry.GlobalEntityMappingRow> GlobalEntityMappings => Set<Registry.GlobalEntityMappingRow>();
    internal DbSet<Registry.IntercompanyOperationRegistryRow> IntercompanyOperations => Set<Registry.IntercompanyOperationRegistryRow>();
    internal DbSet<Registry.MigrationRunRow> MigrationRuns => Set<Registry.MigrationRunRow>();
    internal DbSet<Registry.ModuleRegistryRow> ModuleRegistry => Set<Registry.ModuleRegistryRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.UseCollation(StorageConventions.OrdinalCollation);
        modelBuilder.HasSequence<long>("MovementOrder", "inventory");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PetraDbContext).Assembly);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        if (ChangeTracker.Entries<StockMovementRow>().Any(e => e.State != EntityState.Unchanged))
            throw new InvalidOperationException("Use asynchronous transactional inventory persistence.");
        StampChanges();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        await InventoryMovementGuard.ApplyAsync(this, cancellationToken).ConfigureAwait(false);
        StampChanges();
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken).ConfigureAwait(false);
    }

    private void StampChanges()
    {
        if (ChangeTracker.Entries<Treasury.SettlementApplicationRow>().Any(e => e.State is EntityState.Modified or EntityState.Deleted)
            || ChangeTracker.Entries<Sales.SalesCreditAuditRow>().Any(e => e.State is EntityState.Modified or EntityState.Deleted)
            || ChangeTracker.Entries<PartyPolicyAuditRow>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Settlement and credit audit payloads are immutable; use linked reversals.");
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
