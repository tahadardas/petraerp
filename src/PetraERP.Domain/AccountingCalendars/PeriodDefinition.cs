using PetraERP.Domain.Primitives;

namespace PetraERP.Domain.AccountingCalendars;

public sealed record PeriodDefinition
{
    private PeriodDefinition(EntityCode code, DateRange range)
    {
        Code = code;
        Range = range;
    }

    public EntityCode Code { get; }
    public DateRange Range { get; }

    public static PeriodDefinition Create(EntityCode code, DateRange range)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(range);
        return new PeriodDefinition(code, range);
    }
}
