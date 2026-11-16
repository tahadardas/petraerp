using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Relationships;

/// <summary>Each document owns its browser; scope, source, edit mode and permission changes invalidate pending results.</summary>
public sealed partial class RelatedDocumentsViewModel : ObservableObject
{
    public const int PageSize = 50;
    private readonly IRelatedDocumentsClient client;
    private readonly ShellNavigator navigator;
    private readonly DesktopOperation operation;
    private ShellContext? context;
    private Guid? sourceId;
    private RelatedDocumentSource source;
    private string permissionStamp = string.Empty;
    private int generation;
    [ObservableProperty] private bool loading;
    [ObservableProperty] private int page = 1;
    [ObservableProperty] private int totalCount;
    [ObservableProperty] private RelatedDocumentRow? selected;

    public RelatedDocumentsViewModel(IRelatedDocumentsClient client, ShellNavigator navigator, DesktopOperation operation, ILocalizationService localization)
    {
        this.client = client; this.navigator = navigator; this.operation = operation; Localization = localization;
        RefreshCommand = new(() => LoadAsync(1), CanLoad);
        NextCommand = new(() => LoadAsync(Page + 1), () => CanLoad() && (long)Page * PageSize < TotalCount);
        PreviousCommand = new(() => LoadAsync(Page - 1), () => CanLoad() && Page > 1);
        OpenCommand = new(OpenAsync, CanOpen);
    }

    public ILocalizationService Localization { get; }
    public ObservableCollection<RelatedDocumentRow> Items { get; } = [];
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand NextCommand { get; }
    public AsyncRelayCommand PreviousCommand { get; }
    public AsyncRelayCommand OpenCommand { get; }
    public string PageStatus => $"{Page} · {Items.Count} / {TotalCount}";
    public string Heading => Localization["D_" + HeadingToken];
    public string HeadingToken => source switch
    {
        RelatedDocumentSource.SupplierPurchases => "LinkedPurchases",
        RelatedDocumentSource.CustomerReceipts or RelatedDocumentSource.InvoiceReceipts => "ReceiptVouchers",
        RelatedDocumentSource.SupplierPayments => "PaymentVouchers",
        _ => "RelatedVouchers",
    };
    public bool IsAvailable => sourceId is not null && context?.Can(TargetPermission) == true && context.Can(SourcePermission);
    private string TargetPermission => source == RelatedDocumentSource.SupplierPurchases ? "Purchases.Invoices.View" : "Treasury.Vouchers.View";
    private string SourcePermission => source switch
    {
        RelatedDocumentSource.InvoiceReceipts => "Sales.Invoices.View",
        RelatedDocumentSource.JournalVouchers => "Ledger.Journals.View",
        _ => "Parties.Directory.View",
    };

    public void SetSource(ShellContext? scope, RelatedDocumentSource kind, Guid? id)
    {
        var stamp = scope is null ? string.Empty : string.Join('\n', scope.Permissions.Order(StringComparer.Ordinal));
        if (context?.CompanyId == scope?.CompanyId && context?.BranchId == scope?.BranchId
            && source == kind && sourceId == id && permissionStamp == stamp)
        {
            context = scope; NotifyCommands(); return;
        }
        generation++; context = scope; source = kind; sourceId = id; permissionStamp = stamp;
        Items.Clear(); Selected = null; Page = 1; TotalCount = 0;
        OnPropertyChanged(nameof(IsAvailable)); OnPropertyChanged(nameof(Heading)); OnPropertyChanged(nameof(HeadingToken)); OnPropertyChanged(nameof(PageStatus));
        NotifyCommands();
    }

    private bool CanLoad() => IsAvailable && !Loading && !operation.IsBusy;
    private bool CanOpen() => CanLoad() && Selected is { Id: var id } row && id != Guid.Empty && Items.Contains(row);
    partial void OnSelectedChanged(RelatedDocumentRow? value) => OpenCommand.NotifyCanExecuteChanged();

    private async Task LoadAsync(int requestedPage)
    {
        if (!CanLoad()) return;
        var expected = generation; var scope = context!; var kind = source; var id = sourceId!.Value;
        Loading = true; NotifyCommands();
        try
        {
            await operation.RunAsync(async () =>
            {
                var result = await client.ListAsync(scope.CompanyId, scope.BranchId, kind, id, requestedPage, PageSize, CancellationToken.None).ConfigureAwait(true);
                if (expected != generation || !IsAvailable) return;
                Items.Clear(); Selected = null;
                foreach (var item in result.Items) Items.Add(item);
                Page = result.Page; TotalCount = result.TotalCount;
                OnPropertyChanged(nameof(PageStatus));
            }).ConfigureAwait(true);
        }
        finally { Loading = false; NotifyCommands(); }
    }

    private Task OpenAsync() => !CanOpen() ? Task.CompletedTask
        : source == RelatedDocumentSource.SupplierPurchases
            ? navigator.NavigateAsync("purchase-invoices", new Purchases.OpenPurchaseRequest(Selected!.Id))
            : navigator.NavigateAsync("treasury-vouchers", new Treasury.OpenVoucherRequest(Selected!.Id));

    public void NotifyCommands()
    {
        RefreshCommand.NotifyCanExecuteChanged(); NextCommand.NotifyCanExecuteChanged();
        PreviousCommand.NotifyCanExecuteChanged(); OpenCommand.NotifyCanExecuteChanged();
    }
}

public sealed class RelatedDocumentsFactory(IRelatedDocumentsClient client, ShellNavigator navigator, DesktopOperation operation, ILocalizationService localization)
{
    public RelatedDocumentsViewModel Create() => new(client, navigator, operation, localization);
}
