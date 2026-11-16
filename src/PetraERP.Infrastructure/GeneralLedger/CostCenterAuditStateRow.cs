namespace PetraERP.Infrastructure.GeneralLedger;

internal sealed class CostCenterAuditStateRow
{
    internal const byte Before = 1;
    internal const byte After = 2;
    public Guid EventId { get; set; }
    public byte Stage { get; set; }
    public required string Code { get; set; }
    public required string NameArabic { get; set; }
    public string? NameEnglish { get; set; }
    public bool IsActive { get; set; }
}
