using PetraERP.Domain.Primitives;
using PetraERP.Domain.AccountingCalendars;
using PetraERP.Domain.Organizations;

namespace PetraERP.Domain.GeneralLedger;

/// <summary>Resolved snapshots; must be loaded under the eventual posting transaction and authorization scope.</summary>
public sealed class JournalValidationContext
{
    private readonly Dictionary<Guid, Account> accounts;
    private readonly Dictionary<Guid, CostCenter> centers;

    private JournalValidationContext(Company company, BranchScope scope, FiscalYear year,
        Dictionary<Guid, Account> accounts, Dictionary<Guid, CostCenter> centers)
    {
        Company = company;
        Scope = scope;
        Year = year;
        this.accounts = accounts;
        this.centers = centers;
    }

    public Company Company { get; }
    public BranchScope Scope { get; }
    public FiscalYear Year { get; }

    public static JournalValidationContext Create(Company company, BranchScope scope, FiscalYear year,
        IEnumerable<Account> accounts, IEnumerable<CostCenter> centers)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(year);
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(centers);
        if (scope.CompanyId != company.Id || year.CompanyId != company.Id)
        {
            throw new ArgumentException("All validation context entities must belong to the company.", nameof(scope));
        }

        var accountSnapshot = new Dictionary<Guid, Account>();
        foreach (var account in accounts)
        {
            if (account is null || account.CompanyId != company.Id || !accountSnapshot.TryAdd(account.Id, account))
            {
                throw new ArgumentException("Account context contains a missing, foreign or duplicate account.", nameof(accounts));
            }
        }

        var centerSnapshot = new Dictionary<Guid, CostCenter>();
        foreach (var center in centers)
        {
            if (center is null || center.CompanyId != company.Id || !centerSnapshot.TryAdd(center.Id, center))
            {
                throw new ArgumentException("Cost-center context contains a missing, foreign or duplicate center.", nameof(centers));
            }
        }

        return new JournalValidationContext(company, scope, year, accountSnapshot, centerSnapshot);
    }

    internal AccountingPeriod Validate(JournalEntry entry)
    {
        if (entry.CompanyId != Company.Id || entry.BranchId != Scope.BranchId)
        {
            throw new ArgumentException("Journal does not belong to the supplied company and branch.", nameof(entry));
        }

        if (!Company.IsActive)
        {
            throw RuleViolation.Create(RuleViolation.ScopeInactive, "Inactive company cannot validate or post journals.");
        }

        var period = Year.GetOpenPeriodForPosting(Scope, entry.AccountingDate);
        foreach (var line in entry.Lines)
        {
            if (line.CompanyId != Company.Id || line.JournalId != entry.Id)
            {
                throw new InvalidOperationException("Journal line ownership is inconsistent.");
            }

            if (!accounts.TryGetValue(line.AccountId, out var account))
            {
                throw RuleViolation.Create(RuleViolation.AccountInvalid, "Journal account is missing from the resolved company context.");
            }

            CostCenter? center = null;
            if (line.CostCenterId is { } centerId && !centers.TryGetValue(centerId, out center))
            {
                throw RuleViolation.Create(RuleViolation.CostCenterInvalid, "Journal cost center is missing from the resolved company context.");
            }

            if (line.BaseAmounts.Debit.Currency != Company.BaseCurrency)
            {
                throw RuleViolation.Create(RuleViolation.CurrencyInvalid, "Journal base currency does not match the company.");
            }

            account.ValidatePostingRules(Company, line.TransactionAmounts.Debit.Currency, center);
        }

        _ = BalancedJournalAmounts.Create(entry.Lines.Select(static line => line.BaseAmounts));
        return period;
    }
}
