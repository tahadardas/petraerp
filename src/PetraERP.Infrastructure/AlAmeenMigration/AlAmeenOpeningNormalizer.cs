using PetraERP.Application.AlAmeenMigration.Mapping;
using PetraERP.Application.Common;

namespace PetraERP.Infrastructure.AlAmeenMigration;

/// <summary>One opening position per account and currency, in the exact form the ledger will store it.</summary>
internal sealed record NormalizedOpeningLine(Guid AccountId, string Currency, decimal NetBase, decimal Foreign, decimal Rate);

/// <summary>
/// Turns the Al-Ameen ledger into the opening position that PetraERP can hold exactly. Al-Ameen keeps base-currency
/// amounts on every line plus the line's exchange rate, so the currency amount is base / rate. The ledger stores a
/// currency amount (four places) and a rate (eight places) and derives the base amount from them, so the base is
/// recomputed that way here. Both the cutover posting and the reconciliation source side call this one function, which
/// is what lets reconciliation compare exact decimals instead of tolerances.
/// </summary>
internal static class AlAmeenOpeningNormalizer
{
    public static List<NormalizedOpeningLine> Normalize(SourceSnapshot snapshot, IdentityResolution identities)
    {
        var netByAccountCurrency = new Dictionary<(Guid AccountId, string Currency), (decimal BaseDebit, decimal BaseCredit, decimal ForeignDebit, decimal ForeignCredit)>();
        foreach (var voucher in snapshot.Vouchers)
        {
            foreach (var line in voucher.Lines)
            {
                var accountId = identities.Require(MigrationEntityKind.Account, line.AccountId, line.AccountId.ToString("N"));
                var currency = RequireCurrency(identities, line.CurrencyId, voucher.CurrencyId);
                var rate = line.Rate > 0m ? line.Rate : 1m;
                var current = netByAccountCurrency.GetValueOrDefault((accountId, currency));
                netByAccountCurrency[(accountId, currency)] = (
                    current.BaseDebit + line.Debit,
                    current.BaseCredit + line.Credit,
                    current.ForeignDebit + (line.Debit / rate),
                    current.ForeignCredit + (line.Credit / rate));
            }
        }

        var pending = new List<NormalizedOpeningLine>();
        foreach (var entry in netByAccountCurrency.OrderBy(e => e.Key.AccountId).ThenBy(e => e.Key.Currency, StringComparer.Ordinal))
        {
            var netBase = Math.Round(entry.Value.BaseDebit - entry.Value.BaseCredit, 4, MidpointRounding.ToEven);
            if (netBase == 0m)
                continue;

            var netForeign = entry.Value.ForeignDebit - entry.Value.ForeignCredit;
            var roundedForeign = Math.Round(Math.Abs(netForeign), 4, MidpointRounding.ToEven);
            var sameSide = netForeign != 0m && Math.Sign(netForeign) == Math.Sign(netBase) && roundedForeign > 0m;
            if (sameSide)
            {
                var rate = Math.Round(Math.Abs(netBase) / roundedForeign, 8, MidpointRounding.ToEven);
                var derived = Math.Round(roundedForeign * rate, 4, MidpointRounding.ToEven);
                pending.Add(new NormalizedOpeningLine(entry.Key.AccountId, entry.Key.Currency, netBase > 0 ? derived : -derived, roundedForeign, rate));
            }
            else
            {
                pending.Add(new NormalizedOpeningLine(entry.Key.AccountId, entry.Key.Currency, netBase, Math.Abs(netBase), 1m));
            }
        }

        // Rounding each net can leave a residue of a few ten-thousandths (and rate rounding a little more on foreign lines).
        // A residue within the tolerance is absorbed by the largest base-currency line on the short side so the batch balances
        // exactly; anything larger is a genuine imbalance and is left for the caller to reject.
        var residue = pending.Sum(p => p.NetBase);
        var tolerance = Math.Max(0.01m, pending.Count(p => p.Rate != 1m));
        if (residue != 0m && Math.Abs(residue) <= tolerance)
        {
            var index = pending
                .Select((p, i) => (p, i))
                .Where(x => x.p.Rate == 1m && Math.Sign(x.p.NetBase) == -Math.Sign(residue))
                .OrderByDescending(x => Math.Abs(x.p.NetBase))
                .Select(x => x.i)
                .DefaultIfEmpty(-1)
                .First();
            if (index >= 0)
            {
                var p = pending[index];
                var adjusted = p.NetBase - residue;
                pending[index] = p with { NetBase = adjusted, Foreign = Math.Abs(adjusted) };
            }
        }

        return pending;
    }

    private static string RequireCurrency(IdentityResolution identities, Guid lineCurrencyId, Guid voucherCurrencyId)
    {
        if (identities.CurrencyCodes.TryGetValue(lineCurrencyId == Guid.Empty ? voucherCurrencyId : lineCurrencyId, out var code))
            return code;
        throw new BusinessRuleException("migration.currency_unmapped", "عملة القيد غير معرّفة في بيانات المصدر.");
    }
}