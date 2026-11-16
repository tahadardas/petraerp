namespace PetraERP.Domain.Inventory;

/// <summary>Posting-order WAC at decimal(19,4). Exhausting a balance takes its exact remaining value.</summary>
public static class WeightedAverageCosting
{
    public const decimal MaximumStoredValue = 999999999999999.9999m;

    public static bool TryValue(decimal stockQuantity, decimal stockValue, decimal quantity, out decimal value)
    {
        value = 0m;
        if (stockQuantity <= 0 || stockValue < 0 || quantity <= 0 || stockQuantity > MaximumStoredValue
            || stockValue > MaximumStoredValue || quantity > MaximumStoredValue || decimal.Round(stockQuantity, 4) != stockQuantity
            || decimal.Round(stockValue, 4) != stockValue || decimal.Round(quantity, 4) != quantity)
            return false;
        try
        {
            var computed = quantity == stockQuantity ? stockValue
                : decimal.Round(stockValue / stockQuantity * quantity, 4, MidpointRounding.ToEven);
            if (computed < 0 || computed > MaximumStoredValue) return false;
            value = computed;
            return true;
        }
        catch (OverflowException) { return false; }
    }
}
