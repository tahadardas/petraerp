using System.Collections.ObjectModel;
using PetraERP.Desktop.Controls;
using PetraERP.Desktop.GeneralLedger;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Accounting;

/// <summary>
/// Account and cost-center pickers for filters. Each list is requested only when the user holds the matching view
/// permission, so a reports-only user never triggers a forbidden lookup.
/// </summary>
public sealed class LedgerLookups(IGeneralLedgerClient client)
{
    public ObservableCollection<LookupItem> Accounts { get; } = [];
    public ObservableCollection<LookupItem> PostingAccounts { get; } = [];
    public ObservableCollection<LookupItem> CostCenters { get; } = [];

    public async Task LoadAsync(ShellContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        Clear();
        if (context.Can("Ledger.Accounts.View"))
        {
            var accounts = await client.GetAccountsAsync(context.CompanyId, context.BranchId, cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var account in accounts)
            {
                var item = new LookupItem(account.Id, account.Code, account.NameArabic, account.NameEnglish);
                Accounts.Add(item);
                if (account.PostingAllowed && account.IsActive) PostingAccounts.Add(item);
            }
        }

        if (context.Can("Ledger.CostCenters.View"))
        {
            var centers = await client.GetCostCentersAsync(context.CompanyId, context.BranchId, cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var center in centers) CostCenters.Add(new LookupItem(center.Id, center.Code, center.NameArabic, center.NameEnglish, center.IsActive));
        }
    }

    public void Clear()
    {
        Accounts.Clear(); PostingAccounts.Clear(); CostCenters.Clear();
    }
}

/// <summary>Formats server decimal strings for display without binary floating point.</summary>
public static class MoneyText
{
    /// <summary>Delegates to <see cref="Controls.PetraNumberFormat"/> so every money display shares one formatter.</summary>
    public static string Format(string? value, bool parenthesesForNegative = false) =>
        Controls.PetraNumberFormat.Format(value, Controls.NumberKind.Money, parenthesesForNegative);

    public static bool IsZero(string? value) => Controls.PetraNumberFormat.IsZero(value);
}
