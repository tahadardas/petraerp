namespace PetraERP.Domain.FinancialValues;

/// <summary>A currency-code shape; configured membership is an application rule.</summary>
public sealed record CurrencyCode
{
    private CurrencyCode(string value) => Value = value;

    public string Value { get; }

    public static CurrencyCode Create(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length != 3 || value.Any(static character => character is < 'A' or > 'Z'))
        {
            throw new ArgumentException("Currency code must contain three uppercase Latin letters.", nameof(value));
        }

        return new CurrencyCode(value);
    }

    public override string ToString() => Value;
}
