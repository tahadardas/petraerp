using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.Reports;
using PetraERP.Infrastructure.GeneralLedger;

namespace PetraERP.Infrastructure.Tests;

public sealed class TrialBalanceDisplayTests
{
    [Fact]
    public void ExchangeRateRequiresCurrencyAndCurrencyRequiresValidRate()
    {
        Assert.Equal(1m, SqlLedgerQueryService.ValidateDisplay(new TrialBalanceOptions()));
        Assert.Equal(1m, SqlLedgerQueryService.ValidateDisplay(new TrialBalanceOptions { DisplayCurrencyCode = "EUR", DisplayRate = 1m }));
        Assert.Throws<BusinessRuleException>(() => SqlLedgerQueryService.ValidateDisplay(new TrialBalanceOptions { DisplayRate = 2m }));
        Assert.Throws<BusinessRuleException>(() => SqlLedgerQueryService.ValidateDisplay(new TrialBalanceOptions { DisplayCurrencyCode = "EUR" }));
        Assert.Throws<BusinessRuleException>(() => SqlLedgerQueryService.ValidateDisplay(new TrialBalanceOptions { DisplayCurrencyCode = "E1R", DisplayRate = 1m }));
        Assert.Throws<BusinessRuleException>(() => SqlLedgerQueryService.ValidateDisplay(new TrialBalanceOptions { DisplayCurrencyCode = "EUR", DisplayRate = 0m }));
    }
}
