using PetraERP.Application.Common;
using PetraERP.Application.Workspaces;
using PetraERP.Domain.AccountingCalendars;
using PetraERP.Infrastructure.AccountingCalendars;

namespace PetraERP.Infrastructure.Workspaces;

internal static class FiscalYearProvisioningRules
{
    internal static void Validate(ProvisionFiscalYearInput input, bool initialCompanySeed)
    {
        if (input.StartDate > input.EndDate || input.EndDate.DayNumber - input.StartDate.DayNumber > 731)
            throw new BusinessRuleException("fiscal_year.range_invalid", "A fiscal year requires an ordered range of at most two calendar years.");
        // Existing calendar and legacy naming use the year in which the fiscal range starts.
        if (input.YearNumber != input.StartDate.Year)
            throw new BusinessRuleException("fiscal_year.number_invalid", "YearNumber must equal the calendar year of StartDate.");
        if (string.IsNullOrWhiteSpace(input.YearCode) || input.YearCode.Trim().Length > 64)
            throw new BusinessRuleException("fiscal_year.code_invalid", "A fiscal year code of at most 64 characters is required.");
        if (!initialCompanySeed && (input.CopyChartOfAccounts || input.CopyWarehouses || input.CopyPatterns) && input.SourceFiscalYearId is null)
            throw new BusinessRuleException("fiscal_year.source_required", "Select the registered prior fiscal year; master-data copy never rolls financial balances forward.");
    }

    internal static IEnumerable<AccountingPeriodRow> Periods(Guid companyId, Guid fiscalYearId, DateOnly start, DateOnly end)
    {
        if (start > end) throw new ArgumentException("Start must not follow end.", nameof(start));
        var periodStart = start;
        var index = 1;
        while (periodStart <= end)
        {
            // Anchored monthly anniversaries avoid cumulative drift after February or a 30-day month.
            var monthsRemaining = (DateOnly.MaxValue.Year - start.Year) * 12 + 12 - start.Month;
            var candidateEnd = index > monthsRemaining ? end : start.AddMonths(index).AddDays(-1);
            var periodEnd = candidateEnd > end ? end : candidateEnd;
            yield return new AccountingPeriodRow
            {
                Id = Guid.NewGuid(), CompanyId = companyId, FiscalYearId = fiscalYearId,
                Code = $"P{index:D2}", RangeStart = periodStart, RangeEnd = periodEnd,
                State = (byte)AccountingPeriodState.Open,
            };
            if (periodEnd == end) yield break;
            periodStart = periodEnd.AddDays(1);
            index++;
        }
    }
}
