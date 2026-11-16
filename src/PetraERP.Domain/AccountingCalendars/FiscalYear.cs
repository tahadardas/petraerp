using System.Collections.ObjectModel;
using PetraERP.Domain.Organizations;
using PetraERP.Domain.Primitives;

namespace PetraERP.Domain.AccountingCalendars;

public sealed class FiscalYear
{
    private FiscalYear(Guid id, Guid companyId, EntityCode code, DateRange range, ReadOnlyCollection<AccountingPeriod> periods)
    {
        Id = id;
        CompanyId = companyId;
        Code = code;
        Range = range;
        Periods = periods;
    }

    public Guid Id { get; }
    public Guid CompanyId { get; }
    public EntityCode Code { get; }
    public DateRange Range { get; }
    public IReadOnlyList<AccountingPeriod> Periods { get; }

    public static FiscalYear Create(Company company, EntityCode code, DateRange range, IEnumerable<PeriodDefinition> periods)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(range);
        ArgumentNullException.ThrowIfNull(periods);
        var definitions = periods.ToArray();
        if (definitions.Length == 0 || definitions.Any(static period => period is null))
        {
            throw new ArgumentException("At least one nonnull period definition is required.", nameof(periods));
        }

        definitions = definitions.OrderBy(static period => period.Range.Start).ToArray();
        var codes = new HashSet<EntityCode>();
        for (var index = 0; index < definitions.Length; index++)
        {
            var current = definitions[index];
            if (!codes.Add(current.Code) || !range.Contains(current.Range.Start) || !range.Contains(current.Range.End)
                || (index == 0 && current.Range.Start != range.Start)
                || (index > 0 && current.Range.Start.DayNumber != definitions[index - 1].Range.End.DayNumber + 1))
            {
                throw new ArgumentException("Periods must have unique codes and cover the year without gaps or overlaps.", nameof(periods));
            }
        }

        if (definitions[^1].Range.End != range.End)
        {
            throw new ArgumentException("Periods must cover the entire fiscal year.", nameof(periods));
        }

        var id = Guid.CreateVersion7();
        return new FiscalYear(id, company.Id, code, range,
            Array.AsReadOnly(definitions.Select(definition => AccountingPeriod.Create(company.Id, id, definition)).ToArray()));
    }

    public static FiscalYear Reconstitute(Guid id, Guid companyId, EntityCode code, DateRange range, IEnumerable<AccountingPeriod> periods)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(range);
        ArgumentNullException.ThrowIfNull(periods);
        return new FiscalYear(id, companyId, code, range, Array.AsReadOnly(periods.ToArray()));
    }

    public AccountingPeriod GetOpenPeriodForPosting(BranchScope scope, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.CompanyId != CompanyId)
        {
            throw new ArgumentException("Fiscal year belongs to another company.", nameof(scope));
        }

        if (!Range.Contains(date))
        {
            throw new ArgumentOutOfRangeException(nameof(date), "Accounting date is outside the fiscal year.");
        }

        var period = Periods.Single(period => period.Range.Contains(date));
        if (period.State != AccountingPeriodState.Open)
        {
            throw RuleViolation.Create(RuleViolation.PeriodClosed, "Posting to a closed or soft-closed period is forbidden.");
        }

        return period;
    }

    public FiscalPeriodChange SoftClosePeriod(Company company, Guid periodId, PeriodTransitionContext context) =>
        Transition(company, periodId, context, AccountingPeriodState.SoftClosed);

    public FiscalPeriodChange ClosePeriod(Company company, Guid periodId, PeriodTransitionContext context) =>
        Transition(company, periodId, context, AccountingPeriodState.Closed);

    public FiscalPeriodChange ReopenPeriod(Company company, Guid periodId, PeriodTransitionContext context) =>
        Transition(company, periodId, context, AccountingPeriodState.Open);

    private FiscalPeriodChange Transition(Company company, Guid periodId, PeriodTransitionContext context, AccountingPeriodState next)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(context);
        if (company.Id != CompanyId)
        {
            throw new ArgumentException("Fiscal year belongs to another company.", nameof(company));
        }

        if (!company.IsActive)
        {
            throw RuleViolation.Create(RuleViolation.ScopeInactive, "Inactive company cannot change fiscal periods.");
        }

        var period = Periods.SingleOrDefault(period => period.Id == periodId)
                     ?? throw new ArgumentException("Period does not belong to this fiscal year.", nameof(periodId));
        if (period.State == next || (next == AccountingPeriodState.SoftClosed && period.State != AccountingPeriodState.Open))
        {
            throw RuleViolation.Create(RuleViolation.PeriodTransitionInvalid, "The requested period state transition is not allowed.");
        }

        var changed = Periods.Select(current => current.Id == periodId ? current.WithState(next) : current).ToArray();
        return new FiscalPeriodChange(new FiscalYear(Id, CompanyId, Code, Range, Array.AsReadOnly(changed)),
            new PeriodStateChange(period, next, context));
    }
}
