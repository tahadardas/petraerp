using Microsoft.EntityFrameworkCore;
using PetraERP.Application.AlAmeenMigration.Mapping;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.OpeningBalances;
using PetraERP.Application.Inventory.Opening;
using PetraERP.Application.Security;
using PetraERP.Infrastructure.GeneralLedger.OpeningBalances;
using PetraERP.Infrastructure.Inventory;
using PetraERP.Application.Parties.OpenItems;
using PetraERP.Infrastructure.Parties.OpenItems;
using PetraERP.Infrastructure.Persistence.Actors;
using PetraERP.Infrastructure.Workspaces;

namespace PetraERP.Infrastructure.AlAmeenMigration;

public sealed record FastCutoverResult(
    Guid MigrationRunId,
    int AccountsImported,
    int PartiesImported,
    int ItemsImported,
    int WarehousesImported,
    Guid OpeningBalanceBatchId,
    decimal TotalOpeningBalance,
    decimal TotalOpeningCredit,
    int StockBalancesImported,
    decimal StockValuationTotal,
    bool Success,
    string Message);

/// <summary>
/// Writes the financial effect of a cutover from one frozen capture through one fully resolved identity set.
/// It never creates master data, never defaults a unit or currency, never drops an unmappable row, and derives its
/// correlation key from the capture itself so replaying the same capture cannot produce a second financial effect.
/// </summary>
public sealed class AlAmeenFastCutoverService(
    IAccountingDbContextFactory contextFactory,
    TimeProvider timeProvider)
{
    private readonly IAccountingDbContextFactory _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public Task<FastCutoverResult> ExecuteAsync(
        SourceSnapshot snapshot,
        IdentityResolution identities,
        Guid companyId,
        Guid branchId,
        Guid fiscalYearId,
        Guid actorUserId,
        string baseCurrencyCode,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(snapshot, identities, companyId, branchId, fiscalYearId, actorUserId, baseCurrencyCode, includeAccountBalances: true, cancellationToken);

    /// <summary>
    /// <paramref name="includeAccountBalances"/> is false for a full-history run, whose replayed vouchers already rebuild
    /// every account balance.
    /// </summary>
    public async Task<FastCutoverResult> ExecuteAsync(
        SourceSnapshot snapshot,
        IdentityResolution identities,
        Guid companyId,
        Guid branchId,
        Guid fiscalYearId,
        Guid actorUserId,
        string baseCurrencyCode,
        bool includeAccountBalances,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(identities);

        if (!identities.IsComplete)
            throw new BusinessRuleException("migration.identity_incomplete", "مواءمة هويات المصدر غير مكتملة؛ الترحيل مرفوض بدل إسقاط السجلات.");
        if (identities.CompanyId != companyId || identities.FiscalYearId != fiscalYearId || identities.BranchId != branchId)
            throw new BusinessRuleException("migration.identity_scope_mismatch", "الهويات المبدولة لا تنتمي إلى الشركة والسنة والفرع المطلوب ترحيلهم.");

        var correlationKey = $"ALAMEEN-{snapshot.SourceFingerprint[..16]}";
        // A full-history run rebuilds the account balances from the replayed vouchers, so posting the cutover balances as well
        // would count every account twice; party open items and opening stock are still established here.
        var openingLines = includeAccountBalances
            ? BuildOpeningLines(snapshot, identities, out var totalOpeningDebit, out var totalOpeningCredit)
            : NoOpeningLines(out totalOpeningDebit, out totalOpeningCredit);

        if (openingLines.Count >= 2 && totalOpeningDebit != totalOpeningCredit)
            throw new BusinessRuleException("migration.opening_unbalanced", $"الأرصدة الافتتاحية غير متوازنة: مدينة {totalOpeningDebit} مقابل دائنة {totalOpeningCredit}؛ الترحيل مرفوض.");

        var openingBatchId = openingLines.Count >= 2
            ? await PostOpeningBalancesAsync(companyId, fiscalYearId, branchId, actorUserId, baseCurrencyCode, correlationKey, openingLines, cancellationToken).ConfigureAwait(false)
            : Guid.Empty;

        await PostMigrationOpenItemsAsync(companyId, fiscalYearId, branchId, actorUserId, snapshot, identities, correlationKey, cancellationToken).ConfigureAwait(false);

        var stockValuation = 0m;
        if (AlAmeenSourceBalanceAdapter.PostableStock(snapshot).Any())
        {
            var openingStockLines = new List<OpeningStockLineInput>();
            foreach (var row in AlAmeenSourceBalanceAdapter.PostableStock(snapshot))
            {
                var itemId = identities.Require(MigrationEntityKind.Item, row.ItemId, row.ItemId.ToString("N"));
                var warehouseId = identities.Require(MigrationEntityKind.Warehouse, row.StoreId, row.StoreId.ToString("N"));
                var unitId = identities.Require(MigrationEntityKind.Unit, row.ItemId, itemId.ToString("N"));
                stockValuation += row.TotalValue;
                openingStockLines.Add(new OpeningStockLineInput(warehouseId, itemId, unitId, row.Quantity, row.TotalValue, "رصيد مستودعي افتتاحي مستورد"));
            }

            using var stockContext = await _contextFactory.CreateDbContextAsync(companyId, fiscalYearId, cancellationToken).ConfigureAwait(false);
            stockContext.BindWriteScope(new AccountingWriteScope(companyId, fiscalYearId, actorUserId, AccountingOperation.ManageTransfers, branchId));
            await new SqlInventoryOpeningService(stockContext, _timeProvider).PostOpeningStockBatchAsync(
                companyId,
                actorUserId,
                new CreateOpeningStockBatchCommand(
                    branchId,
                    // The stock position is the one held at the capture cutoff, so the movement must be dated there,
                    // not at the wall-clock moment of the import run.
                    new DateTimeOffset(DateTime.SpecifyKind(snapshot.CutoffUtc, DateTimeKind.Unspecified), TimeSpan.Zero),
                    $"ALAMEEN-STK-{correlationKey}",
                    $"ALAMEEN-STK-{correlationKey}",
                    openingStockLines),
                cancellationToken).ConfigureAwait(false);
        }

        return new FastCutoverResult(
            Guid.NewGuid(),
            snapshot.Accounts.Count,
            snapshot.Parties.Count,
            snapshot.Items.Count,
            snapshot.Warehouses.Count,
            openingBatchId,
            totalOpeningDebit,
            totalOpeningCredit,
            snapshot.Stock.Count,
            stockValuation,
            true,
            "تم ترحيل الأرصدة الافتتاحية والمستودعية من اللقطة المجمدة عبر هويات مبدولة بالكامل.");
    }

    private static List<OpeningBalanceLineInput> NoOpeningLines(out decimal totalDebit, out decimal totalCredit)
    {
        totalDebit = 0m;
        totalCredit = 0m;
        return [];
    }

    /// <summary>
    /// Net per account and transaction currency, with the source line rate preserved. The prior implementation collapsed
    /// every line into the base currency at rate one, which destroyed foreign-currency history.
    /// </summary>
    private static List<OpeningBalanceLineInput> BuildOpeningLines(SourceSnapshot snapshot, IdentityResolution identities, out decimal totalDebit, out decimal totalCredit)
    {
        var lines = new List<OpeningBalanceLineInput>();
        var order = 1;
        totalDebit = 0m;
        totalCredit = 0m;
        foreach (var p in AlAmeenOpeningNormalizer.Normalize(snapshot, identities))
        {
            var baseDebit = p.NetBase > 0 ? p.NetBase : 0m;
            var baseCredit = p.NetBase < 0 ? -p.NetBase : 0m;
            totalDebit += baseDebit;
            totalCredit += baseCredit;
            lines.Add(new OpeningBalanceLineInput(
                p.AccountId,
                null,
                p.Currency,
                p.NetBase > 0 ? p.Foreign : 0m,
                p.NetBase < 0 ? p.Foreign : 0m,
                p.Rate,
                baseDebit,
                baseCredit,
                "رصيد افتتاحي مستورد من برنامج الأمين",
                order++));
        }

        return lines;
    }

    /// <summary>
    /// A party's control-account balance becomes one migration open item, so the migrated subsidiary ledger reconciles
    /// instead of being absorbed into the control account. Replaying the same capture does not create a second item.
    /// </summary>
    private async Task PostMigrationOpenItemsAsync(
        Guid companyId,
        Guid fiscalYearId,
        Guid branchId,
        Guid actorUserId,
        SourceSnapshot snapshot,
        IdentityResolution identities,
        string correlationKey,
        CancellationToken cancellationToken)
    {
        var nets = AlAmeenSourceBalanceAdapter.ComputePartyNets(snapshot, identities);
        if (nets.Count == 0)
            return;

        using var context = await _contextFactory.CreateDbContextAsync(companyId, fiscalYearId, cancellationToken).ConfigureAwait(false);
        context.BindWriteScope(new AccountingWriteScope(companyId, fiscalYearId, actorUserId, AccountingOperation.ManageParties, branchId));
        var service = new SqlOpenItemService(context, _timeProvider);
        var originDate = new DateTimeOffset(DateTime.SpecifyKind(snapshot.CutoffUtc, DateTimeKind.Unspecified), TimeSpan.Zero);

        foreach (var net in nets)
        {
            var partyId = identities.Require(MigrationEntityKind.Party, net.Party.Id, net.Party.Code);
            var receivable = AlAmeenSourceBalanceAdapter.IsReceivable(net);
            var amount = receivable ? net.Amount : -net.Amount;
            if (amount <= 0m)
                continue;

            var reference = $@"{correlationKey}|{partyId:N}|{net.AccountId:N}|{net.CurrencyCode}";
            if (await context.OpenItems.AnyAsync(i => i.CompanyId == companyId && i.SourceReference == reference, cancellationToken).ConfigureAwait(false))
                continue;

            await service.CreateOpenItemAsync(
                companyId,
                new CreateOpenItemCommand(
                    branchId,
                    partyId,
                    receivable ? (byte)1 : (byte)2,
                    fiscalYearId,
                    null,
                    $@"AMIN-{net.Party.Code}",
                    originDate,
                    null,
                    net.CurrencyCode,
                    amount,
                    net.Rate,
                    (byte)OpenItemOriginType.MigrationOpenItem,
                    reference,
                    null,
                    net.AccountId),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<Guid> PostOpeningBalancesAsync(
        Guid companyId,
        Guid fiscalYearId,
        Guid branchId,
        Guid actorUserId,
        string baseCurrencyCode,
        string correlationKey,
        List<OpeningBalanceLineInput> openingLines,
        CancellationToken cancellationToken)
    {
        OpeningBalanceBatchDto draft;
        using (var draftContext = await _contextFactory.CreateDbContextAsync(companyId, fiscalYearId, cancellationToken).ConfigureAwait(false))
        {
            var targetYear = await draftContext.FiscalYears.FirstAsync(y => y.Id == fiscalYearId, cancellationToken).ConfigureAwait(false);
            draftContext.BindWriteScope(new AccountingWriteScope(companyId, fiscalYearId, actorUserId, AccountingOperation.ManageJournals, branchId));
            draft = await new SqlOpeningBalanceService(draftContext, _timeProvider).CreateDraftAsync(
                companyId,
                fiscalYearId,
                actorUserId,
                new CreateOpeningBalanceBatchCommand(
                    targetYear.RangeStart,
                    "AlAmeenCutover",
                    correlationKey,
                    correlationKey,
                    baseCurrencyCode,
                    baseCurrencyCode,
                    branchId,
                    correlationKey,
                    openingLines),
                cancellationToken).ConfigureAwait(false);
        }

        using (var approveContext = await _contextFactory.CreateDbContextAsync(companyId, fiscalYearId, cancellationToken).ConfigureAwait(false))
        {
            approveContext.BindWriteScope(new AccountingWriteScope(companyId, fiscalYearId, actorUserId, AccountingOperation.ApproveJournal, branchId));
            await new SqlOpeningBalanceService(approveContext, _timeProvider).ApproveAsync(companyId, draft.Id, actorUserId, "اعتماد رصيد افتتاحي مستورد", cancellationToken).ConfigureAwait(false);
        }

        using (var postContext = await _contextFactory.CreateDbContextAsync(companyId, fiscalYearId, cancellationToken).ConfigureAwait(false))
        {
            postContext.BindWriteScope(new AccountingWriteScope(companyId, fiscalYearId, actorUserId, AccountingOperation.PostJournal, branchId));
            var posted = await new SqlOpeningBalanceService(postContext, _timeProvider).PostAsync(companyId, draft.Id, actorUserId, "ترحيل رصيد افتتاحي مستورد", cancellationToken).ConfigureAwait(false);
            return posted.Id;
        }
    }

    private static string RequireCurrency(IdentityResolution identities, Guid lineCurrencyId, Guid voucherCurrencyId)
    {
        if (identities.CurrencyCodes.TryGetValue(lineCurrencyId == Guid.Empty ? voucherCurrencyId : lineCurrencyId, out var code))
            return code;
        throw new BusinessRuleException("migration.currency_unmapped", "حركة بعملة مصدر غير مبدولة إلى رمز عملة مفعّل في الهدف.");
    }
}
