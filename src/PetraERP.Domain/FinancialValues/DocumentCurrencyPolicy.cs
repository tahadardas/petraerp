using PetraERP.Domain.Primitives;

namespace PetraERP.Domain.FinancialValues;

/// <summary>
/// Pure currency decisions shared by every financial document. No currency is ever assumed by the code: the document
/// currency comes from the request, then the party default, then the pattern default, then the company base currency
/// (which the operator chose at setup). The rate actually used is stored on the document, so a later register change
/// never rewrites history.
/// </summary>
public static class DocumentCurrencyPolicy
{
    public const string CurrencyInvalid = "currency.invalid";
    public const string CurrencyNotEnabled = "currency.not_enabled";
    public const string RateInvalid = "currency.rate_invalid";
    public const string RateMissing = "currency.rate_missing";
    public const string BaseLocked = "currency.base_locked";
    public const string FxMappingMissing = "currency.fx_mapping_missing";

    /// <summary>Where the stored document rate came from.</summary>
    public enum RateOrigin { BaseCurrency = 0, Document = 1, Register = 2 }

    /// <summary>Normalizes a code to three uppercase Latin letters, or null when empty.</summary>
    public static string? Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var value = code.Trim().ToUpperInvariant();
        if (value.Length != 3 || value.Any(static c => c is < 'A' or > 'Z'))
            throw RuleViolation.Create(CurrencyInvalid, "A currency code is three Latin letters (for example SYP or USD).");
        return value;
    }

    /// <summary>Document, then party, then pattern, then the company base currency.</summary>
    public static string ResolveCode(string? requested, string? partyDefault, string? patternDefault, string baseCurrency) =>
        Normalize(requested) ?? Normalize(partyDefault) ?? Normalize(patternDefault)
        ?? Normalize(baseCurrency) ?? throw RuleViolation.Create(CurrencyInvalid, "The company has no base currency.");

    /// <summary>
    /// Validates an explicit rate (target base units per one document-currency unit). The base currency always uses 1;
    /// returns null when a foreign-currency document sent no rate and the register must supply it.
    /// </summary>
    public static decimal? ExplicitRate(string currency, string baseCurrency, decimal? requested)
    {
        if (currency == baseCurrency)
        {
            if (requested is { } same && same != 1m)
                throw RuleViolation.Create(RateInvalid, "The base currency always uses an exchange rate of 1.");
            return 1m;
        }
        if (requested is not { } rate) return null;
        RequireRate(rate);
        return rate;
    }

    public static void RequireRate(decimal rate)
    {
        if (rate <= 0m || rate > FinancialPrecision.MaximumRate || decimal.Round(rate, FinancialPrecision.RateScale) != rate)
            throw RuleViolation.Create(RateInvalid, "The exchange rate must be positive with at most eight decimals.");
    }

    /// <summary>Base value of a document-currency amount at a stored rate, rounded the same way as the ledger lines.</summary>
    public static decimal ToBase(decimal amount, decimal rate) => decimal.Round(amount * rate, FinancialPrecision.MoneyScale, MidpointRounding.ToEven);

    /// <summary>
    /// Realized exchange difference in base currency when an amount booked at <paramref name="bookedRate"/> is settled at
    /// <paramref name="settlementRate"/>. Positive is a gain for a receivable collected (more base received than booked).
    /// </summary>
    public static decimal RealizedReceivableDifference(decimal amount, decimal bookedRate, decimal settlementRate) =>
        ToBase(amount, settlementRate) - ToBase(amount, bookedRate);
}
