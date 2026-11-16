namespace PetraERP.Infrastructure.GeneralLedger;

/// <summary>
/// Append-only audit of chart-of-accounts changes. It keeps identity fields because the account row may no longer exist,
/// so <see cref="AccountId"/> and <see cref="ParentId"/> carry no foreign key.
/// </summary>
internal sealed class AccountAuditRow
{
    internal const byte Deleted = 1;

    public Guid EventId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid AccountId { get; set; }
    public byte Action { get; set; }
    public required string Code { get; set; }
    public required string NameArabic { get; set; }
    public string? NameEnglish { get; set; }
    public byte AccountType { get; set; }
    public Guid? ParentId { get; set; }
    public Guid ActorUserId { get; set; }
    public Guid? WorkingBranchId { get; set; }
    public Guid CorrelationId { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
