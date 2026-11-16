using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Domain.FinancialValues;
using PetraERP.Domain.Primitives;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Currencies;

/// <summary>
/// The one server-side resolution of a document's currency and exchange rate, used by sales, purchases and treasury:
/// currency from the request, else the party default, else the pattern default, else the company base currency; the
/// currency must be the base or an enabled, active company currency; the rate is the base 1, an explicit valid rate, or
/// the register rate effective on the document date. The result is stored on the document and never re-read.
/// </summary>
internal static class CompanyCurrencies
{
    internal sealed record Resolution(string CurrencyCode, decimal Rate, string BaseCurrencyCode, DocumentCurrencyPolicy.RateOrigin Origin);

    /// <param name="module">Error-code prefix of the calling module (for example "sales"), kept for existing clients.</param>
    internal static async Task<Resolution> ResolveAsync(FinancialDbContext context, Guid companyId, string module, string? requestedCurrency,
        decimal? requestedRate, DateOnly documentDate, string? partyDefault, string? patternDefault, CancellationToken cancellationToken)
    {
        var baseCurrency = await context.Companies.AsNoTracking().Where(c => c.Id == companyId).Select(c => c.BaseCurrencyCode)
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? throw new ResourceNotFoundException("Company");
        var currency = Rule(() => DocumentCurrencyPolicy.ResolveCode(requestedCurrency, partyDefault, patternDefault, baseCurrency),
            module + ".currency_invalid", "currencyCode");
        await RequireEnabledAsync(context, companyId, baseCurrency, currency, module, cancellationToken).ConfigureAwait(false);

        var explicitRate = Rule(() => DocumentCurrencyPolicy.ExplicitRate(currency, baseCurrency, requestedRate), module + ".rate_invalid", "exchangeRate");
        if (explicitRate is { } rate)
            return new(currency, rate, baseCurrency,
                currency == baseCurrency ? DocumentCurrencyPolicy.RateOrigin.BaseCurrency : DocumentCurrencyPolicy.RateOrigin.Document);

        var registered = await EffectiveRateAsync(context, companyId, baseCurrency, currency, documentDate, cancellationToken).ConfigureAwait(false)
            ?? throw new BusinessRuleException(DocumentCurrencyPolicy.RateMissing,
                $"لا يوجد سعر صرف سارٍ للعملة {currency} بتاريخ {documentDate:yyyy-MM-dd}؛ أدخله أو سجله أولاً / No exchange rate for {currency} is registered on or before {documentDate:yyyy-MM-dd}; enter the rate or register it first.", "exchangeRate");
        return new(currency, registered.Rate, baseCurrency, DocumentCurrencyPolicy.RateOrigin.Register);
    }

    /// <summary>The currency must be the base or an enabled company currency, and active in the catalog.</summary>
    internal static async Task RequireEnabledAsync(FinancialDbContext context, Guid companyId, string baseCurrency, string currency, string module,
        CancellationToken cancellationToken)
    {
        var enabled = currency == baseCurrency || await context.CompanyAdditionalCurrencies
            .AnyAsync(c => c.CompanyId == companyId && c.CurrencyCode == currency, cancellationToken).ConfigureAwait(false);
        if (!enabled || !await context.Currencies.AnyAsync(c => c.Code == currency && c.IsActive, cancellationToken).ConfigureAwait(false))
            throw new BusinessRuleException(module + ".currency_invalid", $"العملة {currency} غير متاحة لهذه الشركة / The currency {currency} is not enabled for this company.", "currencyCode");
    }

    /// <summary>The latest register entry effective on or before the date (a later correction of the same day wins).</summary>
    internal static Task<ExchangeRateRow?> EffectiveRateAsync(FinancialDbContext context, Guid companyId, string baseCurrency, string currency,
        DateOnly date, CancellationToken cancellationToken) =>
        context.ExchangeRates.AsNoTracking()
            .Where(r => r.CompanyId == companyId && r.CurrencyCode == currency && r.BaseCurrencyCode == baseCurrency && r.EffectiveDate <= date)
            .OrderByDescending(r => r.EffectiveDate).ThenByDescending(r => r.CreatedAtUtc).ThenByDescending(r => r.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private static T Rule<T>(Func<T> action, string code, string field)
    {
        try { return action(); }
        catch (InvalidOperationException exception) when (RuleViolation.CodeOf(exception) is not null)
        {
            throw new BusinessRuleException(code, exception.Message, field);
        }
    }
}
