using Microsoft.EntityFrameworkCore;
using PetraERP.Infrastructure.AccountingCalendars;
using PetraERP.Infrastructure.Currencies;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Inventory;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Parties;
using PetraERP.Infrastructure.Patterns;
using PetraERP.Infrastructure.Sales;
using PetraERP.Infrastructure.Persistence.Actors;

namespace PetraERP.Infrastructure.Persistence;

/// <summary>
/// Scoped company-year financial and inventory database context.
/// Exclusively responsible for accounts, journal entries, inventory, sales, purchases,
/// treasury, cost centers, and fiscal periods for a specific company's fiscal year.
/// </summary>
public sealed partial class AccountingDbContext(DbContextOptions<AccountingDbContext> options, IAccountingActorAuthority? actorAuthority = null) : FinancialDbContext(options)
{
    internal DbSet<ActorReferenceRow> ActorReferences => Set<ActorReferenceRow>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.UseCollation(StorageConventions.OrdinalCollation);
        modelBuilder.HasSequence<long>("MovementOrder", "inventory");

        // Apply all configurations belonging to accounting domain
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AccountingDbContext).Assembly, type =>
            type.Namespace is not null
            && !type.Namespace.Contains(".Security", StringComparison.Ordinal)
            && !type.Namespace.Contains(".Registry", StringComparison.Ordinal)
            && !type.Namespace.Contains(".Productivity", StringComparison.Ordinal));
        AccountingActorModel.Configure(modelBuilder);
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        optionsBuilder.UseSqlServer(sql => sql.MigrationsHistoryTable(AccountingSchema.HistoryTable, AccountingSchema.HistorySchema));
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        throw new InvalidOperationException("Accounting writes require asynchronous authoritative actor verification.");
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;
        try
        {
            ThrowIfWriteFailed();
            if (ChangeTracker.Entries<StockMovementRow>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
                await InventoryMovementGuard.ApplyAsync(this, cancellationToken).ConfigureAwait(false);
            if (!ChangeTracker.HasChanges()) return 0;
            if (writeScope is null || actorAuthority is null)
                throw new PetraERP.Application.Common.BusinessRuleException("accounting.write_scope_required", "A write scope and central actor authority are required.");
            transaction = Database.CurrentTransaction is null
                ? await Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false) : null;
            await VerifyWriteActorAsync(cancellationToken).ConfigureAwait(false);
            await InventoryMovementGuard.ApplyAsync(this, cancellationToken).ConfigureAwait(false);
            StampChanges();
            var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken).ConfigureAwait(false);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch
        {
            MarkWriteFailed();
            throw;
        }
        finally
        {
            if (transaction is not null) await transaction.DisposeAsync().ConfigureAwait(false);
        }
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
