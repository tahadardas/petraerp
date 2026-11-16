using PetraERP.Domain.Primitives;
using System.Collections.ObjectModel;
using PetraERP.Domain.FinancialValues;

namespace PetraERP.Domain.GeneralLedger;

/// <summary>An immutable, numerically balanced snapshot; this is not a posted journal.</summary>
public sealed class BalancedJournalAmounts
{
    private BalancedJournalAmounts(ReadOnlyCollection<JournalLineAmounts> lines, Money total)
    {
        Lines = lines;
        Total = total;
    }

    public IReadOnlyList<JournalLineAmounts> Lines { get; }
    public Money Total { get; }

    public static BalancedJournalAmounts Create(IEnumerable<JournalLineAmounts> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var snapshot = lines.ToArray();
        if (snapshot.Length < 2 || snapshot.Any(static line => line is null))
        {
            throw new ArgumentException("A journal needs at least two valid lines.", nameof(lines));
        }

        var currency = snapshot[0].Debit.Currency;
        var debit = 0m;
        var credit = 0m;
        foreach (var line in snapshot)
        {
            if (line.Debit.Currency != currency)
            {
                throw new ArgumentException("Balance comparison requires a single currency denomination.", nameof(lines));
            }

            debit += line.Debit.Amount;
            credit += line.Credit.Amount;
        }

        if (debit != credit)
        {
            throw RuleViolation.Tag(new ArgumentException("Journal debit and credit totals must match exactly.", nameof(lines)), RuleViolation.JournalUnbalanced);
        }

        return new BalancedJournalAmounts(Array.AsReadOnly(snapshot), Money.Create(debit, currency));
    }
}
