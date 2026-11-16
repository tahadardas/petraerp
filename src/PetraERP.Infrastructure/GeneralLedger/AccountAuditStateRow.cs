namespace PetraERP.Infrastructure.GeneralLedger;

/// <summary>Immutable business-state snapshot. Historical account/parent/currency values are not live foreign keys.</summary>
internal sealed class AccountAuditStateRow
{
    internal const byte Before = 1;
    internal const byte After = 2;
    public Guid EventId { get; set; }
    public byte Stage { get; set; }
    public required string Code { get; set; }
    public required string NameArabic { get; set; }
    public string? NameEnglish { get; set; }
    public byte AccountType { get; set; }
    public bool PostingAllowed { get; set; }
    public byte CostCenterPolicy { get; set; }
    public byte CurrencyMode { get; set; }
    public string? FixedCurrencyCode { get; set; }
    public Guid? ParentId { get; set; }
    public bool IsActive { get; set; }
}
