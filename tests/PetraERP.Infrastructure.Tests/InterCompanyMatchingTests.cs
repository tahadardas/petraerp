using PetraERP.Application.InterCompany;
using PetraERP.Infrastructure.InterCompany;

namespace PetraERP.Infrastructure.Tests;

public sealed class InterCompanyMatchingTests
{
    private static InterCompanyMovement M(int day, decimal amount) => new(new DateOnly(2026, 1, day), Guid.NewGuid(), "x", amount);

    [Fact]
    public void PairsEqualAndOppositeAmountsOneToOneNearestDate()
    {
        var mine = new[] { M(5, 100m), M(20, 100m), M(9, 40m) };
        var theirs = new[] { M(21, -100m), M(6, -100m), M(10, -55m) };

        var result = SqlInterCompanyReconciliationService.Match(mine, theirs);

        Assert.Equal(2, result.Pairs.Count);
        Assert.All(result.Pairs, p => Assert.Equal(-p.ThisMovement.Amount, p.CounterpartyMovement.Amount));
        Assert.Equal(1, result.Pairs.Count(p => p.ThisMovement.Date.Day == 5 && p.CounterpartyMovement.Date.Day == 6));
        Assert.Equal(1, result.Pairs.Count(p => p.ThisMovement.Date.Day == 20 && p.CounterpartyMovement.Date.Day == 21));
        Assert.Equal([40m], result.UnmatchedThis.Select(m => m.Amount));
        Assert.Equal([-55m], result.UnmatchedCounterparty.Select(m => m.Amount));
        Assert.False(result.Truncated);
    }

    [Fact]
    public void ZeroAmountsNeverPair()
    {
        var result = SqlInterCompanyReconciliationService.Match([M(1, 0m)], [M(1, 0m)]);
        Assert.Empty(result.Pairs);
        Assert.Single(result.UnmatchedThis);
        Assert.Single(result.UnmatchedCounterparty);
    }
}
