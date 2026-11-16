namespace PetraERP.Infrastructure.Sales;

/// <summary>Durable invoice intent, retained independently of the atomic financial transaction.</summary>
internal sealed class SalesPostingRequestRow
{
    public Guid CompanyId { get; set; }
    public Guid BranchId { get; set; }
    public byte Operation { get; set; }
    public Guid IdempotencyKey { get; set; }
    public Guid InvoiceId { get; set; }
    public Guid ActorUserId { get; set; }
    public required byte[] ExpectedInvoiceVersion { get; set; }
    public required string Reason { get; set; }
    public required string FingerprintHex { get; set; }
    public DateTimeOffset RegisteredAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public Guid? OutcomeJournalId { get; set; }
}
