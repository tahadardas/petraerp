using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PetraERP.Application.AlAmeenMigration.Reconciliation;
using PetraERP.Application.Common;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Workspaces;

namespace PetraERP.Infrastructure.AlAmeenMigration.Reconciliation;

internal sealed record TargetStockBalances(
    Guid CompanyId,
    Guid FiscalYearId,
    Guid BranchId,
    DateOnly CutoffDate,
    int MovementCount,
    IReadOnlyList<StockBalance> Balances);

/// <summary>Rebuilds quantity and value from signed movements through a UTC cutoff, in each item's base unit.</summary>
internal sealed class TargetStockBalanceReader(IAccountingDbContextFactory contextFactory)
{
    public async Task<TargetStockBalances> ReadAsync(Guid companyId, Guid fiscalYearId, Guid branchId, DateOnly cutoffDate,
        CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty || fiscalYearId == Guid.Empty || branchId == Guid.Empty)
            throw new ArgumentException("Company, fiscal year and branch are required.");
        ArgumentOutOfRangeException.ThrowIfEqual(cutoffDate, DateOnly.MaxValue);

        using var context = await contextFactory.CreateDbContextAsync(companyId, fiscalYearId, cancellationToken).ConfigureAwait(false);
        using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        var result = await ReadWithinTransactionAsync(context, companyId, fiscalYearId, branchId, cutoffDate, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    internal static async Task<TargetStockBalances> ReadWithinTransactionAsync(AccountingDbContext context, Guid companyId, Guid fiscalYearId,
        Guid branchId, DateOnly cutoffDate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentOutOfRangeException.ThrowIfEqual(cutoffDate, DateOnly.MaxValue);
        if (context.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("A serializable read transaction is required.");
        var year = await context.FiscalYears.AsNoTracking().Where(y => y.CompanyId == companyId && y.Id == fiscalYearId)
            .Select(y => new { y.RangeStart, y.RangeEnd }).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (year is null || cutoffDate < year.RangeStart || cutoffDate > year.RangeEnd)
            throw new BusinessRuleException("migration.invalid_cutoff", "تاريخ القطع خارج السنة المالية المحددة أو السنة غير موجودة.");
        if (!await context.Branches.AsNoTracking().AnyAsync(b => b.CompanyId == companyId && b.Id == branchId, cancellationToken).ConfigureAwait(false))
            throw new BusinessRuleException("migration.invalid_branch", "الفرع غير موجود ضمن الشركة المحددة.");

        var start = new DateTimeOffset(year.RangeStart.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var endExclusive = new DateTimeOffset(cutoffDate.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var movements = context.StockMovements.AsNoTracking().Where(m =>
            m.CompanyId == companyId && m.BranchId == branchId && m.MovementDateUtc < endExclusive);

        // Legacy or mis-scoped history must not silently become authoritative evidence.
        var invalid = await movements.AnyAsync(m => m.MovementDateUtc < start || m.RecordedAtUtc == null
            || m.QuantityBefore == null || m.QuantityAfter == null || m.ValueBefore == null || m.ValueAfter == null
            || !context.Items.Any(i => i.Id == m.ItemId && i.CompanyId == companyId && i.BaseUnitId != Guid.Empty)
            || !context.Warehouses.Any(w => w.Id == m.WarehouseId && w.CompanyId == companyId && w.BranchId == branchId),
            cancellationToken).ConfigureAwait(false);
        if (invalid)
            throw new BusinessRuleException("migration.invalid_stock_evidence", "توجد حركات مخزون تاريخية أو خارج النطاق لا تسمح بمطابقة موثوقة.");

        var movementCount = await movements.CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await (from movement in movements
                          join item in context.Items.AsNoTracking() on movement.ItemId equals item.Id
                          where item.CompanyId == companyId
                          group movement by new { movement.BranchId, movement.ItemId, movement.WarehouseId, item.BaseUnitId } into g
                          select new
                          {
                              g.Key.BranchId,
                              g.Key.ItemId,
                              g.Key.WarehouseId,
                              g.Key.BaseUnitId,
                              Quantity = g.Sum(m => m.BaseQuantity),
                              Value = g.Sum(m => m.BaseQuantity < 0m ? -m.TotalCost : m.TotalCost),
                          }).ToListAsync(cancellationToken).ConfigureAwait(false);
        var balances = rows.Select(row => new StockBalance(
            new StockBalanceKey(row.BranchId, row.ItemId, row.WarehouseId, row.BaseUnitId), row.Quantity, row.Value)).ToList();

        return new TargetStockBalances(companyId, fiscalYearId, branchId, cutoffDate, movementCount, balances);
    }
}
