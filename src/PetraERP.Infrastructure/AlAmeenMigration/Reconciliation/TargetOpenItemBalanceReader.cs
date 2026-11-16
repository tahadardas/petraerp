using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PetraERP.Application.AlAmeenMigration.Reconciliation;
using PetraERP.Application.Common;
using PetraERP.Infrastructure.Parties.OpenItems;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Workspaces;

namespace PetraERP.Infrastructure.AlAmeenMigration.Reconciliation;

internal sealed record TargetOpenItemBalances(
    Guid CompanyId,
    Guid FiscalYearId,
    Guid BranchId,
    DateOnly CutoffDate,
    IReadOnlyList<OpenItemBalance> Receivables,
    IReadOnlyList<OpenItemBalance> Payables);

/// <summary>
/// Reads the target's outstanding party balances through the cutoff. Keys use the same party-level document identity the
/// source adapter produces, because the Al-Ameen ledger snapshot has no settlement records to match document by document;
/// that makes this a party-level comparison and it is not evidence that individual documents migrated correctly.
/// </summary>
internal sealed class TargetOpenItemBalanceReader(IAccountingDbContextFactory contextFactory)
{
    public async Task<TargetOpenItemBalances> ReadAsync(
        Guid companyId,
        Guid fiscalYearId,
        Guid branchId,
        DateOnly cutoffDate,
        CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty || fiscalYearId == Guid.Empty || branchId == Guid.Empty)
            throw new ArgumentException("Company, fiscal year and branch are required.");

        using var context = await contextFactory.CreateDbContextAsync(companyId, fiscalYearId, cancellationToken).ConfigureAwait(false);
        using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        var result = await ReadWithinTransactionAsync(context, companyId, fiscalYearId, branchId, cutoffDate, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    internal static async Task<TargetOpenItemBalances> ReadWithinTransactionAsync(
        AccountingDbContext context,
        Guid companyId,
        Guid fiscalYearId,
        Guid branchId,
        DateOnly cutoffDate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("A serializable read transaction is required.");

        if (!await context.Branches.AsNoTracking().AnyAsync(b => b.CompanyId == companyId && b.Id == branchId, cancellationToken).ConfigureAwait(false))
            throw new BusinessRuleException("migration.invalid_branch", "الفرع غير موجود ضمن الشركة المحددة.");

        var open = (byte)OpenItemState.Open;
        var cutoffExclusive = new DateTimeOffset(cutoffDate.AddDays(1).ToDateTime(new TimeOnly(0, 0, 0)), TimeSpan.Zero);
        var rows = await (from item in context.OpenItems.AsNoTracking()
                          where item.CompanyId == companyId && item.BranchId == branchId && item.State == open
                                && item.OriginDocumentDateUtc < cutoffExclusive
                          join party in context.Parties.AsNoTracking() on item.PartyId equals party.Id
                          where party.CompanyId == companyId
                          group item by new { item.BranchId, item.PartyId, item.AccountId, item.CurrencyCode, party.Code, item.PartyType }
                          into g
                          select new
                          {
                              g.Key.BranchId,
                              g.Key.PartyId,
                              g.Key.AccountId,
                              g.Key.CurrencyCode,
                              g.Key.Code,
                              g.Key.PartyType,
                              Outstanding = g.Sum(i => i.OutstandingAmount),
                              Carrying = g.Sum(i => i.CurrentBaseCarryingAmount),
                          }
                          into projected
                          where projected.Outstanding != 0m
                          orderby projected.PartyId
                          select projected).ToListAsync(cancellationToken).ConfigureAwait(false);

        var customers = new List<OpenItemBalance>();
        var suppliers = new List<OpenItemBalance>();
        var roles = await context.Parties.AsNoTracking()
            .Where(p => p.CompanyId == companyId)
            .Select(p => new { p.Id, p.Role })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var roleByParty = roles.ToDictionary(r => r.Id, r => r.Role);

        foreach (var row in rows)
        {
            var balance = new OpenItemBalance(
                new OpenItemBalanceKey(row.BranchId, row.PartyId, row.AccountId, $"AMIN-PARTY-{row.Code.Trim()}", row.CurrencyCode),
                row.Outstanding,
                row.Carrying);

            // The item records its own direction (1 receivable, 2 payable), so a party that is both is split exactly as posted.
            if (row.PartyType == (byte)OpenItemPartyType.Customer)
                customers.Add(balance);
            else
                suppliers.Add(balance);
        }

        return new TargetOpenItemBalances(companyId, fiscalYearId, branchId, cutoffDate, customers, suppliers);
    }
}
