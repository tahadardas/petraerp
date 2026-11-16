using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Sales;

namespace PetraERP.Desktop.Purchases;

/// <summary>
/// The discounts-and-additions table of the purchase invoice (Al-Ameen): each row names its own account (and optionally a counter
/// account); a row without a counter account joins the invoice's discount or additions total and the supplier's payable.
/// </summary>
public sealed partial class PurchasesViewModel
{
    private RelayCommand? addAdjustmentCommand;
    private RelayCommand<SalesAdjustmentItem>? removeAdjustmentCommand;

    public ObservableCollection<SalesAdjustmentItem> Adjustments { get; } = [];

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
        Refresh();
    }

    private SalesAdjustmentItem NewAdjustment() => new() { Basis = LinesSubTotal, Changed = Refresh };

    private decimal LinesSubTotal() =>
        Lines.Where(l => l.IsComplete).Sum(l => Math.Round((PurchaseLineItem.Parse(l.Quantity) ?? 0m) * (PurchaseLineItem.Parse(l.UnitCost) ?? 0m), 4, MidpointRounding.AwayFromZero));

    private void PopulateAdjustments(PurchaseDetailDto detail)
    {
        Adjustments.Clear();
        foreach (var row in detail.Adjustments ?? [])
        {
            var item = NewAdjustment();
            item.LoadValues(row.Kind ?? SalesInvoiceAdjustmentKind.Discount, row.AccountId, row.Amount ?? string.Empty,
                decimal.TryParse(row.Percentage, NumberStyles.Number, CultureInfo.InvariantCulture, out var percentage) && percentage > 0m ? row.Percentage! : string.Empty,
                row.Notes, row.CostCenterId, row.CounterAccountId);
            Adjustments.Add(item);
        }

        OnPropertyChanged(nameof(HasAdjustments));
    }

    private List<PurchaseAdjustmentRequest> BuildAdjustmentRequests() =>
        Adjustments.Where(a => a.IsComplete).Select((a, index) => new PurchaseAdjustmentRequest
        {
            Kind = a.Kind,
            AccountId = a.AccountId!.Value,
            CounterAccountId = a.CounterAccountId,
            Amount = a.AmountValue.ToString("0.0000", CultureInfo.InvariantCulture),
            Percentage = decimal.TryParse(a.Percentage, NumberStyles.Number, CultureInfo.InvariantCulture, out var percentage) && percentage is > 0m and <= 100m
                ? percentage.ToString("0.0000", CultureInfo.InvariantCulture) : null,
            Notes = string.IsNullOrWhiteSpace(a.Notes) ? null : a.Notes.Trim(),
            CostCenterId = a.CostCenterId,
            DisplayOrder = index + 1,
        }).ToList();

    private bool HasIncompleteAdjustment => Adjustments.Any(a => !a.IsBlank && !a.IsComplete);

    private void RebaseAdjustments()
    {
        foreach (var item in Adjustments) item.Rebase();
    }
}
