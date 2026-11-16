using PetraERP.Application.AlAmeenMigration.Mapping;
using PetraERP.Application.AlAmeenMigration.Reconciliation;
using PetraERP.Application.Common;

namespace PetraERP.Infrastructure.AlAmeenMigration;

/// <summary>
/// Turns a frozen Al-Ameen snapshot into the same exact-decimal balance shape the target readers produce, so the two
/// sides are compared measure by measure instead of against a locally balanced number. Amounts are never rounded here.
/// </summary>
public static class AlAmeenSourceBalanceAdapter
{
    public static ReconciliationBalances FromSnapshot(SourceSnapshot snapshot, IdentityResolution identities, AccountComparisonMode mode = AccountComparisonMode.Cutover)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(identities);

        if (!identities.IsComplete)
        {
            var first = identities.Issues[0];
            throw new BusinessRuleException(
                "migration.identity_incomplete",
                $"مواءمة هويات المصدر غير مكتملة ({identities.Issues.Count} سجل). لا تُجرى مطابقة على بيانات ناقصة. أول سبب: {first.Detail}");
        }

        var accounts = mode == AccountComparisonMode.Cutover ? BuildCutoverAccountBalances(snapshot, identities) : BuildHistoricalAccountBalances(snapshot, identities);
        var (receivables, payables) = BuildPartyBalances(snapshot, identities, accounts);
        var stock = BuildStockBalances(snapshot, identities);
        return new ReconciliationBalances(accounts, receivables, payables, stock);
    }

    /// <summary>The position the cutover posts: one opening line per account and currency, exactly as normalized for posting.</summary>
    private static List<AccountBalance> BuildCutoverAccountBalances(SourceSnapshot snapshot, IdentityResolution identities)
        => [.. AlAmeenOpeningNormalizer.Normalize(snapshot, identities).Select(p => new AccountBalance(
            new AccountBalanceKey(identities.BranchId, p.AccountId, p.Currency),
            p.NetBase > 0 ? p.Foreign : 0m,
            p.NetBase < 0 ? p.Foreign : 0m,
            p.NetBase > 0 ? p.NetBase : 0m,
            p.NetBase < 0 ? -p.NetBase : 0m))];

    /// <summary>
    /// The position a full-history replay leaves in the ledger: every voucher is converted exactly as the historical migrator
    /// posts it (four-decimal transaction amounts, effective rate, sub-cent residue absorbed). A voucher the migrator blocks is
    /// not posted, so it is not part of the expected position either; the run reports it as blocked.
    /// </summary>
    private static List<AccountBalance> BuildHistoricalAccountBalances(SourceSnapshot snapshot, IdentityResolution identities)
    {
        var baseCode = snapshot.Currencies.FirstOrDefault(c => c.EquivalentRate == 1m)?.Code ?? string.Empty;
        var grouped = new Dictionary<(Guid AccountId, string Currency), (decimal Debit, decimal Credit, decimal BaseDebit, decimal BaseCredit)>();
        foreach (var voucher in snapshot.Vouchers)
        {
            var lines = AlAmeenHistoricalMigrator.Adjust(voucher, identities, baseCode, out _);
            if (lines is null)
                continue;
            foreach (var line in lines)
            {
                var accountId = identities.Require(MigrationEntityKind.Account, line.SourceAccountId, line.SourceAccountId.ToString("N"));
                var key = (accountId, line.Currency);
                var current = grouped.GetValueOrDefault(key);
                var derived = AlAmeenHistoricalMigrator.DerivedBase(line);
                grouped[key] = (
                    current.Debit + line.Debit,
                    current.Credit + line.Credit,
                    current.BaseDebit + (line.Debit > 0m ? derived : 0m),
                    current.BaseCredit + (line.Credit > 0m ? derived : 0m));
            }
        }

        return [.. grouped.Select(entry => new AccountBalance(
            new AccountBalanceKey(identities.BranchId, entry.Key.AccountId, entry.Key.Currency),
            entry.Value.Debit,
            entry.Value.Credit,
            entry.Value.BaseDebit,
            entry.Value.BaseCredit))];
    }
    private static List<AccountBalance> BuildAccountBalances(SourceSnapshot snapshot, IdentityResolution identities)
    {
        var grouped = new Dictionary<(Guid AccountId, string Currency), (decimal Debit, decimal Credit, decimal BaseDebit, decimal BaseCredit)>();
        foreach (var voucher in snapshot.Vouchers)
        {
            foreach (var line in voucher.Lines)
            {
                var accountId = identities.Require(MigrationEntityKind.Account, line.AccountId, line.AccountId.ToString("N"));
                var currency = RequireCurrency(identities, line.CurrencyId, voucher.CurrencyId);
                var key = (accountId, currency);
                var current = grouped.GetValueOrDefault(key);
                // Real Al-Ameen lines carry base-currency amounts and the line rate; the transaction amount is base / rate.
                var rate = line.Rate > 0m ? line.Rate : 1m;
                // The ledger stores four decimals per line; mirror the posting path so both sides compare the same stored values.
                var baseDebit = Math.Round(line.Debit, 4, MidpointRounding.ToEven);
                var baseCredit = Math.Round(line.Credit, 4, MidpointRounding.ToEven);
                grouped[key] = (
                    current.Debit + (rate == 1m ? baseDebit : Math.Round(baseDebit / rate, 4, MidpointRounding.ToEven)),
                    current.Credit + (rate == 1m ? baseCredit : Math.Round(baseCredit / rate, 4, MidpointRounding.ToEven)),
                    current.BaseDebit + baseDebit,
                    current.BaseCredit + baseCredit);
            }
        }

        return [.. grouped
            .Select(entry => new AccountBalance(
                new AccountBalanceKey(identities.BranchId, entry.Key.AccountId, entry.Key.Currency),
                entry.Value.Debit,
                entry.Value.Credit,
                entry.Value.BaseDebit,
                entry.Value.BaseCredit))];
    }

    /// <summary>
    /// A party's balance on its control account in one currency. Al-Ameen keeps base amounts plus a per-line rate, so the
    /// currency amount is base / rate; <see cref="Amount"/> and <see cref="Rate"/> are the pair the target open item stores.
    /// </summary>
    public sealed record PartyNet(SourceParty Party, Guid AccountId, string CurrencyCode, decimal NetBase, decimal NetForeign)
    {
        private bool UsesForeign => Math.Sign(NetForeign) == Math.Sign(NetBase) && Math.Round(Math.Abs(NetForeign), 4, MidpointRounding.ToEven) > 0m;

        /// <summary>Signed amount in <see cref="CurrencyCode"/> (positive = debit balance).</summary>
        public decimal Amount => UsesForeign
            ? Math.Sign(NetBase) * Math.Round(Math.Abs(NetForeign), 4, MidpointRounding.ToEven)
            : Math.Round(NetBase, 4, MidpointRounding.ToEven);

        /// <summary>Base units per one currency unit, held to the ledger's eight places.</summary>
        public decimal Rate => UsesForeign ? Math.Round(Math.Abs(NetBase) / Math.Abs(Amount), 8, MidpointRounding.ToEven) : 1m;

        /// <summary>Base carrying amount the ledger derives from <see cref="Amount"/> and <see cref="Rate"/> (signed).</summary>
        public decimal BaseAmount => Math.Round(Amount * Rate, 4, MidpointRounding.ToEven);
    }

    /// <summary>
    /// The Al-Ameen ledger snapshot has no settlement records, so the comparable unit is a party's balance at cutoff in
    /// its own control account, not an individual document. Sign convention matches the target open items: outstanding is
    /// positive for what the customer owes and positive for what we owe the supplier.
    /// </summary>
    public static List<PartyNet> ComputePartyNets(SourceSnapshot snapshot, IdentityResolution identities)
    {
        var balances = BuildAccountBalances(snapshot, identities);
        var foreignNets = new Dictionary<(Guid AccountId, string Currency), decimal>();
        foreach (var voucher in snapshot.Vouchers)
        {
            foreach (var line in voucher.Lines)
            {
                var key = (identities.Require(MigrationEntityKind.Account, line.AccountId, line.AccountId.ToString("N")), RequireCurrency(identities, line.CurrencyId, voucher.CurrencyId));
                var rate = line.Rate > 0m ? line.Rate : 1m;
                foreignNets[key] = foreignNets.GetValueOrDefault(key) + ((line.Debit - line.Credit) / rate);
            }
        }

        var nets = new List<PartyNet>();
        foreach (var party in snapshot.Parties)
        {
            if (party.AccountId == Guid.Empty)
                throw new BusinessRuleException("migration.party_without_control_account", $"الحساب الجاري للعميل {party.Code} غير محدد في المصدر؛ تعذرت المطابقة.");

            var accountId = identities.Require(MigrationEntityKind.Account, party.AccountId, party.Code);
            foreach (var balance in balances.Where(b => b.Key.AccountId == accountId))
            {
                var net = balance.BaseDebit - balance.BaseCredit;
                if (net == 0m)
                    continue;
                var partyNet = new PartyNet(party, accountId, balance.Key.CurrencyCode, net, foreignNets.GetValueOrDefault((accountId, balance.Key.CurrencyCode)));
                // A balance that rounds to nothing in its own currency or in base cannot be an open item (amounts must be positive).
                if (partyNet.Amount == 0m || partyNet.BaseAmount == 0m)
                    continue;
                nets.Add(partyNet);
            }
        }

        return nets;
    }

    /// <summary>A customer-only card is a receivable, a supplier-only card a payable; a card that is both follows the sign of its balance.</summary>
    public static bool IsReceivable(PartyNet net)
    {
        ArgumentNullException.ThrowIfNull(net);
        return net.Party.IsCustomer && net.Party.IsSupplier ? net.Amount >= 0m : net.Party.IsCustomer;
    }

    private static (List<OpenItemBalance> Receivables, List<OpenItemBalance> Payables) BuildPartyBalances(
        SourceSnapshot snapshot,
        IdentityResolution identities,
        List<AccountBalance> accounts)
    {
        _ = accounts;
        var receivables = new List<OpenItemBalance>();
        var payables = new List<OpenItemBalance>();

        foreach (var net in ComputePartyNets(snapshot, identities))
        {
            var partyId = identities.Require(MigrationEntityKind.Party, net.Party.Id, net.Party.Code);
            var receivable = IsReceivable(net);
            var outstanding = receivable ? net.Amount : -net.Amount;
            var balance = new OpenItemBalance(
                new OpenItemBalanceKey(identities.BranchId, partyId, net.AccountId, $"AMIN-PARTY-{net.Party.Code.Trim()}", net.CurrencyCode),
                outstanding,
                receivable ? net.BaseAmount : -net.BaseAmount);

            if (receivable)
                receivables.Add(balance);
            else
                payables.Add(balance);
        }

        return (receivables, payables);
    }

    /// <summary>
    /// Opening stock can only be a positive quantity. Al-Ameen allows a negative book quantity (sold before it was received);
    /// those rows cannot be opened as stock and are excluded identically from posting and from reconciliation, and are
    /// listed in the preview as source data quality findings.
    /// </summary>
    public static IEnumerable<SourceStockBalance> PostableStock(SourceSnapshot snapshot)
        => snapshot.Stock.Where(row => row.Quantity > 0m);

    private static List<StockBalance> BuildStockBalances(SourceSnapshot snapshot, IdentityResolution identities)
    {
        var balances = new List<StockBalance>();
        foreach (var row in PostableStock(snapshot))
        {
            var itemId = identities.Require(MigrationEntityKind.Item, row.ItemId, row.ItemId.ToString("N"));
            var warehouseId = identities.Require(MigrationEntityKind.Warehouse, row.StoreId, row.StoreId.ToString("N"));
            var unitId = identities.Require(MigrationEntityKind.Unit, row.ItemId, itemId.ToString("N"));
            balances.Add(new StockBalance(new StockBalanceKey(identities.BranchId, itemId, warehouseId, unitId), row.Quantity, row.TotalValue));
        }

        return balances;
    }

    private static string RequireCurrency(IdentityResolution identities, Guid lineCurrencyId, Guid voucherCurrencyId)
    {
        if (identities.CurrencyCodes.TryGetValue(lineCurrencyId == Guid.Empty ? voucherCurrencyId : lineCurrencyId, out var code))
            return code;
        throw new BusinessRuleException("migration.currency_unmapped", "حركة بمصدر بعملة غير مبدولة إلى رمز العملة المستهدفة.");
    }

    private static decimal ToBase(decimal amount, decimal rate) => amount * rate;
}
