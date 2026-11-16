namespace PetraERP.Infrastructure.AccountingCalendars;

/// <summary>Append-only audit of accounting period state changes (Domain <c>PeriodStateChange</c>).</summary>
internal sealed class PeriodAuditRow
{
    public Guid EventId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid FiscalYearId { get; set; }
    public Guid PeriodId { get; set; }
    public byte PreviousState { get; set; }
    public byte NewState { get; set; }
    public Guid ActorUserId { get; set; }
    public Guid CorrelationId { get; set; }
    public required string Reason { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
