using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Patterns;

public sealed partial class DocumentPatternSummaryItem : ObservableObject
{
    public Guid Id { get; init; }
    public BusinessOperationKind OperationKind { get; init; }
    public string Code { get; init; } = string.Empty;
    public string NameArabic { get; init; } = string.Empty;
    public string? NameEnglish { get; init; }
    public string ShortCode { get; init; } = string.Empty;
    public string? Icon { get; init; }
    public string? Group { get; init; }
    public bool IsActive { get; set; }
    public bool IsDefault { get; init; }
    public bool IsRunnable { get; init; }
    public int? ActiveRevisionNumber { get; init; }
    public PatternRevisionState? ActiveRevisionState { get; init; }
    public string Version { get; set; } = string.Empty;
    public IReadOnlyList<Guid> BranchIds { get; init; } = [];

    [ObservableProperty] private bool isFavorite;

    public string BranchDisplay => BranchIds.Count == 0 ? "كافة الفروع" : $"{BranchIds.Count} فروع";
}

public sealed partial class DocumentPatternsViewModel : PageViewModel, IShellPage
{
    public const string ViewPermission = "Patterns.Documents.View";
    public const string ManagePermission = "Patterns.Documents.Manage";
    public const string PublishPermission = "Patterns.Documents.Publish";

    private readonly IPatternsClient patternsClient;
    private readonly ShellNavigator navigator;
    private readonly IUserConfirmation confirmation;

    [ObservableProperty] private string search = string.Empty;
    [ObservableProperty] private string filterTab = "all";
    [ObservableProperty] private bool activeOnly;
    [ObservableProperty] private DocumentPatternSummaryItem? selectedPattern;
    [ObservableProperty] private int totalCount;

    public DocumentPatternsViewModel(
        IPatternsClient patternsClient,
        ShellNavigator navigator,
        DesktopOperation operation,
        ILocalizationService localization,
        IUserConfirmation confirmation)
        : base(operation, localization)
    {
        this.patternsClient = patternsClient;
        this.navigator = navigator;
        this.confirmation = confirmation;

        RefreshCommand = new AsyncRelayCommand(LoadPatternsAsync, () => HasContext && Idle);
        OpenCardCommand = new AsyncRelayCommand(OpenSelectedCardAsync, () => SelectedPattern is not null && Idle);
        NewPatternCommand = new AsyncRelayCommand(NewPatternAsync, () => HasContext && Idle && CanManage);
        DuplicatePatternCommand = new AsyncRelayCommand(DuplicateSelectedAsync, () => SelectedPattern is not null && Idle && CanManage);
        PublishRevisionCommand = new AsyncRelayCommand(PublishSelectedAsync, () => SelectedPattern is not null && Idle && CanPublish);
        RetirePatternCommand = new AsyncRelayCommand(RetireSelectedAsync, () => SelectedPattern is not null && Idle && CanPublish);
        ToggleActiveCommand = new AsyncRelayCommand(ToggleSelectedActiveAsync, () => SelectedPattern is not null && Idle && CanManage);
        ToggleFavoriteCommand = new AsyncRelayCommand<DocumentPatternSummaryItem>(ToggleFavoriteAsync);
    }

    protected override string RequiredPermission => ViewPermission;
    public bool CanManage => Can(ManagePermission);
    public bool CanPublish => Can(PublishPermission);

    public ObservableCollection<DocumentPatternSummaryItem> Patterns { get; } = [];

    public IReadOnlyList<DocumentPatternSummaryItem> VisiblePatterns => Patterns
        .Where(MatchesFilter)
        .ToList();

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand OpenCardCommand { get; }
    public AsyncRelayCommand NewPatternCommand { get; }
    public AsyncRelayCommand DuplicatePatternCommand { get; }
    public AsyncRelayCommand PublishRevisionCommand { get; }
    public AsyncRelayCommand RetirePatternCommand { get; }
    public AsyncRelayCommand ToggleActiveCommand { get; }
    public AsyncRelayCommand<DocumentPatternSummaryItem> ToggleFavoriteCommand { get; }

    protected override Task LoadAsync(object? parameter, CancellationToken cancellationToken)
    {
        if (parameter is OpenPatternsRequest request) FilterTab = request.FilterTab;
        NotifyPermissions();
        return LoadPatternsCoreAsync(cancellationToken);
    }

