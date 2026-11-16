namespace PetraERP.Domain.AccountingCalendars;

public sealed record DateRange
{
    private DateRange(DateOnly start, DateOnly end)
    {
        Start = start;
        End = end;
    }

    public DateOnly Start { get; }
    public DateOnly End { get; }

    public static DateRange Create(DateOnly start, DateOnly end)
    {
        if (end < start)
        {
            throw new ArgumentException("End date cannot precede start date.", nameof(end));
        }

        return new DateRange(start, end);
    }

    public bool Contains(DateOnly date) => date >= Start && date <= End;
}
