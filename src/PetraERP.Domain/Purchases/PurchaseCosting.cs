using PetraERP.Domain.Primitives;

namespace PetraERP.Domain.Purchases;

public enum PurchaseInvoiceStatus
{
    Draft = 0,
    Approved = 1,
    Posted = 2,
    Reversed = 3,
}

/// <summary>Transaction-currency money of one purchase line: subtotal less discount is the stocked net; tax is recoverable input tax.</summary>
public sealed record PurchaseLineMoney(decimal SubTotal, decimal Discount, decimal Tax)
{
    public decimal Net => SubTotal - Discount;
    public decimal Total => Net + Tax;

    public const string LineInvalid = "purchases.line_invalid";

    /// <summary>Validates a line typed by the user: positive quantity, non-negative price, discount within the subtotal, tax ≥ 0.</summary>
    public static PurchaseLineMoney Create(decimal quantity, decimal unitCost, decimal discount, decimal tax)
    {
        if (quantity <= 0m || decimal.Round(quantity, 4) != quantity || unitCost < 0m || decimal.Round(unitCost, 4) != unitCost
            || discount < 0m || decimal.Round(discount, 4) != discount || tax < 0m || decimal.Round(tax, 4) != tax)
            throw RuleViolation.Create(LineInvalid, "Quantity must be positive and money non-negative with at most four decimals.");
        var subTotal = decimal.Round(quantity * unitCost, 4, MidpointRounding.AwayFromZero);
        if (discount > subTotal)
            throw RuleViolation.Create(LineInvalid, "A line discount cannot exceed the line subtotal.");
        return new(subTotal, discount, tax);
    }
}

/// <summary>Base-currency postings of a purchase or purchase return.</summary>
public sealed record PurchaseBaseAmounts(decimal Gross, decimal Tax, decimal Inventory, IReadOnlyList<decimal> LineCosts);

/// <summary>
/// Converts purchase money to base currency exactly as the ledger does (4 decimals, banker's rounding) for the settlement
/// (gross) and tax lines. Inventory is the remainder, so the journal always balances in base currency, and it is spread
/// over the lines by net amount with the last line taking the remainder, so stock movement cost equals the inventory posting.
/// </summary>
public static class PurchaseCosting
{
    public static decimal ToBase(decimal amount, decimal rate) => decimal.Round(amount * rate, 4, MidpointRounding.ToEven);

    public static PurchaseBaseAmounts Allocate(decimal rate, IReadOnlyList<PurchaseLineMoney> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (rate <= 0m) throw RuleViolation.Create(PurchaseLineMoney.LineInvalid, "The exchange rate must be positive.");
        if (lines.Count == 0) throw RuleViolation.Create(PurchaseLineMoney.LineInvalid, "A purchase needs at least one line.");
        var gross = ToBase(lines.Sum(l => l.Total), rate);
        var tax = ToBase(lines.Sum(l => l.Tax), rate);
        var inventory = gross - tax;
        var costs = new decimal[lines.Count];
        var assigned = 0m;
        for (var i = 0; i < lines.Count - 1; i++)
        {
            costs[i] = ToBase(lines[i].Net, rate);
            assigned += costs[i];
        }
        costs[^1] = inventory - assigned;
        if (costs.Any(c => c < 0m))
            throw RuleViolation.Create(PurchaseLineMoney.LineInvalid, "Line costs cannot be negative after base-currency allocation.");
        return new(gross, tax, inventory, costs);
    }
}