    protected override void ClearPage()
    {
        Patterns.Clear();
        SelectedPattern = null;
        Search = string.Empty;
        FilterTab = "all";
        ActiveOnly = false;
        TotalCount = 0;
        OnPropertyChanged(nameof(VisiblePatterns));
    }

    public void Localize()
    {
        OnPropertyChanged(nameof(VisiblePatterns));
        NotifyCommands();
    }

    partial void OnSearchChanged(string value) => OnPropertyChanged(nameof(VisiblePatterns));
    partial void OnFilterTabChanged(string value) => OnPropertyChanged(nameof(VisiblePatterns));
    partial void OnActiveOnlyChanged(bool value) => OnPropertyChanged(nameof(VisiblePatterns));
    partial void OnSelectedPatternChanged(DocumentPatternSummaryItem? value) => NotifyCommands();

    protected override void NotifyCommands()
    {
        OpenCardCommand?.NotifyCanExecuteChanged();
        NewPatternCommand?.NotifyCanExecuteChanged();
        DuplicatePatternCommand?.NotifyCanExecuteChanged();
        PublishRevisionCommand?.NotifyCanExecuteChanged();
        RetirePatternCommand?.NotifyCanExecuteChanged();
        ToggleActiveCommand?.NotifyCanExecuteChanged();
    }

    private void NotifyPermissions()
    {
        OnPropertyChanged(nameof(CanManage));
        OnPropertyChanged(nameof(CanPublish));
        NotifyCommands();
    }

