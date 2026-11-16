using PetraERP.Infrastructure.GeneralLedger;

namespace PetraERP.Infrastructure.Tests;

public sealed class TrialBalanceTotalsTests
{
    [Fact]
    public void IncludesEachAccountsOwnActivityOnceIncludingAnAccountWithChildren()
    {
        var parent = Guid.NewGuid();
        var child = Guid.NewGuid();
        var outside = Guid.NewGuid();
        var values = new[]
        {
            new TrialBalanceAccountAmounts(parent, 20m, 100m, 0m),
            new TrialBalanceAccountAmounts(child, -10m, 0m, 60m),
            new TrialBalanceAccountAmounts(outside, 0m, 999m, 0m),
        };

        var totals = TrialBalanceTotalsCalculator.Calculate(values, new HashSet<Guid> { parent, child });

        Assert.Equal(20m, totals.OpeningDebit);
        Assert.Equal(10m, totals.OpeningCredit);
        Assert.Equal(100m, totals.PeriodDebit);
        Assert.Equal(60m, totals.PeriodCredit);
        Assert.Equal(120m, totals.EndingDebit);
        Assert.Equal(70m, totals.EndingCredit);
    }
}
