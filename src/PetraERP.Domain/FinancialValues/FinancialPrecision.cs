namespace PetraERP.Domain.FinancialValues;

public static class FinancialPrecision
{
    public const int StoragePrecision = 19;
    public const int MoneyScale = 4;
    public const int QuantityScale = 6;
    public const int RateScale = 8;
    public const decimal MaximumMoney = 999999999999999.9999m;
    public const decimal MaximumQuantity = 9999999999999.999999m;
    public const decimal MaximumRate = 99999999999.99999999m;

    internal static void EnsureRepresentable(decimal value, decimal maximum, int scale, string parameterName)
    {
        if (value < -maximum || value > maximum || decimal.Round(value, scale, MidpointRounding.ToZero) != value)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Value exceeds its supported decimal range or scale.");
        }
    }
}
