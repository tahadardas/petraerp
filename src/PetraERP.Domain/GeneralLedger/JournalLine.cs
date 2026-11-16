using PetraERP.Domain.FinancialValues;
using PetraERP.Domain.Organizations;
using PetraERP.Domain.Primitives;

namespace PetraERP.Domain.GeneralLedger;

public sealed record JournalLine
{
    public const int MaximumDescriptionLength = 1000;

    private JournalLine(Guid id, Guid companyId, Guid accountId, Guid? costCenterId,
        JournalLineAmounts transactionAmounts, JournalLineAmounts baseAmounts,
        ExchangeRate rate, MidpointRounding rounding, string description, Guid? reversesLineId, Guid? journalId)
    {
        Id = id;
        CompanyId = companyId;
        AccountId = accountId;
        CostCenterId = costCenterId;
        TransactionAmounts = transactionAmounts;
        BaseAmounts = baseAmounts;
        Rate = rate;
        Rounding = rounding;
        Description = description;
        ReversesLineId = reversesLineId;
        JournalId = journalId;
    }

    public Guid Id { get; }
    public Guid CompanyId { get; }
    public Guid AccountId { get; }
    public Guid? CostCenterId { get; }
    public JournalLineAmounts TransactionAmounts { get; }
    public JournalLineAmounts BaseAmounts { get; }
    public ExchangeRate Rate { get; }
    public MidpointRounding Rounding { get; }
    public string Description { get; }
    public Guid? ReversesLineId { get; }
    public Guid? JournalId { get; }

    public static JournalLine Create(Company company, Guid accountId, Guid? costCenterId,
        JournalLineAmounts transactionAmounts, ExchangeRate rate, MidpointRounding rounding, string description)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(transactionAmounts);
        ArgumentNullException.ThrowIfNull(rate);
        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("Account ID must be nonempty.", nameof(accountId));
        }

        if (costCenterId == Guid.Empty)
        {
            throw new ArgumentException("Cost-center ID must be nonempty when supplied.", nameof(costCenterId));
        }

        if (rate.SourceCurrency != transactionAmounts.Debit.Currency || rate.TargetCurrency != company.BaseCurrency)
        {
            throw new ArgumentException("Rate must convert transaction currency into the company base currency.", nameof(rate));
        }

        var baseAmounts = JournalLineAmounts.Create(rate.Convert(transactionAmounts.Debit, rounding),
            rate.Convert(transactionAmounts.Credit, rounding));
        return new JournalLine(Guid.CreateVersion7(), company.Id, accountId, costCenterId, transactionAmounts,
            baseAmounts, rate, rounding,
            DomainText.Required(description, MaximumDescriptionLength, nameof(description), multiline: true), null, null);
    }

    public static JournalLine Reconstitute(Guid id, Guid companyId, Guid accountId, Guid? costCenterId,
        JournalLineAmounts transactionAmounts, JournalLineAmounts baseAmounts,
        ExchangeRate rate, MidpointRounding rounding, string description, Guid? reversesLineId, Guid? journalId)
    {
        ArgumentNullException.ThrowIfNull(transactionAmounts);
        ArgumentNullException.ThrowIfNull(baseAmounts);
        ArgumentNullException.ThrowIfNull(rate);
        return new JournalLine(id, companyId, accountId, costCenterId, transactionAmounts,
            baseAmounts, rate, rounding, description, reversesLineId, journalId);
    }

    internal JournalLine Reverse() => new(Guid.CreateVersion7(), CompanyId, AccountId, CostCenterId,
        JournalLineAmounts.Create(TransactionAmounts.Credit, TransactionAmounts.Debit),
        JournalLineAmounts.Create(BaseAmounts.Credit, BaseAmounts.Debit), Rate, Rounding, Description, Id, null);

    internal JournalLine AssignTo(Guid journalId)
    {
        if (JournalId == journalId)
        {
            return this;
        }

        if (JournalId is not null)
        {
            throw new ArgumentException("An owned line cannot be moved to another journal.", nameof(journalId));
        }

        return new JournalLine(Guid.CreateVersion7(), CompanyId, AccountId, CostCenterId,
            TransactionAmounts, BaseAmounts, Rate, Rounding, Description, ReversesLineId, journalId);
    }

    internal bool IsExactReversalOf(JournalLine original) =>
        ReversesLineId == original.Id && CompanyId == original.CompanyId && AccountId == original.AccountId
        && CostCenterId == original.CostCenterId && Rate == original.Rate && Rounding == original.Rounding
        && TransactionAmounts.Debit == original.TransactionAmounts.Credit
        && TransactionAmounts.Credit == original.TransactionAmounts.Debit
        && BaseAmounts.Debit == original.BaseAmounts.Credit && BaseAmounts.Credit == original.BaseAmounts.Debit;
}
