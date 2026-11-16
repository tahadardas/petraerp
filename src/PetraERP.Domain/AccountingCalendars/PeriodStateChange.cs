namespace PetraERP.Domain.AccountingCalendars;

/// <summary>Audit intent to commit with the state change in the application transaction.</summary>
public sealed class PeriodStateChange
{
    internal PeriodStateChange(AccountingPeriod period, AccountingPeriodState next, PeriodTransitionContext context)
    {
        EventId = Guid.CreateVersion7();
        CompanyId = period.CompanyId;
        FiscalYearId = period.FiscalYearId;
        PeriodId = period.Id;
        PreviousState = period.State;
        NewState = next;
        ActorUserId = context.ActorUserId;
        CorrelationId = context.CorrelationId;
        Reason = context.Reason;
        OccurredAtUtc = context.OccurredAtUtc;
    }

    public Guid EventId { get; }
    public Guid CompanyId { get; }
    public Guid FiscalYearId { get; }
    public Guid PeriodId { get; }
    public AccountingPeriodState PreviousState { get; }
    public AccountingPeriodState NewState { get; }
    public Guid ActorUserId { get; }
    public Guid CorrelationId { get; }
    public string Reason { get; }
    public DateTimeOffset OccurredAtUtc { get; }
}
