using System.ComponentModel.DataAnnotations;

namespace PetraERP.Contracts.Ledger;

/// <summary>Marks a journal as an Al-Ameen cash-book voucher (سند يومية) and names its cash account.</summary>
public sealed record JournalVoucherHeaderDto
{
    /// <summary>Voucher kind; only <c>DAILY</c> exists.</summary>
    [Required, StringLength(20)]
    public string Kind { get; init; } = "DAILY";

    public Guid CashAccountId { get; init; }
}