    private bool MatchesFilter(DocumentPatternSummaryItem item)
    {
        if (ActiveOnly && !item.IsActive) return false;

        if (FilterTab == "invoices")
        {
            if (item.OperationKind is not (BusinessOperationKind.SalesInvoice or BusinessOperationKind.SalesReturn
                or BusinessOperationKind.PurchaseInvoice or BusinessOperationKind.PurchaseReturn))
                return false;
        }
        else if (FilterTab == "vouchers")
        {
            if (item.OperationKind is not (BusinessOperationKind.ManualJournal or BusinessOperationKind.CashReceipt
                or BusinessOperationKind.CashPayment))
                return false;
        }
        else if (FilterTab == "cash")
        {
            if (item.OperationKind is not BusinessOperationKind.CashJournal) return false;
        }
        else if (FilterTab == "inventory")
        {
            if (item.OperationKind is not (BusinessOperationKind.InventoryIssue or BusinessOperationKind.InventoryReceipt)) return false;
        }

        if (string.IsNullOrWhiteSpace(Search)) return true;

        var term = Search.Trim();
        return item.Code.Contains(term, StringComparison.OrdinalIgnoreCase)
            || item.NameArabic.Contains(term, StringComparison.CurrentCultureIgnoreCase)
            || (item.NameEnglish?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
            || item.ShortCode.Contains(term, StringComparison.OrdinalIgnoreCase);
    }

    private Task<bool> LoadPatternsAsync() => RunAsync(LoadPatternsCoreAsync);

    private async Task LoadPatternsCoreAsync(CancellationToken ct)
    {
        if (!HasContext) return;

        var response = await patternsClient.ListPatternsAsync(
            CompanyId, BranchId, page: 1, pageSize: 200, kind: null, activeOnly: null, ct).ConfigureAwait(true);

        Patterns.Clear();
        if (response.Items is not null)
        {
            foreach (var item in response.Items)
            {
                Patterns.Add(new DocumentPatternSummaryItem
                {
                    Id = item.Id ?? Guid.Empty,
                    OperationKind = item.OperationKind ?? BusinessOperationKind.ManualJournal,
                    Code = item.Code ?? string.Empty,
                    NameArabic = item.NameArabic ?? string.Empty,
                    NameEnglish = item.NameEnglish,
                    ShortCode = item.ShortCode ?? string.Empty,
                    Icon = item.Icon,
                    Group = item.Group,
                    IsActive = item.IsActive ?? false,
                    IsDefault = item.IsDefault ?? false,
                    IsRunnable = item.IsRunnable ?? false,
                    ActiveRevisionNumber = item.ActiveRevisionNumber,
                    ActiveRevisionState = item.ActiveRevisionNumber is not null ? PatternRevisionState.Published : PatternRevisionState.Draft,
                    Version = item.Version ?? string.Empty,
                    BranchIds = [],
                });
            }
        }

        TotalCount = (int)(response.TotalCount ?? 0);
        OnPropertyChanged(nameof(VisiblePatterns));
        NotifyCommands();
    }

    private Task OpenSelectedCardAsync()
    {
        if (SelectedPattern is null) return Task.CompletedTask;
        return navigator.NavigateAsync("patterns", new OpenPatternCardRequest(SelectedPattern.Id));
    }

    private Task NewPatternAsync()
    {
        var kind = FilterTab switch
        {
            "invoices" => BusinessOperationKind.SalesInvoice,
            "vouchers" => BusinessOperationKind.ManualJournal,
            "cash" => BusinessOperationKind.CashJournal,
            "inventory" => BusinessOperationKind.InventoryReceipt,
            _ => (BusinessOperationKind?)null,
        };
        return navigator.NavigateAsync("patterns", new NewPatternCardRequest(InitialOperationKind: kind));
    }

    private Task DuplicateSelectedAsync()
    {
        if (SelectedPattern is null) return Task.CompletedTask;
        return navigator.NavigateAsync("patterns", new NewPatternCardRequest(SelectedPattern.Id));
    }

    private async Task ToggleFavoriteAsync(DocumentPatternSummaryItem? item)
    {
        var target = item ?? SelectedPattern;
        if (target is null) return;

        if (!target.IsRunnable)
        {
            Operation.SetError("PatternNotRunnableFavorite");
            return;
        }

        target.IsFavorite = !target.IsFavorite;
    }

    private async Task PublishSelectedAsync()
    {
        if (SelectedPattern is null || !CanPublish) return;

        var confirmed = await confirmation.ConfirmAsync("D_ConfirmPublishPattern").ConfigureAwait(true);
        if (!confirmed) return;

        await RunAsync(async ct =>
        {
            var details = await patternsClient.GetPatternAsync(CompanyId, BranchId, SelectedPattern.Id, ct).ConfigureAwait(true);
            var revId = details.ActiveRevision?.Id;
            if (revId is null)
            {
                Operation.SetError("NoActiveRevisionToPublish");
                return;
            }

            var request = new PublishPatternRevisionRequest
            {
                ExpectedVersion = details.Version,
                RevisionId = revId.Value,
                Reason = "نشر الإصدار من دليل الأنماط",
            };

            await patternsClient.PublishRevisionAsync(CompanyId, BranchId, SelectedPattern.Id, request, ct).ConfigureAwait(true);
            await LoadPatternsAsync().ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    private async Task RetireSelectedAsync()
    {
        if (SelectedPattern is null || !CanPublish) return;

        var confirmed = await confirmation.ConfirmAsync("D_ConfirmRetirePattern").ConfigureAwait(true);
        if (!confirmed) return;

        await RunAsync(async ct =>
        {
            var request = new RetirePatternRequest
            {
                ExpectedVersion = SelectedPattern.Version,
                Reason = "إيقاف النمط من دليل الأنماط",
            };

            await patternsClient.RetirePatternAsync(CompanyId, BranchId, SelectedPattern.Id, request, ct).ConfigureAwait(true);
            await LoadPatternsAsync().ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    private async Task ToggleSelectedActiveAsync()
    {
        if (SelectedPattern is null || !CanManage) return;

        await RunAsync(async ct =>
        {
            var request = new UpdateDocumentPatternRequest
            {
                ExpectedVersion = SelectedPattern.Version,
                NameArabic = SelectedPattern.NameArabic,
                NameEnglish = SelectedPattern.NameEnglish,
                ShortCode = SelectedPattern.ShortCode,
                Icon = SelectedPattern.Icon,
                Group = SelectedPattern.Group,
                IsActive = !SelectedPattern.IsActive,
                IsDefault = SelectedPattern.IsDefault,
                BranchIds = SelectedPattern.BranchIds.ToList(),
            };

            var updated = await patternsClient.UpdatePatternAsync(CompanyId, BranchId, SelectedPattern.Id, SelectedPattern.Version, request, ct).ConfigureAwait(true);
            SelectedPattern.IsActive = updated.IsActive ?? false;
            SelectedPattern.Version = updated.Version ?? string.Empty;
            OnPropertyChanged(nameof(VisiblePatterns));
        }).ConfigureAwait(true);
    }
}
