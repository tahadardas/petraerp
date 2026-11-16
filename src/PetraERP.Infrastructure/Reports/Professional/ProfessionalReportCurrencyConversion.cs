using PetraERP.Application.Common;
using PetraERP.Application.Reports.Professional;
using PetraERP.Infrastructure.Currencies;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Reports.Professional;

/// <summary>
/// Presents a finished report in another currency through the exchange-rate register. Register rates are base units per
/// one unit of a currency (base = 1), so the cross factor from X to Y is rate(X) / rate(Y) for any pair of enabled currencies.
/// One factor, effective on the rate date, is applied to every amount so debit, credit, opening, closing and running
/// balances stay arithmetically consistent; the rate used is stated in the report notes.
/// </summary>
internal static class ProfessionalReportCurrencyConversion
{
    private const int MoneyScale = 4;

    internal static async Task<ProfessionalReportExecutionResponse> ApplyAsync(FinancialDbContext context, Guid companyId, string baseCurrency,
        ProfessionalReportRequest request, ProfessionalReportExecutionResponse report, CancellationToken cancellationToken)
    {
        var target = request.DisplayCurrencyCode;
        if (target is null || string.Equals(target, report.CurrencyCode, StringComparison.Ordinal)) return report;

        await CompanyCurrencies.RequireEnabledAsync(context, companyId, baseCurrency, target, "reporting", cancellationToken).ConfigureAwait(false);
        var date = request.RateDate ?? request.To;
        var source = report.CurrencyCode;
        var sourceRate = await RateAsync(context, companyId, baseCurrency, source, date, cancellationToken).ConfigureAwait(false);
        var targetRate = await RateAsync(context, companyId, baseCurrency, target, date, cancellationToken).ConfigureAwait(false);
        var factor = sourceRate / targetRate;

        decimal M(decimal value) => Math.Round(value * factor, MoneyScale, MidpointRounding.AwayFromZero);
        var rows = report.Rows.Select(r => r with
        {
            CurrencyCode = target,
            Debit = M(r.Debit), Credit = M(r.Credit), ActualBalance = M(r.ActualBalance), DisplayBalance = M(r.DisplayBalance),
            Opening = M(r.Opening), OpeningDebit = M(r.OpeningDebit), OpeningCredit = M(r.OpeningCredit),
            Closing = M(r.Closing), ClosingDebit = M(r.ClosingDebit), ClosingCredit = M(r.ClosingCredit),
        }).ToArray();
        var s = report.Summary;
        var summary = s with
        {
            ActualOpening = M(s.ActualOpening), ActualPeriodDebit = M(s.ActualPeriodDebit), ActualPeriodCredit = M(s.ActualPeriodCredit),
            ActualClosing = M(s.ActualClosing), FilteredDebit = M(s.FilteredDebit), FilteredCredit = M(s.FilteredCredit),
            DisplayClosing = M(s.DisplayClosing), OpeningDebit = M(s.OpeningDebit), OpeningCredit = M(s.OpeningCredit),
            ClosingDebit = M(s.ClosingDebit), ClosingCredit = M(s.ClosingCredit),
        };
        var note = $"عرض مُحوَّل من {source} إلى {target} بسعر تقاطعي {factor:0.##########} (نشرة الأسعار السارية بتاريخ {date:yyyy-MM-dd}) / " +
                   $"Converted from {source} to {target} at cross rate {factor:0.##########} (register rates effective {date:yyyy-MM-dd}).";
        return report with { CurrencyCode = target, Rows = rows, Summary = summary, Notes = [.. report.Notes, note] };
    }

    /// <summary>Base units per one unit of the currency on the date; the base itself is 1; a missing register entry is an explicit error.</summary>
    private static async Task<decimal> RateAsync(FinancialDbContext context, Guid companyId, string baseCurrency, string currency, DateOnly date,
        CancellationToken cancellationToken)
    {
        if (currency == baseCurrency) return 1m;
        var row = await CompanyCurrencies.EffectiveRateAsync(context, companyId, baseCurrency, currency, date, cancellationToken).ConfigureAwait(false)
            ?? throw new BusinessRuleException("reporting.rate_missing",
                $"لا يوجد سعر صرف مسجل للعملة {currency} في نشرة الأسعار حتى {date:yyyy-MM-dd}؛ سجّله أولاً ثم أعد التحويل. / No register rate for {currency} on or before {date:yyyy-MM-dd}; register it first.", "displayCurrencyCode");
        return row.Rate;
    }
}
