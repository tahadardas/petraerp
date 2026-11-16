using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Controls;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Accounting;

public sealed partial class CostCenterTreeNode : ObservableObject
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string NameArabic { get; init; } = string.Empty;
    public string? NameEnglish { get; init; }
    public byte CenterType { get; init; } = 1;
    public bool IsActive { get; set; } = true;
    public ObservableCollection<CostCenterTreeNode> Children { get; } = [];
    [ObservableProperty] private bool isExpanded = true;
    [ObservableProperty] private bool isSelected;

    public string CenterTypeDisplay => CenterType switch
    {
        1 => "عادي",
        2 => "فرعي",
        3 => "تجميعي",
        4 => "توزيعي",
        _ => "عادي"
    };

    public string DisplayText => string.IsNullOrWhiteSpace(Code)
        ? NameArabic
        : $"{Code} - {NameArabic} [{CenterTypeDisplay}]";
}

public sealed partial class CostCenterAllocationRowViewModel : ObservableObject
{
    [ObservableProperty] private Guid targetCostCenterId;
    [ObservableProperty] private string targetCode = string.Empty;
    [ObservableProperty] private string targetNameArabic = string.Empty;
    [ObservableProperty] private decimal percentage;

    public CostCenterAllocationRowViewModel(Guid targetCostCenterId, string targetCode, string targetNameArabic, decimal percentage)
    {
        TargetCostCenterId = targetCostCenterId;
        TargetCode = targetCode;
        TargetNameArabic = targetNameArabic;
        Percentage = percentage;
    }
}

/// <summary>Cost centers: server-side search and paging, hierarchical tree navigation, and allocation management.</summary>
public sealed partial class CostCentersViewModel : PageViewModel, IUnsavedPage
{
    protected override string RequiredPermission => "Ledger.CostCenters.View";
    public const string ManagePermission = "Ledger.CostCenters.Manage";
    public const int PageSize = 100;

    private readonly IChartOfAccountsClient client;
    private readonly ShellNavigator navigator;
    private readonly NotificationCenter notifications;
    private CostCenterResponse? editing;

    [ObservableProperty] private string search = string.Empty;
    [ObservableProperty] private bool? activeFilter;
    [ObservableProperty] private int page = 1;
    [ObservableProperty] private long totalCount;
    [ObservableProperty] private CostCenterResponse? selected;
    [ObservableProperty] private bool isEditorOpen;
    [ObservableProperty] private bool isNew;

    // Fields
    [ObservableProperty] private string code = string.Empty;
    [ObservableProperty] private string nameArabic = string.Empty;
    [ObservableProperty] private string? nameEnglish;
    [ObservableProperty] private Guid? parentId;
    [ObservableProperty] private byte centerType = 1;
    [ObservableProperty] private string? description;
    [ObservableProperty] private DateTime? startDate;
    [ObservableProperty] private DateTime? endDate;
    [ObservableProperty] private string? responsiblePerson;

    // Allocation Draft
    [ObservableProperty] private Guid? newAllocationTargetId;
    [ObservableProperty] private string newAllocationPercentage = "0.00";

    // Errors
    [ObservableProperty] private string codeError = string.Empty;
    [ObservableProperty] private string nameError = string.Empty;
    [ObservableProperty] private string allocationError = string.Empty;

    // Financial Analysis
    [ObservableProperty] private decimal totalDebit;
    [ObservableProperty] private decimal totalCredit;
    [ObservableProperty] private decimal netBalance;
    [ObservableProperty] private decimal totalRevenue;
    [ObservableProperty] private decimal totalExpense;
    [ObservableProperty] private decimal netProfitLoss;

