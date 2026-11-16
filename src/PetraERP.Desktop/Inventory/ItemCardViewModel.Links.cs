using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Sales;

namespace PetraERP.Desktop.Inventory;

public sealed partial class ItemCardViewModel
{
    internal void FocusMovement(Guid movementId)
    {
        linkedMovementId = movementId;
        DocumentLinks?.SetSource(Context, SalesLinkSource.Movement, movementId);
    }
    [ObservableProperty] private StockMovementDto? selectedMovement;
    private AsyncRelayCommand? openMovementSourceCommand;
    private AsyncRelayCommand? viewItemInvoicesCommand;

    public AsyncRelayCommand OpenMovementSourceCommand => openMovementSourceCommand ??= new(async () =>
    {
        if (!CanOpenMovementSource()) return;
        FocusMovement(SelectedMovement!.Id!.Value);
        await DocumentLinks!.RefreshCommand.ExecuteAsync(null).ConfigureAwait(true);
    }, CanOpenMovementSource);

    public AsyncRelayCommand ViewItemInvoicesCommand => viewItemInvoicesCommand ??= new(async () =>
    {
        if (!CanViewItemInvoices()) return;
        linkedMovementId = null;
        DocumentLinks!.SetSource(Context, SalesLinkSource.Item, ItemId);
        await DocumentLinks.RefreshCommand.ExecuteAsync(null).ConfigureAwait(true);
    }, CanViewItemInvoices);

    private bool CanViewItemInvoices() => HasContext && Idle && IsViewMode && ItemId is not null && DocumentLinks is not null &&
        Can("Sales.Invoices.View") && Can("Inventory.Items.View");
    private bool CanOpenMovementSource() => CanViewItemInvoices() && Can("Inventory.Movements.View") &&
        SelectedMovement is { DocumentType: "SalesInvoice" or "SalesInvoiceReturn" } row && Movements.Contains(row);
    partial void OnSelectedMovementChanged(StockMovementDto? value) => openMovementSourceCommand?.NotifyCanExecuteChanged();
}
