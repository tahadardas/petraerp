namespace PetraERP.Domain.AccountingCalendars;

public sealed class FiscalPeriodChange
{
    internal FiscalPeriodChange(FiscalYear updatedYear, PeriodStateChange change)
    {
        UpdatedYear = updatedYear;
        Change = change;
    }

    public FiscalYear UpdatedYear { get; }
    public PeriodStateChange Change { get; }
}
