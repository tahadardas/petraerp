using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using PetraERP.Infrastructure.Currencies;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Security;
using PetraERP.Infrastructure.Persistence.Registry;

namespace PetraERP.Infrastructure.Persistence;

/// <summary>Central storage facade. API registration always supplies SystemDbContext.
/// Legacy conversion preserves explicit installation/test tooling without routing annual financial writes.</summary>
public sealed class CentralDataContext
{
    internal DbContext Context { get; }
    public CentralDataContext(SystemDbContext context) => Context = context ?? throw new ArgumentNullException(nameof(context));
    private CentralDataContext(PetraDbContext context) => Context = context ?? throw new ArgumentNullException(nameof(context));
    public static implicit operator CentralDataContext(PetraDbContext context) => new(context);
    public static implicit operator CentralDataContext(SystemDbContext context) => new(context);
    internal DatabaseFacade Database => Context.Database;
    internal ChangeTracker ChangeTracker => Context.ChangeTracker;
    internal EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class => Context.Entry(entity);
    internal DbSet<TEntity> Set<TEntity>() where TEntity : class => Context.Set<TEntity>();
    internal EntityEntry<TEntity> Add<TEntity>(TEntity entity) where TEntity : class => Context.Add(entity);
    internal Task<int> SaveChangesAsync(CancellationToken token = default) => Context.SaveChangesAsync(token);
    public Task<bool> HasAuthorizedBranchAsync(Guid actor, Guid company, Guid branch, CancellationToken token) =>
        Users.AsNoTracking().AnyAsync(u => u.Id == actor && u.IsActive && !u.MustChangePassword
            && UserCompanyScopes.Any(s => s.UserId == u.Id && s.CompanyId == company)
            && UserBranchScopes.Any(s => s.UserId == u.Id && s.CompanyId == company && s.BranchId == branch)
            && Companies.Any(c => c.Id == company && c.IsActive)
            && Branches.Any(b => b.Id == branch && b.CompanyId == company && b.IsActive), token);
    internal DbSet<CurrencyRow> Currencies => Set<CurrencyRow>();
    internal DbSet<CompanyRow> Companies => Set<CompanyRow>();
    internal DbSet<BranchRow> Branches => Set<BranchRow>();
    internal DbSet<CompanyAdditionalCurrencyRow> CompanyAdditionalCurrencies => Set<CompanyAdditionalCurrencyRow>();
    internal DbSet<ExchangeRateRow> ExchangeRates => Set<ExchangeRateRow>();
    internal DbSet<CurrencyAuditRow> CurrencyAudit => Set<CurrencyAuditRow>();
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
    internal DbSet<CompanyDatabaseRegistryRow> CompanyDatabases => Set<CompanyDatabaseRegistryRow>();
    internal DbSet<FiscalDatabaseRegistryRow> FiscalDatabases => Set<FiscalDatabaseRegistryRow>();
    internal DbSet<ProvisioningOperationRow> ProvisioningOperations => Set<ProvisioningOperationRow>();
    internal DbSet<DatabaseVersionRegistryRow> DatabaseVersions => Set<DatabaseVersionRegistryRow>();
    internal DbSet<BackupCatalogRow> BackupCatalog => Set<BackupCatalogRow>();
    internal DbSet<GlobalEntityMappingRow> GlobalEntityMappings => Set<GlobalEntityMappingRow>();
    internal DbSet<IntercompanyOperationRegistryRow> IntercompanyOperations => Set<IntercompanyOperationRegistryRow>();
}
