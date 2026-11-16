using PetraERP.Domain.Primitives;

namespace PetraERP.Domain.Sales;

/// <summary>Money components of one sold invoice line, or of the portions already returned from it.</summary>
public sealed record SalesLineAmounts(decimal SubTotal, decimal Discount, decimal Tax, decimal Pump, decimal Transport, decimal Cost)
{
    public static SalesLineAmounts Zero { get; } = new(0m, 0m, 0m, 0m, 0m, 0m);

    public decimal LineTotal => SubTotal - Discount + Tax + Pump + Transport;

    public SalesLineAmounts Add(SalesLineAmounts other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new(SubTotal + other.SubTotal, Discount + other.Discount, Tax + other.Tax,
            Pump + other.Pump, Transport + other.Transport, Cost + other.Cost);
    }

    public SalesLineAmounts Subtract(SalesLineAmounts other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new(SubTotal - other.SubTotal, Discount - other.Discount, Tax - other.Tax,
            Pump - other.Pump, Transport - other.Transport, Cost - other.Cost);
    }
}

/// <summary>
/// Allocates a partial return of one posted invoice line at the line's original price, discount, tax,
/// additions and stock cost. Each partial return is proportional and truncated to 4 decimals, so earlier
/// returns can never overdraw the line; the return that exhausts the sold quantity takes the exact remainder,
/// so all returns together equal the original exactly.
/// </summary>
public static class SalesReturnAllocation
{
    public const string OverReturn = "sales.over_return";
    public const string QuantityInvalid = "sales.return_quantity_invalid";

    public static SalesLineAmounts Allocate(decimal soldQuantity, SalesLineAmounts sold,
        decimal previouslyReturnedQuantity, SalesLineAmounts previouslyReturned, decimal returnQuantity)
    {
        ArgumentNullException.ThrowIfNull(sold);
        ArgumentNullException.ThrowIfNull(previouslyReturned);
        if (soldQuantity <= 0m || previouslyReturnedQuantity < 0m)
            throw RuleViolation.Create(QuantityInvalid, "Sold quantity must be positive and returned quantity non-negative.");
        if (returnQuantity <= 0m || decimal.Round(returnQuantity, 4) != returnQuantity)
            throw RuleViolation.Create(QuantityInvalid, "Return quantity must be positive with at most four decimals.");
        var remaining = soldQuantity - previouslyReturnedQuantity;
        if (returnQuantity > remaining)
            throw RuleViolation.Create(OverReturn, "The return exceeds the quantity still returnable on the original line.");

        if (returnQuantity == remaining)
        {
            return sold.Subtract(previouslyReturned);
        }

        decimal Part(decimal amount) => decimal.Round(amount * returnQuantity / soldQuantity, 4, MidpointRounding.ToZero);
        return new(Part(sold.SubTotal), Part(sold.Discount), Part(sold.Tax), Part(sold.Pump), Part(sold.Transport), Part(sold.Cost));
    }
}
