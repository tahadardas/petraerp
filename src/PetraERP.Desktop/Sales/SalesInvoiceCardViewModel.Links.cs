using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Sales;

public sealed partial class SalesInvoiceCardViewModel
{
    private AsyncRelayCommand? openCustomerCommand;
    private AsyncRelayCommand? openItemCommand;
    private AsyncRelayCommand? openJournalCommand;
    private AsyncRelayCommand? openOriginalInvoiceCommand;
    private AsyncRelayCommand? openReversalInvoiceCommand;
    private AsyncRelayCommand? receivePaymentCommand;
    private AsyncRelayCommand? managePatternsCommand;

    public bool CanViewPatterns => HasContext && Can("Patterns.Documents.View");
    public AsyncRelayCommand ManagePatternsCommand => managePatternsCommand ??= new(
        () => CanViewPatterns && Idle ? navigator.NavigateAsync("patterns") : Task.CompletedTask,
        () => CanViewPatterns && Idle);

    /// <summary>Starts a receipt for this posted credit invoice, pre-allocated to its outstanding balance (the server rechecks it).</summary>
    public AsyncRelayCommand ReceivePaymentCommand => receivePaymentCommand ??= new(
        () => CanReceivePayment() ? navigator.NavigateAsync("treasury-vouchers",
            new Treasury.NewVoucherRequest(VoucherKind.Receipt, Invoice!.CustomerId, Invoice.Id)) : Task.CompletedTask,
        CanReceivePayment);

    private bool CanReceivePayment() => CanFollow(Treasury.VouchersViewModel.ManagePermission) && Can(Treasury.VouchersViewModel.ViewPermission)
        && Invoice is { Status: SalesInvoiceStatus.Posted, PaymentMethod: PaymentMethod.Credit, ReversedByInvoiceId: null, ReversesInvoiceId: null, ReturnOfInvoiceId: null };

    public AsyncRelayCommand OpenCustomerCommand => openCustomerCommand ??= new(
        () => CanFollow("Parties.Directory.View") && Invoice?.CustomerId is { } id
            ? navigator.NavigateAsync("party-card", new OpenPartyCardRequest(id)) : Task.CompletedTask,
        () => CanFollow("Parties.Directory.View") && Invoice?.CustomerId is not null);

    private AsyncRelayCommand? openCustomerStatementCommand;
    public AsyncRelayCommand OpenCustomerStatementCommand => openCustomerStatementCommand ??= new(
        () => CanFollow("Ledger.Reports.View") && Invoice?.CustomerId is { } id
            ? navigator.NavigateAsync("customer-statement", new Reports.OpenPartyStatementRequest(id, PartyStatementKind.Customer)) : Task.CompletedTask,
        () => CanFollow("Ledger.Reports.View") && Invoice?.CustomerId is not null);

    public AsyncRelayCommand OpenItemCommand => openItemCommand ??= new(
        () => CanOpenItem() ? navigator.NavigateAsync("item-card", new OpenItemCardRequest(SelectedLine!.ItemId!.Value)) : Task.CompletedTask,
        CanOpenItem);

    public AsyncRelayCommand OpenJournalCommand => openJournalCommand ??= new(
        () => CanFollow("Ledger.Journals.View") && Invoice?.JournalEntryId is { } id
            ? navigator.NavigateAsync("journals", new OpenJournalRequest(id)) : Task.CompletedTask,
        () => CanFollow("Ledger.Journals.View") && Invoice?.JournalEntryId is not null);

    public AsyncRelayCommand OpenOriginalInvoiceCommand => openOriginalInvoiceCommand ??= new(
        () => CanFollow(SalesInvoicesViewModel.ViewPermission) && (Invoice?.ReversesInvoiceId ?? Invoice?.ReturnOfInvoiceId) is { } id
            ? navigator.NavigateAsync("sales-invoice-card", new OpenSalesInvoiceCardRequest(id)) : Task.CompletedTask,
        () => CanFollow(SalesInvoicesViewModel.ViewPermission) && (Invoice?.ReversesInvoiceId ?? Invoice?.ReturnOfInvoiceId) is not null);

    public AsyncRelayCommand OpenReversalInvoiceCommand => openReversalInvoiceCommand ??= new(
        () => CanFollow(SalesInvoicesViewModel.ViewPermission) && Invoice?.ReversedByInvoiceId is { } id
            ? navigator.NavigateAsync("sales-invoice-card", new OpenSalesInvoiceCardRequest(id)) : Task.CompletedTask,
        () => CanFollow(SalesInvoicesViewModel.ViewPermission) && Invoice?.ReversedByInvoiceId is not null);

    private bool CanFollow(string permission) => HasContext && Idle && IsViewMode && Invoice is not null && Can(permission);
    private bool CanOpenItem() => CanFollow("Inventory.Items.View") && SelectedLine is { ItemId: not null } line && Lines.Contains(line);
    partial void OnSelectedLineChanged(SalesInvoiceLineItem? value)
    {
        openItemCommand?.NotifyCanExecuteChanged(); RemoveLineCommand?.NotifyCanExecuteChanged(); UseListPriceCommand?.NotifyCanExecuteChanged();
    }
    private void NotifyLinkCommands()
    {
        OnPropertyChanged(nameof(CanViewPatterns)); managePatternsCommand?.NotifyCanExecuteChanged();
        openCustomerCommand?.NotifyCanExecuteChanged(); openItemCommand?.NotifyCanExecuteChanged();
        openCustomerStatementCommand?.NotifyCanExecuteChanged();
        openJournalCommand?.NotifyCanExecuteChanged(); openOriginalInvoiceCommand?.NotifyCanExecuteChanged();
        openReversalInvoiceCommand?.NotifyCanExecuteChanged();
        receivePaymentCommand?.NotifyCanExecuteChanged();
    }
}
