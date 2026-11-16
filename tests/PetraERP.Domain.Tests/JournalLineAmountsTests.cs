using PetraERP.Domain.FinancialValues;
using PetraERP.Domain.GeneralLedger;

namespace PetraERP.Domain.Tests;

public sealed class JournalLineAmountsTests
{
    private static readonly CurrencyCode Currency = CurrencyCode.Create("USD");

    [Theory]
    [InlineData("0", "0")]
    [InlineData("1", "1")]
    [InlineData("-1", "0")]
    [InlineData("0", "-1")]
    public void InvalidSidesAreRejected(string debitText, string creditText)
    {
        var debit = decimal.Parse(debitText, System.Globalization.CultureInfo.InvariantCulture);
        var credit = decimal.Parse(creditText, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Throws<ArgumentException>(() => JournalLineAmounts.Create(Money.Create(debit, Currency), Money.Create(credit, Currency)));
    }

    [Fact]
    public void ExactlyOnePositiveSideIsRequiredInTheSameCurrency()
    {
        var debit = JournalLineAmounts.Create(Money.Create(0.0001m, Currency), Money.Create(0m, Currency));
        var credit = JournalLineAmounts.Create(Money.Create(0m, Currency), Money.Create(0.0001m, Currency));
        Assert.Equal(0.0001m, debit.Debit.Amount);
        Assert.Equal(0.0001m, credit.Credit.Amount);
        Assert.Throws<ArgumentException>(() => JournalLineAmounts.Create(debit.Debit, Money.Create(0m, CurrencyCode.Create("SYP"))));
        Assert.Throws<ArgumentNullException>(() => JournalLineAmounts.Create(null!, credit.Credit));
    }
}