    public CostCentersViewModel(IChartOfAccountsClient client, ShellNavigator navigator, DesktopOperation operation, ILocalizationService localization, NotificationCenter notifications)
        : base(operation, localization)
    {
        this.client = client; this.navigator = navigator; this.notifications = notifications;
        SearchCommand = new AsyncRelayCommand(() => RunAsync(token => LoadPageAsync(1, null, token)), () => HasContext && Idle);
        PreviousPageCommand = new AsyncRelayCommand(() => RunAsync(token => LoadPageAsync(Page - 1, null, token)), () => HasContext && Idle && Page > 1);
        NextPageCommand = new AsyncRelayCommand(() => RunAsync(token => LoadPageAsync(Page + 1, null, token)), () => HasContext && Idle && Page * (long)PageSize < TotalCount);
        NewCommand = new RelayCommand(StartNew, () => CanManage && Idle);
        NewChildCommand = new RelayCommand(StartNewChild, () => CanManage && editing is not null && Idle);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => CanManage && IsEditorOpen && Idle && !string.IsNullOrWhiteSpace(Code) && !string.IsNullOrWhiteSpace(NameArabic));
        PropertyChanged += (_, args) => { if (args.PropertyName is nameof(Code) or nameof(NameArabic)) SaveCommand.NotifyCanExecuteChanged(); };
        ToggleActiveCommand = new AsyncRelayCommand(ToggleActiveAsync, () => CanManage && editing is not null && !IsNew && Idle);
        OpenGeneralJournalCommand = new AsyncRelayCommand(OpenGeneralJournalAsync, () => CanViewGeneralJournal && editing is not null && Idle);
        OpenCostCenterLedgerCommand = new AsyncRelayCommand(OpenLedgerAsync, () => CanViewGeneralJournal && editing is not null && Idle);
        OpenCostCenterTrialBalanceCommand = new AsyncRelayCommand(OpenTrialBalanceAsync, () => CanViewGeneralJournal && editing is not null && Idle);

        AddAllocationCommand = new RelayCommand(AddAllocation, () => CanManage && IsEditorOpen && NewAllocationTargetId.HasValue);
        RemoveAllocationCommand = new RelayCommand<CostCenterAllocationRowViewModel>(RemoveAllocation, _ => CanManage && IsEditorOpen);

