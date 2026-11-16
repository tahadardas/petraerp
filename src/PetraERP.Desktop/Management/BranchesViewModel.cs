using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Accounting;
using PetraERP.Desktop.Currencies;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Management;

/// <summary>One flattened branch-tree row; <see cref="DisplayName"/> is indented by depth.</summary>
public sealed record BranchRow(BranchCardDto Card, int Depth)
{
    public string DisplayName => new string(' ', Depth * 4) + (Depth > 0 ? "└ " : string.Empty) + Card.NameArabic;
}

/// <summary>
/// Branch directory + tree, branch card (create / edit / activate), effective-dated account mappings and the sub-account preview.
/// All rules (loops, last active branch, cross-branch denial, history) are enforced by the server.
/// </summary>
public sealed partial class BranchesViewModel : PageViewModel
{
    public const string ViewPermission = "System.Branches.View";
    public const string ManagePermission = "System.Branches.Manage";

    private readonly IManagementClient client;
    private readonly IChartOfAccountsClient accountsClient;
    private readonly NotificationCenter notifications;

    [ObservableProperty] private BranchRow? selectedBranch;
    [ObservableProperty] private string code = string.Empty;
    [ObservableProperty] private string nameArabic = string.Empty;
    [ObservableProperty] private string? nameEnglish;
    [ObservableProperty] private Guid? parentBranchId;
    [ObservableProperty] private string? numberPrefix;
    [ObservableProperty] private string? address;
    [ObservableProperty] private string? phone;
    [ObservableProperty] private string? email;
    [ObservableProperty] private string? notes;
    [ObservableProperty] private string reason = string.Empty;
    [ObservableProperty] private string linkRole = "Cash";
    [ObservableProperty] private Guid? linkAccountId;
    [ObservableProperty] private DateTime linkEffectiveFrom = DateTime.Today;
    [ObservableProperty] private Guid? modelBranchId;
    [ObservableProperty] private string? applyResult;

