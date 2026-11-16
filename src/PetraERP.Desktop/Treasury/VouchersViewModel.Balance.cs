using System.Globalization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Treasury;

/// <summary>
/// Al-Ameen shows the account's balance before and after the voucher. Here it is the selected party's open balance (what a customer
/// still owes on a receipt, what is still owed to the supplier on a payment) and what it becomes once this voucher's amount is applied.
/// It is read-only information for a draft; the ledger and the open items stay the authority.
/// </summary>
public sealed partial class VouchersViewModel
{
    private decimal? partyReceivable;
    private decimal? partyPayable;
    private int balanceRequest;

    private decimal? PartyBalance => IsReceipt ? partyReceivable : partyPayable;

    public bool ShowPartyBalance => IsDraft && DraftPartyId is not null && PartyBalance is not null;

    public string PreviousBalanceDisplay => PartyBalance is { } balance ? balance.ToString("0.0000", CultureInfo.InvariantCulture) : string.Empty;

    public string FinalBalanceDisplay => PartyBalance is { } balance ? (balance - (Parse(DraftAmount) ?? 0m)).ToString("0.0000", CultureInfo.InvariantCulture) : string.Empty;

    private async Task LoadPartyBalanceAsync(Guid? partyId)
    {
        var request = ++balanceRequest;
        partyReceivable = partyPayable = null;
        RefreshBalance();
        if (partyId is not { } id || !HasContext) return;
        try
        {
            var party = await partiesClient.GetPartyAsync(CompanyId, BranchId, id, Token).ConfigureAwait(true);
            if (request != balanceRequest) return;
            partyReceivable = Parse(party.ReceivableBalance);
            partyPayable = Parse(party.PayableBalance);
        }
        catch (Exception exception) when (exception is OperationCanceledException or System.Net.Http.HttpRequestException or PetraERP.ApiClient.PetraApiException)
        {
            // The balance is information only: a failed read leaves the fields empty and never blocks the voucher.
        }

        RefreshBalance();
    }

    private void RefreshBalance()
    {
        OnPropertyChanged(nameof(ShowPartyBalance));
        OnPropertyChanged(nameof(PreviousBalanceDisplay));
        OnPropertyChanged(nameof(FinalBalanceDisplay));
    }
}