        InitializeHistoryCommands();
    }

    public ObservableCollection<CostCenterResponse> Items { get; } = [];
    public ObservableCollection<CostCenterTreeNode> CostCenterTree { get; } = [];
    public ObservableCollection<LookupItem> ParentCostCenterLookups { get; } = [];
    public ObservableCollection<LookupItem> TargetCostCenterLookups { get; } = [];
    public ObservableCollection<CostCenterAllocationRowViewModel> Allocations { get; } = [];

    public static IReadOnlyList<ChoiceOption<byte>> CenterTypeOptions { get; } =
    [
        new(1, "عادي (Ordinary)"),
        new(2, "فرعي تجميعي (Parent/Sub-Center)"),
        new(3, "رئيسي تجميعي (Aggregation)"),
        new(4, "توزيعي تحليلي (Distribution)")
    ];

    public decimal TotalAllocationPercentage => Allocations.Sum(a => a.Percentage);
    public bool IsDistributionType => CenterType == 4;

    public bool HasUnsavedChanges => IsEditorOpen && CanManage && (IsNew ? Code.Length > 0 || NameArabic.Length > 0 || !string.IsNullOrEmpty(NameEnglish)
        : editing is not null && (Code != editing.Code || NameArabic != editing.NameArabic || (string.IsNullOrWhiteSpace(NameEnglish) ? null : NameEnglish.Trim()) != editing.NameEnglish
            || ParentId != editing.ParentId || CenterType != editing.CenterType || Description != editing.Description));

    public bool CanManage => Can(ManagePermission);
    public bool CanViewGeneralJournal => Can("Ledger.Reports.View");
    public bool CanEditCode => CanManage && IsEditorOpen && editing?.HasJournalLines != true;
    public bool CanEditDetails => CanManage && IsEditorOpen;
    public bool IsActive => editing?.IsActive ?? true;
    public string StatusText => Localization[IsActive ? "D_Active" : "D_Inactive"];
    public BadgeKind StatusKind => IsActive ? BadgeKind.Success : BadgeKind.Neutral;
    public string ToggleActiveLabel => Localization[IsActive ? "D_Deactivate" : "D_Activate"];
    public string PageStatus => string.Format(Localization.Culture, Localization["D_PageStatus"], Page, Items.Count, TotalCount);
    public string EditorTitle => IsNew ? Localization["D_NewCostCenter"] : editing is null ? string.Empty : editing.Code + " — " + (Localization.IsRightToLeft ? editing.NameArabic : editing.NameEnglish ?? editing.NameArabic);

    public AsyncRelayCommand SearchCommand { get; }
    public AsyncRelayCommand PreviousPageCommand { get; }
    public AsyncRelayCommand NextPageCommand { get; }
    public RelayCommand NewCommand { get; }
    public RelayCommand NewChildCommand { get; }
    public AsyncRelayCommand SaveCommand { get; }
    public AsyncRelayCommand ToggleActiveCommand { get; }
    public AsyncRelayCommand OpenGeneralJournalCommand { get; }
    public AsyncRelayCommand OpenCostCenterLedgerCommand { get; }
    public AsyncRelayCommand OpenCostCenterTrialBalanceCommand { get; }

    public RelayCommand AddAllocationCommand { get; }
    public RelayCommand<CostCenterAllocationRowViewModel> RemoveAllocationCommand { get; }

    private Task OpenGeneralJournalAsync() =>
        editing is null || !CanViewGeneralJournal ? Task.CompletedTask : navigator.NavigateAsync("general-journal", new OpenGeneralJournalRequest(CostCenterId: editing.Id));

    private Task OpenLedgerAsync() =>
        editing is null || !CanViewGeneralJournal ? Task.CompletedTask : navigator.NavigateAsync("account-ledger", new OpenAccountLedgerRequest(Guid.Empty, null, null, false));

    private Task OpenTrialBalanceAsync() =>
        editing is null || !CanViewGeneralJournal ? Task.CompletedTask : navigator.NavigateAsync("trial-balance", new OpenTrialBalanceRequest());

    protected override async Task LoadAsync(object? parameter, CancellationToken cancellationToken)
    {
        await LoadTreeAsync(cancellationToken).ConfigureAwait(true);
        await LoadPageAsync(Page, editing?.Id, cancellationToken).ConfigureAwait(true);
    }

    protected override void ClearPage()
    {
        Items.Clear(); CostCenterTree.Clear(); ParentCostCenterLookups.Clear(); TargetCostCenterLookups.Clear(); Allocations.Clear();
        editing = null; Selected = null; IsEditorOpen = false; IsNew = false; Page = 1; TotalCount = 0; Search = string.Empty; ActiveFilter = null;
        ClearHistory();
        Notify();
    }

    private async Task LoadTreeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var treeItems = await client.GetCostCenterTreeAsync(CompanyId, BranchId, cancellationToken).ConfigureAwait(true);
            CostCenterTree.Clear();
            ParentCostCenterLookups.Clear();
            TargetCostCenterLookups.Clear();

            var nodeMap = treeItems.ToDictionary(c => c.Id, c => new CostCenterTreeNode
            {
                Id = c.Id,
                Code = c.Code,
                NameArabic = c.NameArabic,
                NameEnglish = c.NameEnglish,
                CenterType = (byte)(c.CenterType ?? 1),
                IsActive = c.IsActive,
                IsExpanded = true
            });

            foreach (var item in treeItems)
            {
                if (item.ParentId.HasValue && nodeMap.TryGetValue(item.ParentId.Value, out var parentNode))
                {
                    parentNode.Children.Add(nodeMap[item.Id]);
                }
                else if (nodeMap.TryGetValue(item.Id, out var rootNode))
                {
                    CostCenterTree.Add(rootNode);
                }

                if (editing is null || item.Id != editing.Id)
                {
                    ParentCostCenterLookups.Add(new LookupItem(item.Id, item.Code, item.NameArabic, item.NameEnglish));
                    if (item.CenterType != 4) // Target of distribution cannot be another distribution center
                    {
                        TargetCostCenterLookups.Add(new LookupItem(item.Id, item.Code, item.NameArabic, item.NameEnglish));
                    }
                }
            }
        }
        catch
        {
            // Graceful fallback
        }
    }

    private async Task LoadPageAsync(int target, Guid? select, CancellationToken cancellationToken)
    {
        var result = await client.SearchCostCentersAsync(CompanyId, BranchId, string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(), ActiveFilter,
            Math.Max(1, target), PageSize, cancellationToken).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        Items.Clear();
        foreach (var item in result.Items) Items.Add(item);
        Page = result.Page; TotalCount = result.TotalCount;
        if (select is { } id && Items.FirstOrDefault(item => item.Id == id) is { } found) Selected = found;
        Notify();
    }

    partial void OnCenterTypeChanged(byte value)
    {
        OnPropertyChanged(nameof(IsDistributionType));
    }

    partial void OnSelectedChanged(CostCenterResponse? value)
    {
        ClearHistory();
        if (value is null) return;
        editing = value; IsNew = false;
        Code = value.Code; NameArabic = value.NameArabic; NameEnglish = value.NameEnglish;
        ParentId = value.ParentId;
        CenterType = (byte)(value.CenterType ?? 1);
        Description = value.Description;
        StartDate = value.StartDate is { } sd ? sd.ToDateTime(TimeOnly.MinValue) : null;
        EndDate = value.EndDate is { } ed ? ed.ToDateTime(TimeOnly.MinValue) : null;
        ResponsiblePerson = value.ResponsiblePerson;
        IsEditorOpen = true;

        _ = LoadAllocationsAsync(value.Id);
        Notify();
    }

    public void SelectTreeNode(CostCenterTreeNode node)
    {
        var match = Items.FirstOrDefault(i => i.Id == node.Id);
        if (match is not null)
        {
            Selected = match;
        }
        else
        {
            _ = RunAsync(async ct =>
            {
                var response = await client.GetCostCenterTreeAsync(CompanyId, BranchId, ct).ConfigureAwait(true);
                var found = response.FirstOrDefault(r => r.Id == node.Id);
                if (found is not null)
                {
                    editing = found; IsNew = false;
                    Code = found.Code; NameArabic = found.NameArabic; NameEnglish = found.NameEnglish;
                    ParentId = found.ParentId;
                    CenterType = (byte)(found.CenterType ?? 1);
                    Description = found.Description;
                    StartDate = found.StartDate is { } sd ? sd.ToDateTime(TimeOnly.MinValue) : null;
                    EndDate = found.EndDate is { } ed ? ed.ToDateTime(TimeOnly.MinValue) : null;
                    ResponsiblePerson = found.ResponsiblePerson;
                    IsEditorOpen = true;
                    await LoadAllocationsAsync(found.Id).ConfigureAwait(true);
                    Notify();
                }
            });
        }
    }

    private async Task LoadAllocationsAsync(Guid costCenterId)
    {
        Allocations.Clear();
        if (CenterType != 4) return;
        try
        {
            var allocs = await client.GetCostCenterAllocationsAsync(CompanyId, BranchId, costCenterId, Token).ConfigureAwait(true);
            foreach (var a in allocs)
            {
                Allocations.Add(new CostCenterAllocationRowViewModel(a.TargetCostCenterId ?? Guid.Empty, a.TargetCode ?? string.Empty, a.TargetNameArabic ?? string.Empty, decimal.TryParse(a.Percentage, System.Globalization.CultureInfo.InvariantCulture, out var p) ? p : 0m));
            }
            OnPropertyChanged(nameof(TotalAllocationPercentage));
        }
        catch
        {
            // Graceful fallback
        }
    }

    private void StartNew()
    {
        editing = null; IsNew = true; Code = string.Empty; NameArabic = string.Empty; NameEnglish = null;
        ParentId = null; CenterType = 1; Description = null; StartDate = null; EndDate = null; ResponsiblePerson = null;
        Allocations.Clear();
        IsEditorOpen = true;
        ClearHistory();
        Notify();
    }

    private void StartNewChild()
    {
        var currentParentId = editing?.Id;
        StartNew();
        ParentId = currentParentId;
        Notify();
    }

    private void AddAllocation()
    {
        if (!NewAllocationTargetId.HasValue) return;
        if (!decimal.TryParse(NewAllocationPercentage, out var pct) || pct <= 0m || pct > 100m)
        {
            AllocationError = "النسبة يجب أن تكون بين 0.01 و 100.00";
            return;
        }

        var target = TargetCostCenterLookups.FirstOrDefault(t => t.Id == NewAllocationTargetId.Value);
        if (target is null) return;

        var existing = Allocations.FirstOrDefault(a => a.TargetCostCenterId == NewAllocationTargetId.Value);
        if (existing is not null)
        {
            existing.Percentage = pct;
        }
        else
        {
            Allocations.Add(new CostCenterAllocationRowViewModel(target.Id, target.Code, target.NameArabic, pct));
        }

        NewAllocationTargetId = null;
        NewAllocationPercentage = "0.00";
        AllocationError = string.Empty;
        OnPropertyChanged(nameof(TotalAllocationPercentage));
    }

    private void RemoveAllocation(CostCenterAllocationRowViewModel? row)
    {
        if (row is null) return;
        Allocations.Remove(row);
        OnPropertyChanged(nameof(TotalAllocationPercentage));
    }

    private async Task SaveAsync()
    {
        if (!CanManage || !Idle) return;
        CodeError = string.IsNullOrWhiteSpace(Code) ? Localization["D_Required"] : string.Empty;
        NameError = string.IsNullOrWhiteSpace(NameArabic) ? Localization["D_Required"] : string.Empty;
        if (CodeError.Length > 0 || NameError.Length > 0) return;

        if (CenterType == 4)
        {
            var sum = TotalAllocationPercentage;
            if (sum != 100.00m)
            {
                AllocationError = $"مجموع نسب التوزيع يجب أن يساوي 100.00% تماماً (المجموع الحالي: {sum:F2}%)";
                return;
            }
        }

        AllocationError = string.Empty;
        var request = new CostCenterRequest
        {
            Code = Code.Trim(),
            NameArabic = NameArabic.Trim(),
            NameEnglish = string.IsNullOrWhiteSpace(NameEnglish) ? null : NameEnglish.Trim(),
            ParentId = ParentId,
            CenterType = CenterType,
            Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
            StartDate = StartDate is { } s ? DateOnly.FromDateTime(s) : null,
            EndDate = EndDate is { } e ? DateOnly.FromDateTime(e) : null,
            ResponsiblePerson = string.IsNullOrWhiteSpace(ResponsiblePerson) ? null : ResponsiblePerson.Trim()
        };

        var target = editing;
        if (await RunAsync(async token =>
            {
                var saved = target is null
                    ? await client.CreateCostCenterAsync(CompanyId, BranchId, request, token).ConfigureAwait(true)
                    : await client.UpdateCostCenterAsync(CompanyId, BranchId, target.Id, target.Version, request, token).ConfigureAwait(true);

                if (CenterType == 4 && saved.Id != Guid.Empty)
                {
                    await client.SetCostCenterAllocationsAsync(CompanyId, BranchId, saved.Id, new SetCostCenterAllocationsRequest
                    {
                        Allocations = Allocations.Select(a => new CostCenterAllocationItemDto
                        {
                            TargetCostCenterId = a.TargetCostCenterId,
                            Percentage = a.Percentage.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                        }).ToList()
                    }, token).ConfigureAwait(true);
                }

                await LoadTreeAsync(token).ConfigureAwait(true);
                await LoadPageAsync(Page, saved.Id, token).ConfigureAwait(true);
            }).ConfigureAwait(true))
        {
            notifications.Success("D_Saved");
        }
    }

    private async Task ToggleActiveAsync()
    {
        if (!CanManage || !Idle) return;
        if (editing is null) return;
        var target = editing; var activate = !target.IsActive;
        if (await RunAsync(async token =>
            {
                await client.SetCostCenterActiveAsync(CompanyId, BranchId, target.Id, new SetActiveRequest { ExpectedVersion = target.Version, IsActive = activate }, token).ConfigureAwait(true);
                await LoadTreeAsync(token).ConfigureAwait(true);
                await LoadPageAsync(Page, target.Id, token).ConfigureAwait(true);
            }).ConfigureAwait(true))
        {
            notifications.Success(activate ? "D_Activated" : "D_Deactivated");
        }
    }

    public void Localize() => Notify();

    private void Notify()
    {
        CodeError = string.Empty; NameError = string.Empty;
        OnPropertyChanged(nameof(CanEditCode)); OnPropertyChanged(nameof(CanEditDetails)); OnPropertyChanged(nameof(IsActive)); OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusKind)); OnPropertyChanged(nameof(ToggleActiveLabel)); OnPropertyChanged(nameof(PageStatus)); OnPropertyChanged(nameof(EditorTitle));
        OnPropertyChanged(nameof(IsDistributionType)); OnPropertyChanged(nameof(TotalAllocationPercentage));
        OnSelectedHistoryChanged(SelectedHistory);
        NotifyCommands();
    }

    protected override void NotifyCommands()
    {
        OnPropertyChanged(nameof(CanManage)); OnPropertyChanged(nameof(CanViewGeneralJournal)); OnPropertyChanged(nameof(CanEditCode)); OnPropertyChanged(nameof(CanEditDetails));
        SearchCommand?.NotifyCanExecuteChanged(); PreviousPageCommand?.NotifyCanExecuteChanged(); NextPageCommand?.NotifyCanExecuteChanged();
        NewCommand?.NotifyCanExecuteChanged(); NewChildCommand?.NotifyCanExecuteChanged(); SaveCommand?.NotifyCanExecuteChanged(); ToggleActiveCommand?.NotifyCanExecuteChanged();
        OpenGeneralJournalCommand?.NotifyCanExecuteChanged(); OpenCostCenterLedgerCommand?.NotifyCanExecuteChanged(); OpenCostCenterTrialBalanceCommand?.NotifyCanExecuteChanged();
        AddAllocationCommand?.NotifyCanExecuteChanged(); RemoveAllocationCommand?.NotifyCanExecuteChanged();
        NotifyHistory();
    }

    partial void OnIsEditorOpenChanged(bool value) => NotifyCommands();
}
