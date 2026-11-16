using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Infrastructure.Currencies;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Reports.Professional;

/// <summary>
/// Values report movements in a display currency the way Al-Ameen does: every movement uses the exchange-rate register entry
/// effective on ITS OWN date (not one rate for the whole report). Register rates are base units per one unit of a currency
/// (base = 1), so converting X to Y on a date is amount × rate(X) / rate(Y). A movement that is already in the display
/// currency keeps its exact amount. A currency with no register entry on or before the movement date is an explicit error.
/// </summary>
internal sealed class DisplayValuation
{
    private const int MoneyScale = 4;
    private readonly string baseCurrency;
    private readonly Dictionary<string, List<(DateOnly Date, decimal Rate)>> rates;

    private DisplayValuation(string code, string baseCurrency, Dictionary<string, List<(DateOnly Date, decimal Rate)>> rates)
    {
        Code = code; this.baseCurrency = baseCurrency; this.rates = rates;
    }

    /// <summary>The currency movements are presented in.</summary>
    public string Code { get; }

    /// <summary>Null when no display currency was requested; otherwise validates it and loads the register entries up to <paramref name="to"/>.</summary>
    internal static async Task<DisplayValuation?> CreateAsync(FinancialDbContext context, Guid companyId, string baseCurrency, string? displayCode,
        IEnumerable<string> sourceCurrencies, DateOnly to, CancellationToken cancellationToken)
    {
        if (displayCode is null) return null;
        await CompanyCurrencies.RequireEnabledAsync(context, companyId, baseCurrency, displayCode, "reporting", cancellationToken).ConfigureAwait(false);
        var codes = sourceCurrencies.Append(displayCode).Where(c => c != baseCurrency).Distinct().ToArray();
        var entries = codes.Length == 0 ? [] : await context.ExchangeRates.AsNoTracking()
            .Where(r => r.CompanyId == companyId && r.BaseCurrencyCode == baseCurrency && codes.Contains(r.CurrencyCode) && r.EffectiveDate <= to)
            .OrderBy(r => r.EffectiveDate).ThenBy(r => r.CreatedAtUtc).ThenBy(r => r.Id)
            .Select(r => new { r.CurrencyCode, r.EffectiveDate, r.Rate }).ToListAsync(cancellationToken).ConfigureAwait(false);
        var byCurrency = entries.GroupBy(e => e.CurrencyCode).ToDictionary(g => g.Key, g => g.Select(e => (e.EffectiveDate, e.Rate)).ToList());
        return new DisplayValuation(displayCode, baseCurrency, byCurrency);
    }

    /// <summary>Base units per one unit of the currency on the date.</summary>
    internal decimal Rate(string currency, DateOnly date)
    {
        if (currency == baseCurrency) return 1m;
        decimal? found = null;
        if (rates.TryGetValue(currency, out var entries))
            foreach (var entry in entries) { if (entry.Date <= date) found = entry.Rate; else break; }
        return found ?? throw new BusinessRuleException("reporting.rate_missing",
            $"لا يوجد سعر صرف مسجل للعملة {currency} في نشرة الأسعار حتى {date:yyyy-MM-dd}؛ سجّله ثم أعد التحويل. / No register rate for {currency} on or before {date:yyyy-MM-dd}.", "displayCurrencyCode");
    }

    /// <summary>Converts an amount between two currencies at the rates effective on the date; the same currency is returned unchanged.</summary>
    internal decimal Convert(decimal amount, string from, string to, DateOnly date) =>
        from == to || amount == 0m ? amount : Math.Round(amount * Rate(from, date) / Rate(to, date), MoneyScale, MidpointRounding.AwayFromZero);

    /// <summary>A ledger line: exact transaction amounts when it was in the display currency, otherwise its base amounts converted on its date.</summary>
    internal (decimal Debit, decimal Credit) Value(DateOnly date, string transactionCurrency, decimal transactionDebit, decimal transactionCredit,
        decimal baseDebit, decimal baseCredit) =>
        transactionCurrency == Code ? (transactionDebit, transactionCredit)
            : (Convert(baseDebit, baseCurrency, Code, date), Convert(baseCredit, baseCurrency, Code, date));
}
