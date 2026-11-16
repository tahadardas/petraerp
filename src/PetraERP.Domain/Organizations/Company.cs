using System.Collections.ObjectModel;
using PetraERP.Domain.FinancialValues;
using PetraERP.Domain.Primitives;

namespace PetraERP.Domain.Organizations;

public sealed class Company
{
    private Company(Guid id, EntityCode code, BilingualName name, CurrencyCode baseCurrency,
        ReadOnlyCollection<CurrencyCode> currencies, bool isActive)
    {
        Id = id;
        Code = code;
        Name = name;
        BaseCurrency = baseCurrency;
        EnabledCurrencies = currencies;
        IsActive = isActive;
    }

    public Guid Id { get; }
    public EntityCode Code { get; }
    public BilingualName Name { get; }
    public CurrencyCode BaseCurrency { get; }
    public IReadOnlyList<CurrencyCode> EnabledCurrencies { get; }
    public bool IsActive { get; }

    public static Company Create(EntityCode code, BilingualName name, CurrencyCode baseCurrency,
        IEnumerable<CurrencyCode> additionalCurrencies, bool isActive = true)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(baseCurrency);
        ArgumentNullException.ThrowIfNull(additionalCurrencies);
        var currencies = new HashSet<CurrencyCode> { baseCurrency };
        foreach (var currency in additionalCurrencies)
        {
            if (currency is null || !currencies.Add(currency))
            {
                throw new ArgumentException("Enabled currencies must be nonnull and unique, including base currency.", nameof(additionalCurrencies));
            }
        }

        return new Company(Guid.CreateVersion7(), code, name, baseCurrency,
            Array.AsReadOnly(currencies.OrderBy(static value => value.Value, StringComparer.Ordinal).ToArray()), isActive);
    }

    public static Company Reconstitute(Guid id, EntityCode code, BilingualName name, CurrencyCode baseCurrency,
        IEnumerable<CurrencyCode> enabledCurrencies, bool isActive)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(baseCurrency);
        ArgumentNullException.ThrowIfNull(enabledCurrencies);
        var currencies = new HashSet<CurrencyCode>(enabledCurrencies);
        currencies.Add(baseCurrency);
        return new Company(id, code, name, baseCurrency,
            Array.AsReadOnly(currencies.OrderBy(static value => value.Value, StringComparer.Ordinal).ToArray()), isActive);
    }

    public bool SupportsCurrency(CurrencyCode currency)
    {
        ArgumentNullException.ThrowIfNull(currency);
        return EnabledCurrencies.Contains(currency);
    }
}
