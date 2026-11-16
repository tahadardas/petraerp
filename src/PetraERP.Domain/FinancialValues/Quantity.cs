using System.Globalization;

namespace PetraERP.Domain.FinancialValues;

public sealed record Quantity
{
    private Quantity(decimal value) => Value = value;

    public decimal Value { get; }

    public static Quantity Create(decimal value)
    {
        FinancialPrecision.EnsureRepresentable(value, FinancialPrecision.MaximumQuantity,
            FinancialPrecision.QuantityScale, nameof(value));
        return new Quantity(value);
    }

    public Quantity Add(Quantity other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Create(Value + other.Value);
    }

    public Quantity Subtract(Quantity other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Create(Value - other.Value);
    }

    public string ToInvariantString() => Value.ToString("F6", CultureInfo.InvariantCulture);
}
