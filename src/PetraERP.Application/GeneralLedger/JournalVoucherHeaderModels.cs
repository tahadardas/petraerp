using PetraERP.Application.Common;

namespace PetraERP.Application.GeneralLedger;

/// <summary>The cash-book voucher marker of a journal.</summary>
public sealed record JournalVoucherHeaderData(string Kind, Guid CashAccountId)
{
    public const string Daily = "DAILY";
}

/// <summary>Reads and sets the cash-book voucher marker of a draft journal (Al-Ameen daily voucher).</summary>
public interface IJournalVoucherHeaderService
{
    /// <summary>The marker of a journal, or null when it is an ordinary journal.</summary>
    Task<JournalVoucherHeaderData?> GetAsync(JournalEntryScope scope, Guid journalId, CancellationToken cancellationToken);

    /// <summary>Marks a draft or validated journal as a voucher of <paramref name="data"/>; replaces an earlier marker.</summary>
    Task<JournalVoucherHeaderData> SetAsync(JournalEntryScope scope, Guid journalId, JournalVoucherHeaderData data, CancellationToken cancellationToken);
}
