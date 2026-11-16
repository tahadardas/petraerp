using PetraERP.Domain.Primitives;

namespace PetraERP.Domain.AccountingCalendars;

public sealed class AccountingPeriod
{
    private AccountingPeriod(Guid id, Guid companyId, Guid fiscalYearId, EntityCode code,
        DateRange range, AccountingPeriodState state)
    {
        Id = id;
        CompanyId = companyId;
        FiscalYearId = fiscalYearId;
        Code = code;
        Range = range;
        State = state;
    }

    public Guid Id { get; }
    public Guid CompanyId { get; }
    public Guid FiscalYearId { get; }
    public EntityCode Code { get; }
    public DateRange Range { get; }
    public AccountingPeriodState State { get; }

    internal static AccountingPeriod Create(Guid companyId, Guid fiscalYearId, PeriodDefinition definition) =>
        new(Guid.CreateVersion7(), companyId, fiscalYearId, definition.Code, definition.Range, AccountingPeriodState.Open);

    public static AccountingPeriod Reconstitute(Guid id, Guid companyId, Guid fiscalYearId, EntityCode code,
        DateRange range, AccountingPeriodState state)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(range);
        return new AccountingPeriod(id, companyId, fiscalYearId, code, range, state);
    }

    internal AccountingPeriod WithState(AccountingPeriodState state) =>
        new(Id, CompanyId, FiscalYearId, Code, Range, state);
}
