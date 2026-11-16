using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Shell;
using PetraERP.Desktop.Treasury;

namespace PetraERP.Desktop.Parties;

public sealed partial class PartyCardViewModel
{
    private AsyncRelayCommand? newReceiptCommand;
    private AsyncRelayCommand? newPaymentCommand;
    private AsyncRelayCommand? customerStatementCommand;
    private AsyncRelayCommand? supplierStatementCommand;
    private AsyncRelayCommand? openAccountLedgerCommand;

    /// <summary>Opens the dedicated account ledger (كشف الحساب / دفتر الأستاذ) for this party's accounting ledger account.</summary>
    public AsyncRelayCommand OpenAccountLedgerCommand => openAccountLedgerCommand ??= new(
        () =>
        {
            var targetAccountId = (Party?.Role == PartyRole.Supplier ? Party?.PayableAccountId : Party?.ReceivableAccountId)
                                  ?? Party?.ReceivableAccountId ?? Party?.PayableAccountId;
            return targetAccountId is not null
                ? navigator.NavigateAsync("account-ledger", new OpenAccountLedgerRequest(targetAccountId.Value, null, null, false))
                : Task.CompletedTask;
        },
        () => HasContext && Idle && IsViewMode && (Party?.ReceivableAccountId is not null || Party?.PayableAccountId is not null));

    public AsyncRelayCommand CustomerStatementCommand => customerStatementCommand ??= new(
        () => CanStatement(PartyRole.Customer) ? navigator.NavigateAsync("customer-statement",
            new Reports.OpenPartyStatementRequest(PartyId!.Value, PartyStatementKind.Customer, Party?.DefaultCurrencyCode)) : Task.CompletedTask,
        () => CanStatement(PartyRole.Customer));
    public AsyncRelayCommand SupplierStatementCommand => supplierStatementCommand ??= new(
        () => CanStatement(PartyRole.Supplier) ? navigator.NavigateAsync("supplier-statement",
            new Reports.OpenPartyStatementRequest(PartyId!.Value, PartyStatementKind.Supplier, Party?.DefaultCurrencyCode)) : Task.CompletedTask,
        () => CanStatement(PartyRole.Supplier));
    private bool CanStatement(PartyRole role) => HasContext && Idle && IsViewMode && PartyId is not null
        && Party is { } party && (party.Role == role || party.Role == PartyRole.Both)
        && Can(Reports.CommercialReportsViewModel.ViewPermission) && Can(PartiesViewModel.ViewPermission);

    /// <summary>Opens a new receipt for this customer in the voucher workstation (allocations load from its outstanding invoices).</summary>
    public AsyncRelayCommand NewReceiptCommand => newReceiptCommand ??= new(
        () => CanStartVoucher(PartyRole.Customer) ? navigator.NavigateAsync("treasury-vouchers", new NewVoucherRequest(VoucherKind.Receipt, PartyId)) : Task.CompletedTask,
        () => CanStartVoucher(PartyRole.Customer));

    /// <summary>Opens a new payment whose first line targets this supplier's payable.</summary>
    public AsyncRelayCommand NewPaymentCommand => newPaymentCommand ??= new(
        () => CanStartVoucher(PartyRole.Supplier) ? navigator.NavigateAsync("treasury-vouchers", new NewVoucherRequest(VoucherKind.Payment, PartyId)) : Task.CompletedTask,
        () => CanStartVoucher(PartyRole.Supplier));

    private AsyncRelayCommand? newPurchaseCommand;

    /// <summary>Opens a new purchase invoice for this supplier in the purchases workstation.</summary>
    public AsyncRelayCommand NewPurchaseCommand => newPurchaseCommand ??= new(
        () => CanStartPurchase() ? navigator.NavigateAsync("purchase-invoices", new Purchases.NewPurchaseRequest(PartyId)) : Task.CompletedTask,
        CanStartPurchase);

    private bool CanStartPurchase() => HasContext && Idle && IsViewMode && PartyId is not null
        && Party is { IsActive: true } party && (party.Role == PartyRole.Supplier || party.Role == PartyRole.Both)
        && Can(Purchases.PurchasesViewModel.ManagePermission) && Can(Purchases.PurchasesViewModel.ViewPermission);

    private bool CanStartVoucher(PartyRole role) => HasContext && Idle && IsViewMode && PartyId is not null
        && Party is { IsActive: true } party && (party.Role == role || party.Role == PartyRole.Both)
        && Can(VouchersViewModel.ManagePermission) && Can(VouchersViewModel.ViewPermission);

    private void NotifyTreasuryCommands()
    {
        newReceiptCommand?.NotifyCanExecuteChanged();
        newPaymentCommand?.NotifyCanExecuteChanged();
        newPurchaseCommand?.NotifyCanExecuteChanged();
        customerStatementCommand?.NotifyCanExecuteChanged();
        supplierStatementCommand?.NotifyCanExecuteChanged();
        openAccountLedgerCommand?.NotifyCanExecuteChanged();
    }

    private void RefreshRelatedSources()
    {
        var scope = IsViewMode ? Context : null;
        var supplier = Party?.Role is PartyRole.Supplier or PartyRole.Both ? PartyId : null;
        var customer = Party?.Role is PartyRole.Customer or PartyRole.Both ? PartyId : null;
        PurchaseLinks?.SetSource(scope, Relationships.RelatedDocumentSource.SupplierPurchases, supplier);
        ReceiptLinks?.SetSource(scope, Relationships.RelatedDocumentSource.CustomerReceipts, customer);
        PaymentLinks?.SetSource(scope, Relationships.RelatedDocumentSource.SupplierPayments, supplier);
    }

    partial void OnPartyChanged(PartyDetailDto? value) { NotifyTreasuryCommands(); RefreshRelatedSources(); }
}
