namespace PetraERP.Infrastructure.GeneralLedger;

/// <summary>Append-only events; identity survives any future removal of the mutable cost center.</summary>
internal sealed class CostCenterAuditRow
{
    public Guid EventId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid CostCenterId { get; set; }
    public byte Action { get; set; }
    public Guid ActorUserId { get; set; }
    public Guid? WorkingBranchId { get; set; }
    public Guid CorrelationId { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
}