    public BranchesViewModel(IManagementClient client, IChartOfAccountsClient accountsClient, NotificationCenter notifications, DesktopOperation operation, ILocalizationService localization)
        : base(operation, localization)
    {
        this.client = client;
        this.accountsClient = accountsClient;
        this.notifications = notifications;
        RefreshCommand = new AsyncRelayCommand(() => RunAsync(LoadAllAsync), () => HasContext && Idle);
        NewCommand = new RelayCommand(NewBranch, () => HasContext && CanManage);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => HasContext && Idle && CanManage && !string.IsNullOrWhiteSpace(Code) && !string.IsNullOrWhiteSpace(NameArabic)
            && (SelectedBranch is null || !string.IsNullOrWhiteSpace(Reason)));
        ToggleActiveCommand = new AsyncRelayCommand(ToggleActiveAsync, () => HasContext && Idle && CanManage && SelectedBranch is not null && !string.IsNullOrWhiteSpace(Reason));
        SaveLinkCommand = new AsyncRelayCommand(SaveLinkAsync, () => HasContext && Idle && CanManage && SelectedBranch is not null && LinkAccountId.HasValue && !string.IsNullOrWhiteSpace(Reason));
        PreviewSubAccountsCommand = new AsyncRelayCommand(PreviewSubAccountsAsync, () => HasContext && Idle && SelectedBranch is not null && ModelBranchId.HasValue);
        ApplySubAccountsCommand = new AsyncRelayCommand(ApplySubAccountsAsync, () => HasContext && Idle && CanManage && Can("Ledger.Accounts.Manage") && SelectedBranch is not null
            && ModelBranchId.HasValue && ModelBranchId != SelectedBranch.Card.Id && !string.IsNullOrWhiteSpace(Reason));
    }

    public ObservableCollection<BranchRow> Branches { get; } = [];
    public ObservableCollection<BranchAccountLinkDto> Links { get; } = [];
    public ObservableCollection<BranchSubAccountPlanDto> SubAccountPlan { get; } = [];
    public ObservableCollection<AccountLookupItem> Accounts { get; } = [];
    public IReadOnlyList<string> Roles { get; } = ["Cash", "Bank", "Sales", "SalesReturns", "Purchases", "PurchaseReturns", "Inventory", "CostOfGoodsSold", "Receivable", "Payable", "InterBranchClearing"];

    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand NewCommand { get; }
    public AsyncRelayCommand SaveCommand { get; }
    public AsyncRelayCommand ToggleActiveCommand { get; }
    public AsyncRelayCommand SaveLinkCommand { get; }
    public AsyncRelayCommand PreviewSubAccountsCommand { get; }
    public AsyncRelayCommand ApplySubAccountsCommand { get; }
    public bool CanManage => Can(ManagePermission);

    protected override string RequiredPermission => ViewPermission;

    protected override Task LoadAsync(object? parameter, CancellationToken cancellationToken) => RunAsync(LoadAllAsync);

    protected override void ClearPage()
    {
        Branches.Clear(); Links.Clear(); SubAccountPlan.Clear(); Accounts.Clear(); SelectedBranch = null; ResetEditor();
    }

    protected override void NotifyCommands()
    {
        RefreshCommand?.NotifyCanExecuteChanged(); NewCommand?.NotifyCanExecuteChanged(); SaveCommand?.NotifyCanExecuteChanged();
        ToggleActiveCommand?.NotifyCanExecuteChanged(); SaveLinkCommand?.NotifyCanExecuteChanged(); PreviewSubAccountsCommand?.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanManage));
    }

    partial void OnCodeChanged(string value) => SaveCommand.NotifyCanExecuteChanged();
    partial void OnNameArabicChanged(string value) => SaveCommand.NotifyCanExecuteChanged();
    partial void OnReasonChanged(string value) => NotifyCommands();
    partial void OnModelBranchIdChanged(Guid? value) => NotifyCommands();
    partial void OnLinkAccountIdChanged(Guid? value) => SaveLinkCommand.NotifyCanExecuteChanged();

    partial void OnSelectedBranchChanged(BranchRow? value)
    {
        if (value is null) { ResetEditor(); }
        else
        {
            var c = value.Card;
            Code = c.Code ?? string.Empty; NameArabic = c.NameArabic ?? string.Empty; NameEnglish = c.NameEnglish; ParentBranchId = c.ParentBranchId;
            NumberPrefix = c.NumberPrefix; Address = c.Address; Phone = c.Phone; Email = c.Email; Notes = c.Notes;
            _ = LoadLinksAsync(value);
        }

        NotifyCommands();
    }

    private void ResetEditor()
    {
        Code = string.Empty; NameArabic = string.Empty; NameEnglish = null; ParentBranchId = null; NumberPrefix = null; Address = null;
        Phone = null; Email = null; Notes = null; Reason = string.Empty; Links.Clear(); SubAccountPlan.Clear();
    }

    private void NewBranch() { SelectedBranch = null; ResetEditor(); }

    private async Task LoadAllAsync(CancellationToken token)
    {
        var list = await client.ListBranchesAsync(CompanyId, BranchId, token).ConfigureAwait(true);
        var keep = SelectedBranch?.Card.Id;
        Branches.Clear();
        foreach (var row in Flatten(list)) Branches.Add(row);
        SelectedBranch = keep is null ? null : Branches.FirstOrDefault(b => b.Card.Id == keep);
        Accounts.Clear();
        if (Can("Ledger.Accounts.View"))
        {
            foreach (var a in (await accountsClient.GetAccountTreeAsync(CompanyId, BranchId, token).ConfigureAwait(true))
                         .Where(a => a.PostingAllowed == true && a.IsActive == true && a.Id != Guid.Empty).OrderBy(a => a.Code, StringComparer.Ordinal))
                Accounts.Add(new(a.Id, a.Code ?? string.Empty, a.NameArabic ?? string.Empty, a.NameEnglish));
        }
    }

    /// <summary>Depth-first flattening of the parent/child relation (roots first, siblings by code).</summary>
    internal static IEnumerable<BranchRow> Flatten(IReadOnlyList<BranchCardDto> all)
    {
        var ids = all.Where(b => b.Id.HasValue).Select(b => b.Id!.Value).ToHashSet();
        var children = all.Where(b => b.ParentBranchId.HasValue && ids.Contains(b.ParentBranchId.Value)).ToLookup(b => b.ParentBranchId!.Value);
        var roots = all.Where(b => !b.ParentBranchId.HasValue || !ids.Contains(b.ParentBranchId.Value)).OrderBy(b => b.Code, StringComparer.Ordinal);
        var stack = new HashSet<Guid>();
        IEnumerable<BranchRow> Walk(BranchCardDto node, int depth)
        {
            yield return new BranchRow(node, depth);
            if (node.Id is not { } id || !stack.Add(id)) yield break;
            foreach (var child in children[id].OrderBy(b => b.Code, StringComparer.Ordinal))
                foreach (var row in Walk(child, depth + 1)) yield return row;
            stack.Remove(id);
        }

        foreach (var root in roots)
            foreach (var row in Walk(root, 0)) yield return row;
    }

    private async Task SaveAsync()
    {
        string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        var selected = SelectedBranch?.Card;
        var ok = await RunAsync(async token =>
        {
            if (selected?.Id is { } id)
                await client.UpdateBranchAsync(CompanyId, BranchId, id, new UpdateBranchRequest
                {
                    Version = selected.Version ?? string.Empty, Reason = Reason.Trim(), Code = Code.Trim(), NameArabic = NameArabic.Trim(), NameEnglish = Clean(NameEnglish),
                    ParentBranchId = ParentBranchId, NumberPrefix = Clean(NumberPrefix), Address = Clean(Address), Phone = Clean(Phone), Email = Clean(Email), Notes = Clean(Notes),
                    DefaultWarehouseId = selected.DefaultWarehouseId, DefaultCashAccountId = selected.DefaultCashAccountId, DefaultBankAccountId = selected.DefaultBankAccountId,
                    DefaultCostCenterId = selected.DefaultCostCenterId,
                }, token).ConfigureAwait(true);
            else
                await client.CreateBranchAsync(CompanyId, BranchId, new SaveBranchRequest
                {
                    Code = Code.Trim(), NameArabic = NameArabic.Trim(), NameEnglish = Clean(NameEnglish), ParentBranchId = ParentBranchId, NumberPrefix = Clean(NumberPrefix),
                    Address = Clean(Address), Phone = Clean(Phone), Email = Clean(Email), Notes = Clean(Notes),
                }, token).ConfigureAwait(true);
            await LoadAllAsync(token).ConfigureAwait(true);
        }, OperationKind.Save).ConfigureAwait(true);
        if (ok) { notifications.Success("D_Saved"); Reason = string.Empty; }
    }

    private async Task ToggleActiveAsync()
    {
        if (SelectedBranch?.Card is not { Id: { } id } card) return;
        var ok = await RunAsync(async token =>
        {
            await client.SetBranchActiveAsync(CompanyId, BranchId, id, new SetBranchActiveRequest { IsActive = !(card.IsActive ?? true), Version = card.Version ?? string.Empty, Reason = Reason.Trim() }, token).ConfigureAwait(true);
            await LoadAllAsync(token).ConfigureAwait(true);
        }, OperationKind.Save).ConfigureAwait(true);
        if (ok) { notifications.Success("D_Saved"); Reason = string.Empty; }
    }

    private async Task LoadLinksAsync(BranchRow row)
    {
        if (row.Card.Id is not { } id || !HasContext) return;
        await RunAsync(async token =>
        {
            var links = await client.ListBranchAccountLinksAsync(CompanyId, BranchId, id, token).ConfigureAwait(true);
            if (SelectedBranch != row) return;
            Links.Clear();
            foreach (var l in links.OrderBy(l => l.Role, StringComparer.Ordinal).ThenByDescending(l => l.EffectiveFrom)) Links.Add(l);
        }).ConfigureAwait(true);
    }

    private async Task SaveLinkAsync()
    {
        if (SelectedBranch?.Card.Id is not { } id || LinkAccountId is not { } account) return;
        var ok = await RunAsync(async token =>
        {
            await client.SetBranchAccountLinkAsync(CompanyId, BranchId, id,
                new SetBranchAccountLinkRequest { Role = LinkRole, AccountId = account, EffectiveFrom = DateOnly.FromDateTime(LinkEffectiveFrom), Reason = Reason.Trim() }, token).ConfigureAwait(true);
            var links = await client.ListBranchAccountLinksAsync(CompanyId, BranchId, id, token).ConfigureAwait(true);
            Links.Clear();
            foreach (var l in links.OrderBy(l => l.Role, StringComparer.Ordinal).ThenByDescending(l => l.EffectiveFrom)) Links.Add(l);
        }, OperationKind.Save).ConfigureAwait(true);
        if (ok) notifications.Success("D_Saved");
    }

    private async Task PreviewSubAccountsAsync()
    {
        if (SelectedBranch?.Card.Id is not { } id || ModelBranchId is null) return;
        await RunAsync(async token =>
        {
            var plan = await client.PreviewBranchSubAccountsAsync(CompanyId, BranchId, id, ModelBranchId!.Value, token).ConfigureAwait(true);
            SubAccountPlan.Clear();
            foreach (var p in plan) SubAccountPlan.Add(p);
        }).ConfigureAwait(true);
    }

    private async Task ApplySubAccountsAsync()
    {
        if (SelectedBranch?.Card.Id is not { } id || ModelBranchId is not { } model) return;
        BranchSubAccountApplyResponse? response = null;
        var ok = await RunAsync(async token =>
        {
            response = await client.ApplyBranchSubAccountsAsync(CompanyId, BranchId, id,
                new ApplyBranchSubAccountsRequest { ModelBranchId = model, EffectiveFrom = DateOnly.FromDateTime(LinkEffectiveFrom), Reason = Reason.Trim() }, token).ConfigureAwait(true);
            var links = await client.ListBranchAccountLinksAsync(CompanyId, BranchId, id, token).ConfigureAwait(true);
            Links.Clear();
            foreach (var l in links.OrderBy(l => l.Role, StringComparer.Ordinal).ThenByDescending(l => l.EffectiveFrom)) Links.Add(l);
        }, OperationKind.Save).ConfigureAwait(true);
        if (ok && response is not null)
        {
            ApplyResult = $"أُنشئ {(response.Items ?? []).Count(i => i.AccountCreated == true)} حساباً وربط {(response.Items ?? []).Count(i => i.LinkChanged == true)} دوراً";
            notifications.Success("D_Saved");
        }
    }
}
