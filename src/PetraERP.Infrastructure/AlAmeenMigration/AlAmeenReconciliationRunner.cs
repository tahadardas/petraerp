using System.Globalization;
using PetraERP.Application.AlAmeenMigration;
using PetraERP.Application.AlAmeenMigration.Mapping;
using PetraERP.Application.AlAmeenMigration.Reconciliation;
using PetraERP.Application.Common;
using PetraERP.Infrastructure.AlAmeenMigration.Reconciliation;
using PetraERP.Infrastructure.Workspaces;

namespace PetraERP.Infrastructure.AlAmeenMigration;

/// <summary>
/// Compares a frozen Al-Ameen snapshot against the target database, measure by measure, over the same branch and cutoff.
/// A check passes only when the two sides agree on every resolved identity; it never infers agreement from the target
/// balancing itself, and it does not claim completeness beyond what the identity resolution could resolve.
/// </summary>
public sealed class AlAmeenReconciliationRunner(
    IAccountingDbContextFactory contextFactory,
    TimeProvider timeProvider)
{
    private readonly IAccountingDbContextFactory _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public async Task<MigrationReconciliationResponse> ReconcileAsync(
        SourceSnapshot snapshot,
        IdentityResolution identities,
        Guid branchId,
        DateOnly cutoffDate,
        AccountComparisonMode mode,
        Guid migrationRunId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(identities);

        if (migrationRunId == Guid.Empty)
            throw new ArgumentException("A migration run identity is required so the evidence belongs to one run.", nameof(migrationRunId));
        if (identities.CompanyId == Guid.Empty || identities.FiscalYearId == Guid.Empty)
            throw new BusinessRuleException("migration.scope_required", "المطابقة تحتاج شركة وسنة مالية محددتين.");
        if (identities.CutoffDate != cutoffDate)
            throw new BusinessRuleException("migration.cutoff_mismatch", "تاريخ قطع لقطة المصدر لا يطابق تاريخ قطع قراءة الهدف.");

        var source = AlAmeenSourceBalanceAdapter.FromSnapshot(snapshot, identities, mode);

        var targetLedger = await new TargetLedgerBalanceReader(_contextFactory)
            .ReadAsync(identities.CompanyId, identities.FiscalYearId, branchId, cutoffDate, cancellationToken).ConfigureAwait(false);
        var targetStock = await new TargetStockBalanceReader(_contextFactory)
            .ReadAsync(identities.CompanyId, identities.FiscalYearId, branchId, cutoffDate, cancellationToken).ConfigureAwait(false);
        var targetOpenItems = await new TargetOpenItemBalanceReader(_contextFactory)
            .ReadAsync(identities.CompanyId, identities.FiscalYearId, branchId, cutoffDate, cancellationToken).ConfigureAwait(false);

        var target = new ReconciliationBalances(
            targetLedger.Balances,
            targetOpenItems.Receivables,
            targetOpenItems.Payables,
            targetStock.Balances);

        var differences = ReconciliationBalanceComparer.Compare(source, target, mode);

        var checks = new List<MigrationCheckDto>
        {
            BuildCheck("LEDGER_ACCOUNT_BALANCES", differences.Accounts.Select(d => ($"{d.Key.AccountId:N}/{d.Key.CurrencyCode}", d.Measure, d.Source, d.Target)), "الميزان العام لكل حساب وعملة", mode),
            BuildCheck("RECEIVABLES", differences.Receivables.Select(d => ($"{d.Key.PartyId:N}/{d.Key.CurrencyCode}/{d.Key.SourceDocumentId:N}", d.Measure, d.Source, d.Target)), "ذمم العملاء", mode),
            BuildCheck("PAYABLES", differences.Payables.Select(d => ($"{d.Key.PartyId:N}/{d.Key.CurrencyCode}/{d.Key.SourceDocumentId:N}", d.Measure, d.Source, d.Target)), "ذمم الموردين", mode),
            BuildCheck("STOCK", differences.Stock.Select(d => ($"{d.Key.ItemId:N}/{d.Key.WarehouseId:N}/{d.Key.UnitId:N}", d.Measure, d.Source, d.Target)), "أرصدة المخزون", mode),
        };

        var allPassed = identities.IsComplete && checks.All(c => c.Passed);
        var ledgerDifference = Difference(checks[0]);
        var arDifference = Difference(checks[1]);
        var apDifference = Difference(checks[2]);
        var stockDifference = Difference(checks[3]);

        if (!identities.IsComplete)
        {
            checks.Add(new MigrationCheckDto(
                "IDENTITY_RESOLUTION",
                0m.ToString("F4", CultureInfo.InvariantCulture),
                0m.ToString("F4", CultureInfo.InvariantCulture),
                0m.ToString("F4", CultureInfo.InvariantCulture),
                false,
                $"لم تُبدول {identities.Issues.Count} سجل مصدر إلى هوية هدف؛ لا يمكن اعتبار أي تطابق دليلاً على الاكتمال."));
        }

        return new MigrationReconciliationResponse(
            migrationRunId,
            _timeProvider.GetUtcNow(),
            Money(ledgerDifference),
            Money(arDifference),
            Money(apDifference),
            Money(stockDifference),
            allPassed,
            checks);
    }

    private static MigrationCheckDto BuildCheck(
        string name,
        IEnumerable<(string Identity, string Measure, decimal? Source, decimal? Target)> rows,
        string label,
        AccountComparisonMode mode)
    {
        var list = rows.ToList();
        var sourceTotal = list.Sum(r => r.Source ?? 0m);
        var targetTotal = list.Sum(r => r.Target ?? 0m);
        var netDifference = targetTotal - sourceTotal;

        if (list.Count == 0)
            return new MigrationCheckDto(name, Money(0m), Money(0m), Money(0m), true, $"لا فروق في {label} على مستوى كل هوية ({ModeText(mode)}).");

        var rendered = list.Take(20)
            .Select(r => $"{r.Identity}: {r.Measure} مصدر={r.Source?.ToString("0.####", CultureInfo.InvariantCulture) ?? "-"} هدف={r.Target?.ToString("0.####", CultureInfo.InvariantCulture) ?? "-"}")
            .ToList();
        if (list.Count > 20)
            rendered.Add($"و{list.Count - 20} فروق أخرى");
        var details = string.Join(" | ", rendered);

        return new MigrationCheckDto(
            name,
            Money(sourceTotal),
            Money(targetTotal),
            Money(netDifference),
            list.Count == 0,
            details);

        static string ModeText(AccountComparisonMode mode) => mode == AccountComparisonMode.Cutover ? "أرصدة صافية" : "مجاميع مدينة/دائنة";
    }

    private static decimal Difference(MigrationCheckDto check) => decimal.Parse(check.Difference, CultureInfo.InvariantCulture);

    private static string Money(decimal value) => value.ToString("F4", CultureInfo.InvariantCulture);
}
