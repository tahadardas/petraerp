using System.Globalization;

namespace PetraERP.Domain.FinancialValues;

/// <summary>Target currency units per one source currency unit.</summary>
public sealed record ExchangeRate
{
    private ExchangeRate(CurrencyCode sourceCurrency, CurrencyCode targetCurrency, decimal value)
    {
        SourceCurrency = sourceCurrency;
        TargetCurrency = targetCurrency;
        Value = value;
    }

    public CurrencyCode SourceCurrency { get; }
    public CurrencyCode TargetCurrency { get; }
    public decimal Value { get; }

    public static ExchangeRate Create(CurrencyCode sourceCurrency, CurrencyCode targetCurrency, decimal value)
    {
        ArgumentNullException.ThrowIfNull(sourceCurrency);
        ArgumentNullException.ThrowIfNull(targetCurrency);
        FinancialPrecision.EnsureRepresentable(value, FinancialPrecision.MaximumRate,
            FinancialPrecision.RateScale, nameof(value));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
        if (sourceCurrency == targetCurrency && value != 1m)
        {
            throw new ArgumentException("A same-currency exchange rate must equal one.", nameof(value));
        }

        return new ExchangeRate(sourceCurrency, targetCurrency, value);
    }

    public Money Convert(Money source, MidpointRounding rounding)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Currency != SourceCurrency)
        {
            throw new ArgumentException("Money currency does not match the rate source currency.", nameof(source));
        }

        return Money.Create(ExactDecimalProduct.ToMoney(source.Amount, Value, rounding), TargetCurrency);
    }

    public string ToInvariantString() => Value.ToString("F8", CultureInfo.InvariantCulture);
}
