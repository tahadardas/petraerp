using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Workspaces;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Workspaces;

public sealed class SqlLegacyToIndependentDbMigrator : ILegacyToIndependentDbMigrator
{
    private static readonly string[] IncompleteEntities =
    [
        "Company/branch/currency and actor references", "Accounts and account audit", "CostCenters and cost center audit",
        "FiscalYears and AccountingPeriods", "JournalEntries, JournalLines, JournalAudit and posting idempotency/numbering",
        "Parties and credit policy audit", "Items, units, conversions, prices and barcodes", "Warehouses and StockMovements",
        "StockTransfers, StockAdjustments and InventoryDocuments", "SalesInvoices, lines, returns and posting requests",
        "PurchaseInvoices, lines and returns", "Vouchers, lines, allocations and allocation plans",
        "CommercialPapers", "SettlementBatches and SettlementApplications", "Patterns, revisions, grants and numbering",
        "ExchangeRates, company currencies and currency audit", "Reconciliation and recovery validation",
    ];

    private readonly PetraDbContext sourceContext;

    public SqlLegacyToIndependentDbMigrator(PetraDbContext sourceContext)
    {
        ArgumentNullException.ThrowIfNull(sourceContext);
        this.sourceContext = sourceContext;
    }

    public async Task<IReadOnlyList<MigrationSummary>> PlanMigrationAsync(CancellationToken cancellationToken = default)
    {
        var plans = new List<MigrationSummary>();
        var countedJournals = new HashSet<Guid>();

        // 1. Discover all companies and fiscal years in source
        var years = await sourceContext.FiscalYears.AsNoTracking()
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        foreach (var year in years)
        {
            var company = await sourceContext.Companies.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == year.CompanyId, cancellationToken).ConfigureAwait(false);

            var companyCode = company?.Code ?? "UNKNOWN";
            var targetDbName = $"PetraERP_{companyCode}_{year.RangeStart.Year}";

            var accountsCount = await sourceContext.Accounts.AsNoTracking()
                .CountAsync(a => a.CompanyId == year.CompanyId, cancellationToken).ConfigureAwait(false);

            var costCentersCount = await sourceContext.CostCenters.AsNoTracking()
                .CountAsync(c => c.CompanyId == year.CompanyId, cancellationToken).ConfigureAwait(false);

            var journalEntries = await sourceContext.JournalEntries.AsNoTracking()
                .Where(j => j.CompanyId == year.CompanyId && (j.PostingFiscalYearId == year.Id
                    || (j.PostingFiscalYearId == null && j.AccountingDate >= year.RangeStart && j.AccountingDate <= year.RangeEnd
                        && !sourceContext.FiscalYears.Any(other => other.CompanyId == year.CompanyId && other.Id != year.Id
                            && other.RangeStart <= j.AccountingDate && other.RangeEnd >= j.AccountingDate))))
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            var journalIds = journalEntries.Select(j => j.Id).ToHashSet();
            if (journalIds.Any(id => !countedJournals.Add(id)))
                throw new BusinessRuleException("legacy.journal_partition_ambiguous", "A journal would be counted in multiple fiscal years. Execution remains blocked.");

            var journalLines = await sourceContext.JournalLines.AsNoTracking()
                .Where(l => journalIds.Contains(l.JournalId))
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            var totalDebit = journalLines.Sum(l => l.BaseDebit);
            var totalCredit = journalLines.Sum(l => l.BaseCredit);

            var itemsCount = await sourceContext.Items.AsNoTracking()
                .CountAsync(i => i.CompanyId == year.CompanyId, cancellationToken).ConfigureAwait(false);

            var warehousesCount = await sourceContext.Warehouses.AsNoTracking()
                .CountAsync(w => w.CompanyId == year.CompanyId, cancellationToken).ConfigureAwait(false);

            var startDateUtc = year.RangeStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var endDateUtc = year.RangeEnd.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

            var movements = await sourceContext.StockMovements.AsNoTracking()
                .Where(m => m.CompanyId == year.CompanyId && m.MovementDateUtc >= startDateUtc && m.MovementDateUtc <= endDateUtc)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            var totalStockQty = movements.Sum(m => m.BaseQuantity);

            var salesCount = await sourceContext.SalesInvoices.AsNoTracking()
                .CountAsync(s => s.CompanyId == year.CompanyId && s.InvoiceDateUtc >= startDateUtc && s.InvoiceDateUtc <= endDateUtc, cancellationToken).ConfigureAwait(false);

            var purchasesCount = await sourceContext.PurchaseInvoices.AsNoTracking()
                .CountAsync(p => p.CompanyId == year.CompanyId && p.InvoiceDateUtc >= startDateUtc && p.InvoiceDateUtc <= endDateUtc, cancellationToken).ConfigureAwait(false);

            var vouchersCount = await sourceContext.Vouchers.AsNoTracking()
                .CountAsync(v => v.CompanyId == year.CompanyId && v.VoucherDateUtc >= startDateUtc && v.VoucherDateUtc <= endDateUtc, cancellationToken).ConfigureAwait(false);

            var discrepancies = new List<string>
            {
                "DRY RUN ONLY: complete legacy migration is not approved. No rows were migrated.",
            };
            if (journalEntries.Any(j => j.AccountingDate < year.RangeStart || j.AccountingDate > year.RangeEnd))
                discrepancies.Add("Some journals have an explicit PostingFiscalYearId outside their accounting date range; counted only in the explicitly assigned year.");
            var ambiguousDrafts = await sourceContext.JournalEntries.AsNoTracking().CountAsync(j => j.CompanyId == year.CompanyId
                && j.PostingFiscalYearId == null && j.AccountingDate >= year.RangeStart && j.AccountingDate <= year.RangeEnd
                && sourceContext.FiscalYears.Any(other => other.CompanyId == year.CompanyId && other.Id != year.Id
                    && other.RangeStart <= j.AccountingDate && other.RangeEnd >= j.AccountingDate), cancellationToken).ConfigureAwait(false);
            if (ambiguousDrafts != 0)
                discrepancies.Add($"{ambiguousDrafts} unassigned journals have ambiguous fiscal ranges and are excluded from planned counts.");
            if (totalDebit != totalCredit)
            {
                discrepancies.Add($"Unbalanced journals in Year {year.Code}: TotalDebit={totalDebit:N4}, TotalCredit={totalCredit:N4}, Diff={totalDebit - totalCredit:N4}");
            }

            plans.Add(new MigrationSummary(
                CompanyId: year.CompanyId,
                FiscalYearId: year.Id,
                TargetDatabaseName: targetDbName,
                AccountsMigrated: 0,
                CostCentersMigrated: 0,
                JournalEntriesMigrated: 0,
                JournalLinesMigrated: 0,
                TotalDebit: totalDebit,
                TotalCredit: totalCredit,
                ItemsMigrated: 0,
                WarehousesMigrated: 0,
                StockMovementsMigrated: 0,
                TotalStockQuantity: totalStockQty,
                SalesInvoicesMigrated: 0,
                PurchaseInvoicesMigrated: 0,
                VouchersMigrated: 0,
                Discrepancies: discrepancies.AsReadOnly())
            {
                PlannedCounts = new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    ["Accounts"] = accountsCount, ["CostCenters"] = costCentersCount,
                    ["JournalEntries"] = journalEntries.Count, ["JournalLines"] = journalLines.Count,
                    ["Items"] = itemsCount, ["Warehouses"] = warehousesCount, ["StockMovements"] = movements.Count,
                    ["SalesInvoices"] = salesCount, ["PurchaseInvoices"] = purchasesCount, ["Vouchers"] = vouchersCount,
                },
                PendingEntities = Array.AsReadOnly(IncompleteEntities),
            });
        }

        return plans.AsReadOnly();
    }

    public async Task<MigrationSummary> MigrateCompanyYearAsync(Guid companyId, Guid fiscalYearId, bool dryRun = false, CancellationToken cancellationToken = default)
    {
        if (!dryRun)
            throw new BusinessRuleException("legacy.migration_not_approved",
                "Complete legacy migration is not approved in Phase 1. Only Dry Run is available; no target data or registry was changed. Review PendingEntities in the plan.");
        var plans = await PlanMigrationAsync(cancellationToken).ConfigureAwait(false);
        return plans.SingleOrDefault(p => p.CompanyId == companyId && p.FiscalYearId == fiscalYearId)
            ?? throw new ResourceNotFoundException("Fiscal year not found in legacy source.");
    }
}
