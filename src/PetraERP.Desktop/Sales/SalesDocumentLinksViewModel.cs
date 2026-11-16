using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Sales;

/// <summary>A document-owned, bounded link browser. Changing scope/source invalidates even non-cooperative responses.</summary>
public sealed partial class SalesDocumentLinksViewModel : ObservableObject
{
    private readonly ISalesDocumentLinksClient client;
    private readonly ShellNavigator navigator;
    private readonly DesktopOperation operation;
    private ShellContext? context;
    private SalesLinkSource source;
    private Guid? sourceId;
    private string permissionStamp = string.Empty;
    private int generation;
    [ObservableProperty] private int page = 1;
    [ObservableProperty] private int totalCount;
    [ObservableProperty] private bool loading;
    [ObservableProperty] private SalesDocumentLinkDto? selected;
    public ObservableCollection<SalesDocumentLinkDto> Items { get; } = [];
    public ILocalizationService Localization { get; }
    public string Heading => Localization[source == SalesLinkSource.Invoice ? "D_StockMovements" : "D_LinkedSalesInvoices"];
    public string PageStatus => $"{Page} · {Items.Count} / {TotalCount}";
    public bool IsAvailable => context?.Can("Sales.Invoices.View") == true && context.Can(SourcePermission(source));

    public SalesDocumentLinksViewModel(ISalesDocumentLinksClient client, ShellNavigator navigator,
        DesktopOperation operation, ILocalizationService localization)
    {
        this.client = client; this.navigator = navigator; this.operation = operation; Localization = localization;
        RefreshCommand = new AsyncRelayCommand(() => LoadAsync(1), CanLoad);
        NextCommand = new AsyncRelayCommand(() => LoadAsync(Page + 1), () => CanLoad() && Page * 50 < TotalCount);
        PreviousCommand = new AsyncRelayCommand(() => LoadAsync(Page - 1), () => CanLoad() && Page > 1);
        OpenCommand = new AsyncRelayCommand(OpenAsync, CanOpen);
    }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand NextCommand { get; }
    public AsyncRelayCommand PreviousCommand { get; }
    public AsyncRelayCommand OpenCommand { get; }

    public void SetSource(ShellContext? scope, SalesLinkSource kind, Guid? id)
    {
        // Permission changes invalidate the current results as well as company/branch changes.
        var stamp = scope is null ? string.Empty : string.Join('\n', scope.Permissions.Order(StringComparer.Ordinal));
        if (ReferenceEquals(context, scope) && source == kind && sourceId == id && permissionStamp == stamp) { NotifyCommands(); return; }
        generation++; context = scope; source = kind; sourceId = id;
        permissionStamp = stamp;
        Items.Clear(); Selected = null; Page = 1; TotalCount = 0;
        OnPropertyChanged(nameof(Heading)); OnPropertyChanged(nameof(IsAvailable)); OnPropertyChanged(nameof(PageStatus));
        NotifyCommands();
    }

    private bool CanLoad() => IsAvailable && sourceId is not null && !Loading && !operation.IsBusy;
    private bool CanOpen() => CanLoad() && Selected is { Id: { } id } row && Items.Contains(row) && id != Guid.Empty &&
        (source != SalesLinkSource.Invoice || (row.ItemId is not null && context!.Can("Inventory.Items.View")));
    partial void OnSelectedChanged(SalesDocumentLinkDto? value) => OpenCommand.NotifyCanExecuteChanged();

    private async Task LoadAsync(int requestedPage)
    {
        if (!CanLoad()) return;
        var requestedGeneration = generation;
        var scope = context!; var kind = source; var id = sourceId!.Value;
        Loading = true; NotifyCommands();
        try
        {
            await operation.RunAsync(async () =>
            {
                var result = await client.ListAsync(scope.CompanyId, scope.BranchId, kind, id, requestedPage, 50, CancellationToken.None).ConfigureAwait(true);
                if (requestedGeneration != generation) return;
                Items.Clear(); Selected = null;
                foreach (var item in result.Items ?? []) Items.Add(item);
                Page = result.Page ?? requestedPage; TotalCount = result.TotalCount ?? 0;
                OnPropertyChanged(nameof(PageStatus));
            }).ConfigureAwait(true);
        }
        finally { Loading = false; NotifyCommands(); }
    }

    private Task OpenAsync()
    {
        if (!CanOpen()) return Task.CompletedTask;
        return source == SalesLinkSource.Invoice
            ? navigator.NavigateAsync("item-card", new OpenItemCardRequest(Selected!.ItemId!.Value, Selected.Id))
            : navigator.NavigateAsync("sales-invoice-card", new OpenSalesInvoiceCardRequest(Selected!.Id!.Value));
    }

    public void NotifyCommands()
    {
        RefreshCommand.NotifyCanExecuteChanged(); NextCommand.NotifyCanExecuteChanged();
        PreviousCommand.NotifyCanExecuteChanged(); OpenCommand.NotifyCanExecuteChanged();
    }

    private static string SourcePermission(SalesLinkSource kind) => kind switch
    {
        SalesLinkSource.Customer => "Parties.Directory.View", SalesLinkSource.Item => "Inventory.Items.View",
        SalesLinkSource.Journal => "Ledger.Journals.View", _ => "Inventory.Movements.View",
    };
}

public sealed class SalesDocumentLinksFactory(ISalesDocumentLinksClient client, ShellNavigator navigator,
    DesktopOperation operation, ILocalizationService localization)
{
    public SalesDocumentLinksViewModel Create() => new(client, navigator, operation, localization);
}
