namespace PetraERP.Infrastructure.Security;

/// <summary>
/// Append-only security audit trail. A database trigger rejects UPDATE and DELETE. Rows carry
/// no foreign keys so evidence never depends on the continued existence of other records.
/// Never stores passwords, tokens or secrets.
/// </summary>
internal sealed class SecurityAuditEventRow
{
    public Guid Id { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public byte EventType { get; set; }
    public Guid? UserId { get; set; }
    public string? UserNameSnapshot { get; set; }
    public Guid? ActorUserId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? CompanyId { get; set; }
    public Guid? BranchId { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? CorrelationId { get; set; }
    public string? Reason { get; set; }
    public string? Details { get; set; }
}
