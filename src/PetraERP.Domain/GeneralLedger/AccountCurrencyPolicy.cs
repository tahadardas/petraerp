using PetraERP.Domain.Primitives;
using PetraERP.Domain.FinancialValues;
using PetraERP.Domain.Organizations;

namespace PetraERP.Domain.GeneralLedger;

public sealed record AccountCurrencyPolicy
{
    private AccountCurrencyPolicy(AccountCurrencyMode mode, CurrencyCode? fixedCurrency)
    {
        Mode = mode;
        FixedCurrency = fixedCurrency;
    }

    public AccountCurrencyMode Mode { get; }
    public CurrencyCode? FixedCurrency { get; }

    public static AccountCurrencyPolicy BaseOnly() => new(AccountCurrencyMode.BaseCurrencyOnly, null);
    public static AccountCurrencyPolicy AnyEnabled() => new(AccountCurrencyMode.AnyEnabledCurrency, null);

    public static AccountCurrencyPolicy Fixed(CurrencyCode currency)
    {
        ArgumentNullException.ThrowIfNull(currency);
        return new AccountCurrencyPolicy(AccountCurrencyMode.FixedCurrency, currency);
    }

    internal void EnsureConfiguredFor(Company company)
    {
        if (FixedCurrency is not null && !company.SupportsCurrency(FixedCurrency))
        {
            throw RuleViolation.Create(RuleViolation.CurrencyInvalid, "Fixed account currency must be enabled for its company.");
        }
    }

    internal void Validate(Company company, CurrencyCode currency)
    {
        if (!company.SupportsCurrency(currency)
            || (Mode == AccountCurrencyMode.BaseCurrencyOnly && currency != company.BaseCurrency)
            || (Mode == AccountCurrencyMode.FixedCurrency && currency != FixedCurrency))
        {
            throw RuleViolation.Create(RuleViolation.CurrencyInvalid, "Currency is not permitted by company and account rules.");
        }
    }
}
