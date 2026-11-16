namespace PetraERP.Infrastructure.GeneralLedger;

internal readonly record struct TrialBalanceAccountAmounts(Guid AccountId, decimal Opening, decimal Debit, decimal Credit);

internal readonly record struct TrialBalanceTotals(
    decimal OpeningDebit, decimal OpeningCredit, decimal PeriodDebit, decimal PeriodCredit, decimal EndingDebit, decimal EndingCredit);

internal static class TrialBalanceTotalsCalculator
{
    internal static TrialBalanceTotals Calculate(IEnumerable<TrialBalanceAccountAmounts> accounts, IReadOnlySet<Guid> includedAccountIds)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(includedAccountIds);
        decimal openingDebit = 0, openingCredit = 0, periodDebit = 0, periodCredit = 0, endingDebit = 0, endingCredit = 0;
        foreach (var account in accounts)
        {
            if (!includedAccountIds.Contains(account.AccountId)) continue;
            var ending = account.Opening + account.Debit - account.Credit;
            openingDebit += account.Opening > 0m ? account.Opening : 0m;
            openingCredit += account.Opening < 0m ? -account.Opening : 0m;
            periodDebit += account.Debit;
            periodCredit += account.Credit;
            endingDebit += ending > 0m ? ending : 0m;
            endingCredit += ending < 0m ? -ending : 0m;
        }
        return new(openingDebit, openingCredit, periodDebit, periodCredit, endingDebit, endingCredit);
    }
}
