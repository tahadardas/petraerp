using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Parties.OpenItems;

public enum OpenItemPartyType : byte
{
    Customer = 1,
    Supplier = 2,
}

public enum OpenItemOriginType : byte
{
    CurrentYearInvoice = 1,
    PriorYearInvoiceRollover = 2,
    OpeningBalance = 3,
    MigrationOpenItem = 4,
}

public enum OpenItemState : byte
{
    Open = 1,
    PartiallySettled = 2,
    FullySettled = 3,
    Cancelled = 4,
}

public sealed class OpenItemRow : ITrackedStorageRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid BranchId { get; set; }
    public Guid PartyId { get; set; }
    public byte PartyType { get; set; } // 1=Customer, 2=Supplier
    public Guid? OriginFiscalYearId { get; set; }
    public Guid? OriginDocumentId { get; set; }
    public string? OriginDocumentNumber { get; set; }
    public DateTimeOffset OriginDocumentDateUtc { get; set; }
    public DateTimeOffset? DueDateUtc { get; set; }
    public required string CurrencyCode { get; set; }
    public decimal OriginalAmount { get; set; }
    public decimal SettledAmount { get; set; }
    public decimal OutstandingAmount { get; set; }
    public decimal ExchangeRate { get; set; }
    public decimal OriginalBaseCarryingAmount { get; set; }
    public decimal CurrentBaseCarryingAmount { get; set; }
    public byte OriginType { get; set; } // 1=CurrentYearInvoice, 2=PriorYearInvoiceRollover, 3=OpeningBalance, 4=MigrationOpenItem
    public string? SourceReference { get; set; }
    public Guid? RolloverBatchId { get; set; }
    public Guid AccountId { get; set; } // AR or AP control account
    public byte State { get; set; } = (byte)OpenItemState.Open;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
