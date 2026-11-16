using System.Globalization;

namespace PetraERP.Domain.FinancialValues;

public sealed record Money
{
    private Money(decimal amount, CurrencyCode currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }
    public CurrencyCode Currency { get; }

    public static Money Create(decimal amount, CurrencyCode currency)
    {
        ArgumentNullException.ThrowIfNull(currency);
        FinancialPrecision.EnsureRepresentable(amount, FinancialPrecision.MaximumMoney,
            FinancialPrecision.MoneyScale, nameof(amount));
        return new Money(amount, currency);
    }

    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return Create(Amount + other.Amount, Currency);
    }

    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return Create(Amount - other.Amount, Currency);
    }

    public string ToInvariantString() => Amount.ToString("F4", CultureInfo.InvariantCulture);

    private void EnsureSameCurrency(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (Currency != other.Currency)
        {
            throw new InvalidOperationException("Amounts in different currencies cannot be added or subtracted.");
        }
    }
}
