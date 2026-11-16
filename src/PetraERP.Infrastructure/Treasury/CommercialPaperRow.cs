using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Parties;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Treasury;

internal sealed class CommercialPaperRow : ITrackedStorageRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid BranchId { get; set; }
    public byte PaperKind { get; set; }
    public string PaperNumber { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string? BranchName { get; set; }
    public string DrawerName { get; set; } = string.Empty;
    public string? PayeeName { get; set; }
    public DateOnly IssueDate { get; set; }
    public DateOnly DueDate { get; set; }
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal ExchangeRate { get; set; }
    public decimal BaseAmount { get; set; }
    public byte Status { get; set; }
    public DateOnly StatusDate { get; set; }

    public Guid? PartyId { get; set; }
    public Guid? PortfolioAccountId { get; set; }
    public Guid? IntermediaryAccountId { get; set; }
    public Guid? BankAccountId { get; set; }
    public Guid? EndorsedPartyId { get; set; }
    public Guid? VoucherId { get; set; }
    public Guid? LastJournalEntryId { get; set; }

    public string? Notes { get; set; }

    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    // Navigations
    public PartyRow? Party { get; set; }
    public PartyRow? EndorsedParty { get; set; }
    public AccountRow? PortfolioAccount { get; set; }
    public AccountRow? IntermediaryAccount { get; set; }
    public AccountRow? BankAccount { get; set; }
    public JournalEntryRow? LastJournalEntry { get; set; }
    public VoucherRow? Voucher { get; set; }
}
