using PetraERP.Domain.FinancialValues;

namespace PetraERP.Domain.GeneralLedger;

/// <summary>One journal line's nonnegative sides in a single explicit currency.</summary>
public sealed record JournalLineAmounts
{
    private JournalLineAmounts(Money debit, Money credit)
    {
        Debit = debit;
        Credit = credit;
    }

    public Money Debit { get; }
    public Money Credit { get; }

    public static JournalLineAmounts Create(Money debit, Money credit)
    {
        ArgumentNullException.ThrowIfNull(debit);
        ArgumentNullException.ThrowIfNull(credit);
        if (debit.Currency != credit.Currency)
        {
            throw new ArgumentException("Journal sides must use the same currency.", nameof(credit));
        }

        if (debit.Amount < 0 || credit.Amount < 0 || (debit.Amount > 0) == (credit.Amount > 0))
        {
            throw new ArgumentException("Exactly one journal side must be positive and the other zero.", nameof(debit));
        }

        return new JournalLineAmounts(debit, credit);
    }
}
