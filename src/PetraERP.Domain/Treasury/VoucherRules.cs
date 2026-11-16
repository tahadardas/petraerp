using PetraERP.Domain.Primitives;

namespace PetraERP.Domain.Treasury;

public enum VoucherKind
{
    /// <summary>Money received: debit cash/bank, credit the customer's receivable.</summary>
    Receipt = 1,

    /// <summary>Money paid: credit cash/bank, debit supplier payables or expense accounts.</summary>
    Payment = 2,
}

public enum VoucherStatus
{
    Draft = 0,
    Approved = 1,
    Posted = 2,
    Reversed = 3,
}

/// <summary>Which settlement role of the voucher pattern supplies the cash/bank account.</summary>
public enum SettlementMethod
{
    Cash = 1,
    Bank = 2,
}

/// <summary>A requested allocation of receipt money to one invoice, with that invoice's outstanding balance.</summary>
public sealed record AllocationRequest(Guid InvoiceId, decimal Amount, decimal Outstanding);

/// <summary>
/// Server rules for voucher money. Amounts are positive with at most four decimals. A receipt may allocate at
/// most its amount, and each invoice at most its outstanding balance; any unallocated remainder is an explicit
/// customer advance credited to the same receivable. Payment distribution lines must sum exactly to the amount.
/// </summary>
public static class VoucherRules
{
    public const string AmountInvalid = "treasury.amount_invalid";
    public const string OverAllocation = "treasury.over_allocation";
    public const string InvoiceOverAllocated = "treasury.invoice_over_allocated";
    public const string AllocationDuplicate = "treasury.allocation_duplicate";
    public const string LinesUnbalanced = "treasury.lines_unbalanced";

    public static void RequireAmount(decimal amount)
    {
        if (amount <= 0m || decimal.Round(amount, 4) != amount || amount >= 1_000_000_000_000_000m)
            throw RuleViolation.Create(AmountInvalid, "Voucher amounts must be positive with at most four decimals.");
    }

    /// <summary>Validates receipt allocations and returns the unallocated advance.</summary>
    public static decimal Allocate(decimal voucherAmount, IReadOnlyCollection<AllocationRequest> allocations)
    {
        ArgumentNullException.ThrowIfNull(allocations);
        RequireAmount(voucherAmount);
        if (allocations.Select(a => a.InvoiceId).Distinct().Count() != allocations.Count)
            throw RuleViolation.Create(AllocationDuplicate, "An invoice can be allocated once per voucher.");
        foreach (var allocation in allocations)
        {
            RequireAmount(allocation.Amount);
            if (allocation.Amount > allocation.Outstanding)
                throw RuleViolation.Create(InvoiceOverAllocated, "An allocation exceeds the invoice's outstanding balance.");
        }

        var allocated = allocations.Sum(a => a.Amount);
        if (allocated > voucherAmount)
            throw RuleViolation.Create(OverAllocation, "Allocations exceed the voucher amount.");
        return voucherAmount - allocated;
    }

    public static void RequireBalancedLines(decimal voucherAmount, IReadOnlyCollection<decimal> lineAmounts)
    {
        ArgumentNullException.ThrowIfNull(lineAmounts);
        RequireAmount(voucherAmount);
        if (lineAmounts.Count == 0)
            throw RuleViolation.Create(LinesUnbalanced, "A payment needs at least one distribution line.");
        foreach (var amount in lineAmounts) RequireAmount(amount);
        if (lineAmounts.Sum() != voucherAmount)
            throw RuleViolation.Create(LinesUnbalanced, "Payment distribution lines must equal the voucher amount.");
    }
}
