using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Controls;

namespace PetraERP.Desktop.Sales;

/// <summary>
/// The discounts-and-additions table of the invoice (Al-Ameen): each row names its own account, its amount joins the invoice's
/// discount or additions total, and the server books it to that account when the invoice is posted.
/// </summary>
public sealed partial class SalesInvoiceCardViewModel
{
    private RelayCommand? addAdjustmentCommand;
    private RelayCommand<SalesAdjustmentItem>? removeAdjustmentCommand;

    public ObservableCollection<SalesAdjustmentItem> Adjustments { get; } = [];

    /// <summary>Active accounts that accept postings, for the table's account picker.</summary>
    public ObservableCollection<LookupItem> AdjustmentAccounts { get; } = [];

    public IReadOnlyList<AdjustmentKindOption> AdjustmentKinds =>
    [
        new(SalesInvoiceAdjustmentKind.Discount, Localization["D_AdjustmentDiscount"]),
        new(SalesInvoiceAdjustmentKind.Addition, Localization["D_AdjustmentAddition"]),
    ];

    public bool HasAdjustments => Adjustments.Count > 0;

    public RelayCommand AddAdjustmentCommand => addAdjustmentCommand ??= new(() => AddAdjustment());

    public RelayCommand<SalesAdjustmentItem> RemoveAdjustmentCommand => removeAdjustmentCommand ??= new(item => RemoveAdjustment(item));

    public SalesAdjustmentItem AddAdjustment()
    {
        var item = NewAdjustment();
        Adjustments.Add(item);
        OnPropertyChanged(nameof(HasAdjustments));
        return item;
    }

    public void RemoveAdjustment(SalesAdjustmentItem? item)
    {
        if (item is null || !Adjustments.Remove(item)) return;
        OnPropertyChanged(nameof(HasAdjustments));
        RecalculateTotals();
        DraftChanged();
    }

    private SalesAdjustmentItem NewAdjustment() => new()
    {
        Basis = LinesSubTotal,
        Changed = () => { RecalculateTotals(); DraftChanged(); },
    };

    private decimal LinesSubTotal() =>
        Lines.Where(l => !l.IsPristineBlank).Sum(l => Math.Round(ParseDecimal(l.Quantity) * ParseDecimal(l.UnitPrice), 4, MidpointRounding.AwayFromZero));

    private void PopulateAdjustments(SalesInvoiceDetailDto detail)
    {
        Adjustments.Clear();
        foreach (var row in detail.Adjustments ?? [])
        {
            var item = NewAdjustment();
            item.LoadValues(row.Kind ?? SalesInvoiceAdjustmentKind.Discount, row.AccountId, row.Amount ?? string.Empty,
                row.Percentage is { } percentage && ParseDecimal(percentage) > 0m ? percentage : string.Empty, row.Notes, row.CostCenterId, row.CounterAccountId);
            Adjustments.Add(item);
        }

        OnPropertyChanged(nameof(HasAdjustments));
    }

    private List<SalesInvoiceAdjustmentRequest> BuildAdjustmentRequests() =>
        Adjustments.Where(a => a.IsComplete).Select((a, index) => new SalesInvoiceAdjustmentRequest
        {
            Kind = a.Kind,
            AccountId = a.AccountId!.Value,
            CounterAccountId = a.CounterAccountId,
            Amount = a.AmountValue.ToString("0.0000", CultureInfo.InvariantCulture),
            Percentage = ParseDecimal(a.Percentage) is > 0m and <= 100m ? ParseDecimal(a.Percentage).ToString("0.0000", CultureInfo.InvariantCulture) : null,
            Notes = string.IsNullOrWhiteSpace(a.Notes) ? null : a.Notes.Trim(),
            CostCenterId = a.CostCenterId,
            DisplayOrder = index + 1,
        }).ToList();

    /// <summary>A row that was started but cannot be saved (no account or no amount) is reported instead of silently dropped.</summary>
    private bool HasIncompleteAdjustment => Adjustments.Any(a => !a.IsBlank && !a.IsComplete);

    private void RebaseAdjustments()
    {
        foreach (var item in Adjustments) item.Rebase();
    }

    private async Task LoadAdjustmentAccountsAsync(CancellationToken cancellationToken)
    {
        AdjustmentAccounts.Clear();
        CustomerAccountLookups.Clear();
        if (!Can("Ledger.Accounts.View")) return;
        try
        {
            var tree = await accountsClient.GetAccountTreeAsync(CompanyId, BranchId, cancellationToken).ConfigureAwait(true);
            foreach (var account in tree.Where(a => a.PostingAllowed && a.IsActive && a.Id != Guid.Empty).OrderBy(a => a.Code, StringComparer.Ordinal))
            {
                var item = new LookupItem(account.Id, account.Code, account.NameArabic, account.NameEnglish);
                AdjustmentAccounts.Add(item);
                CustomerAccountLookups.Add(item);
            }
            UpdateCustomerAccount();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The picker stays empty; an invoice without these rows is unaffected.
        }
    }
}
