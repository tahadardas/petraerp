namespace PetraERP.Infrastructure.Sales;

/// <summary>Immutable evidence of a successful warning acknowledgement or privileged credit override.</summary>
internal sealed class SalesCreditAuditRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid BranchId { get; set; }
    public Guid InvoiceId { get; set; }
    public Guid PartyId { get; set; }
    public Guid ActorUserId { get; set; }
    public Guid IdempotencyKey { get; set; }
    public int PolicyKind { get; set; }
    public bool WasCreditBlocked { get; set; }
    public bool IsOverride { get; set; }
    public decimal CreditLimitBase { get; set; }
    public decimal ExposureBeforeBase { get; set; }
    public decimal InvoiceBaseAmount { get; set; }
    public decimal ExposureAfterBase { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
}
