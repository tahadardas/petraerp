using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Controls;
using PetraERP.Desktop.GeneralLedger;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Accounting;

/// <summary>One account in the flattened, virtualized tree.</summary>
public sealed partial class AccountNode(AccountResponse account) : ObservableObject
{
    [ObservableProperty] private bool isExpanded = true;
    public AccountResponse Account { get; } = account;
    public Guid Id => Account.Id;
    public int Level => Account.Level;
    public double Indent => Account.Level * 16;
    public bool HasChildren => Account.HasChildren;
    public string ExpanderGlyph => !HasChildren ? string.Empty : IsExpanded ? "" : "";
    public string AccountIconGlyph => !HasChildren ? "" : IsExpanded ? "" : "";
    public string AccountIconColor => !HasChildren ? "#2E5AAC" : "#E5A93C";
    partial void OnIsExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(ExpanderGlyph));
        OnPropertyChanged(nameof(AccountIconGlyph));
    }
}

/// <summary>
/// Chart of accounts: the complete company hierarchy (one server tree call), local filtering over that complete list,
/// and an editor whose locks mirror the server rules (code, type, currency and posting are fixed once journal lines
/// exist). Save and activation go through the API; the server stays authoritative.
/// </summary>
public sealed partial class ChartOfAccountsViewModel : PageViewModel, ISavablePage
{
    protected override string RequiredPermission => "Ledger.Accounts.View";
    public const string ViewPermission = "Ledger.Accounts.View";
    public const string ManagePermission = "Ledger.Accounts.Manage";

    private readonly IChartOfAccountsClient client;
    private readonly IGeneralLedgerClient ledger;
    private readonly NotificationCenter notifications;
    private readonly ShellNavigator navigator;
    private readonly IUserConfirmation confirmation;
    private List<AccountNode> all = [];
    private AccountResponse? editing;

    [ObservableProperty] private string filter = string.Empty;
    [ObservableProperty] private bool onlyWithMovements;
    [ObservableProperty] private AccountNode? selectedNode;
    [ObservableProperty] private bool isEditorOpen;
    [ObservableProperty] private bool isNew;
    [ObservableProperty] private string code = string.Empty;
    [ObservableProperty] private string nameArabic = string.Empty;
    [ObservableProperty] private string? nameEnglish;
    [ObservableProperty] private AccountType accountType = AccountType.Assets;
    [ObservableProperty] private bool postingAllowed = true;
    [ObservableProperty] private CostCenterPolicy costCenterPolicy = CostCenterPolicy.Optional;
    [ObservableProperty] private AccountCurrencyMode currencyMode = AccountCurrencyMode.BaseCurrencyOnly;
    [ObservableProperty] private string? fixedCurrencyCode;
    [ObservableProperty] private Guid? parentId;
    [ObservableProperty] private string codeError = string.Empty;
    [ObservableProperty] private string nameError = string.Empty;
    [ObservableProperty] private bool isDeletePending;
    [ObservableProperty] private string deleteReason = string.Empty;

    public ChartOfAccountsViewModel(IChartOfAccountsClient client, IGeneralLedgerClient ledger, ShellNavigator navigator, DesktopOperation operation, ILocalizationService localization,
        NotificationCenter notifications, IUserConfirmation confirmation) : base(operation, localization)
    {
        this.client = client; this.ledger = ledger; this.navigator = navigator; this.notifications = notifications; this.confirmation = confirmation;
        NewCommand = new RelayCommand(NewAccount, () => CanManage && Idle);
        NewChildCommand = new RelayCommand(NewChild, () => CanManage && Idle && SelectedNode is { Account.PostingAllowed: false, Account.IsActive: true });
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => CanManage && IsEditorOpen && Idle && RequiredFieldsComplete);
        PropertyChanged += (_, args) => { if (args.PropertyName is nameof(Code) or nameof(NameArabic) or nameof(FixedCurrencyCode) or nameof(CurrencyMode)) SaveCommand.NotifyCanExecuteChanged(); };
        ToggleActiveCommand = new AsyncRelayCommand(ToggleActiveAsync, () => CanManage && !IsNew && editing is not null && Idle);
        RefreshCommand = new AsyncRelayCommand(() => RunAsync(token => ReloadAsync(SelectedNode?.Id, token)), () => HasContext && Idle);
        ToggleNodeCommand = new RelayCommand<AccountNode>(node => { if (node is { HasChildren: true }) { node.IsExpanded = !node.IsExpanded; Rebuild(); } });
        ExpandAllCommand = new RelayCommand(() => SetExpanded(true));
        ExpandLevelCommand = new RelayCommand<object>(arg =>
        {
            if (arg is int i) ExpandToLevel(i);
            else if (arg is string s && int.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, out var parsed)) ExpandToLevel(parsed);
            else ExpandToLevel(-1);
        });
        CollapseAllCommand = new RelayCommand(() => SetExpanded(false));
        // Tree context menu / keyboard: each command acts on the selected node (right-click selects the node first).
        OpenCardCommand = new AsyncRelayCommand(() => SelectedNode is { } node ? navigator.NavigateAsync("accounts", new OpenAccountCardRequest(node.Id)) : Task.CompletedTask,
            () => HasContext && Idle && SelectedNode is not null);
        OpenLedgerCommand = new AsyncRelayCommand(() => SelectedNode is { } node ? navigator.NavigateAsync("account-ledger", new OpenAccountLedgerRequest(node.Id, null, null, false)) : Task.CompletedTask,
            () => HasContext && Idle && SelectedNode is not null && CanViewLedger);
        EditCommand = new RelayCommand(EditSelected, () => CanManage && Idle && SelectedNode is not null);
        // Delete is offered only for an unused leaf; the server re-checks children and journal lines in the same transaction.
        RequestDeleteCommand = new RelayCommand(RequestDelete, () => CanManage && Idle && SelectedNode is { Account.HasChildren: false, Account.HasJournalLines: false });
        CancelDeleteCommand = new RelayCommand(() => { IsDeletePending = false; DeleteReason = string.Empty; });
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, () => CanDelete && IsDeletePending && Idle && !string.IsNullOrWhiteSpace(DeleteReason));
        Localize();
    }

    public ObservableCollection<AccountNode> Nodes { get; } = [];
    public ObservableCollection<AccountNode> SelectedNodeChildren { get; } = [];
    public IRelayCommand<object> ExpandLevelCommand { get; }
    public ObservableCollection<LookupItem> ParentOptions { get; } = [];
    public ObservableCollection<string> Currencies { get; } = [];
    public IReadOnlyList<ChoiceOption<AccountType>> AccountTypes { get; private set; } = [];
    public IReadOnlyList<ChoiceOption<CostCenterPolicy>> CostCenterPolicies { get; private set; } = [];
    public IReadOnlyList<ChoiceOption<AccountCurrencyMode>> CurrencyModes { get; private set; } = [];
    public int TotalCount => all.Count;
    public bool HasUnsavedChanges => IsEditorOpen && CanManage && (IsNew ? Code.Length > 0 || NameArabic.Length > 0 || !string.IsNullOrEmpty(NameEnglish)
        : editing is not null && (Code != editing.Code || NameArabic != editing.NameArabic || (string.IsNullOrWhiteSpace(NameEnglish) ? null : NameEnglish.Trim()) != editing.NameEnglish || AccountType != editing.AccountType
            || PostingAllowed != editing.PostingAllowed || ParentId != editing.ParentId || CostCenterPolicy != editing.CostCenterPolicy || CurrencyMode != editing.CurrencyMode || FixedCurrencyCode != editing.FixedCurrencyCode));
    public bool CanSaveBeforeClose => HasUnsavedChanges && SaveCommand.CanExecute(null);

    public async Task<bool> SaveBeforeCloseAsync()
    {
        if (!CanSaveBeforeClose) return false;
        await SaveCommand.ExecuteAsync(null).ConfigureAwait(true);
        return !HasUnsavedChanges;
    }

    public bool CanManage => Can(ManagePermission);
    public bool CanViewLedger => Can("Ledger.Reports.View");
    public bool HasJournalLines => editing?.HasJournalLines == true;
    public bool HasChildren => editing?.HasChildren == true;
    /// <summary>An unused leaf (no child, no journal line in any state) may be deleted; anything else can only be deactivated.</summary>
    public bool CanDelete => CanManage && IsEditorOpen && !IsNew && editing is { HasChildren: false, HasJournalLines: false };
    /// <summary>Code, type, currency policy and posting capability become read-only once journal lines reference the account.</summary>
    public bool CanEditStructure => CanManage && IsEditorOpen && !HasJournalLines;
    public bool CanEditPosting => CanEditStructure && !HasChildren;
    public bool CanEditDetails => CanManage && IsEditorOpen;
    public bool IsFixedCurrency => CurrencyMode == AccountCurrencyMode.FixedCurrency;
    public bool IsActive => editing?.IsActive ?? true;
    public string StatusText => Localization[IsActive ? "D_Active" : "D_Inactive"];
    public BadgeKind StatusKind => IsActive ? BadgeKind.Success : BadgeKind.Neutral;
    public string ToggleActiveLabel => Localization[IsActive ? "D_Deactivate" : "D_Activate"];
    public string EditorTitle => IsNew ? Localization["D_NewAccount"] : editing is null ? string.Empty : editing.Code + " — " + (Localization.IsRightToLeft ? editing.NameArabic : editing.NameEnglish ?? editing.NameArabic);

    public RelayCommand NewCommand { get; }
    public RelayCommand NewChildCommand { get; }
    public AsyncRelayCommand SaveCommand { get; }
    public AsyncRelayCommand ToggleActiveCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand<AccountNode> ToggleNodeCommand { get; }
    public RelayCommand ExpandAllCommand { get; }
    public RelayCommand CollapseAllCommand { get; }
    public AsyncRelayCommand OpenCardCommand { get; }
    public AsyncRelayCommand OpenLedgerCommand { get; }
    public RelayCommand EditCommand { get; }
    public RelayCommand RequestDeleteCommand { get; }
    public RelayCommand CancelDeleteCommand { get; }
    public AsyncRelayCommand DeleteCommand { get; }

    /// <summary>Raised after the server deleted an account, so the shell can close that account's card.</summary>
    public event EventHandler<Guid>? AccountDeleted;

    /// <summary>Raised by Delete so the view can focus the reason box.</summary>
    public event EventHandler? DeleteRequested;

    /// <summary>Raised by F2 / Edit so the view can move keyboard focus into the editor.</summary>
    public event EventHandler? EditRequested;

    public void Localize()
    {
        AccountTypes = Options.For<AccountType>(Localization, "D_AccountType_");
        CostCenterPolicies = Options.For<CostCenterPolicy>(Localization, "D_CostPolicy_");
        CurrencyModes = Options.For<AccountCurrencyMode>(Localization, "D_CurrencyMode_");
        OnPropertyChanged(nameof(AccountTypes)); OnPropertyChanged(nameof(CostCenterPolicies)); OnPropertyChanged(nameof(CurrencyModes));
        OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(ToggleActiveLabel)); OnPropertyChanged(nameof(EditorTitle));
    }

    protected override async Task LoadAsync(object? parameter, CancellationToken cancellationToken)
    {
        var context = await ledger.GetCurrencyContextAsync(CompanyId, BranchId, cancellationToken).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        Currencies.Clear();
        Currencies.Add(context.BaseCurrency.Code);
        foreach (var currency in context.AdditionalCurrencies) Currencies.Add(currency.Code);
        await ReloadAsync(parameter is OpenAccountRequest open ? open.AccountId : SelectedNode?.Id, cancellationToken).ConfigureAwait(true);
    }

    protected override void ClearPage()
    {
        ClearTemplatePreview();
        all = []; Nodes.Clear(); ParentOptions.Clear(); Currencies.Clear(); editing = null;
        Filter = string.Empty; SelectedNode = null; IsEditorOpen = false; IsNew = false; IsDeletePending = false; DeleteReason = string.Empty;
        OnPropertyChanged(nameof(TotalCount)); NotifyEditor();
    }

    private async Task ReloadAsync(Guid? select, CancellationToken cancellationToken)
    {
        var tree = await client.GetAccountTreeAsync(CompanyId, BranchId, cancellationToken).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        var expanded = all.Where(node => !node.IsExpanded).Select(node => node.Id).ToHashSet();
        all = tree.Select(account => new AccountNode(account) { IsExpanded = !expanded.Contains(account.Id) }).ToList();
        OnPropertyChanged(nameof(TotalCount));
        Rebuild();
        if (select is { } id && all.FirstOrDefault(node => node.Id == id) is { } node)
        {
            Reveal(node);
            SelectedNode = node;
        }
    }

    /// <summary>Visible rows: filter matches plus their ancestors; otherwise the expanded hierarchy.</summary>
    private void Rebuild()
    {
        Nodes.Clear();
        var term = Filter.Trim();

        // Al-Ameen style: filter active accounts with movements/journal lines
        HashSet<Guid>? movementFilter = null;
        if (OnlyWithMovements)
        {
            var byId = all.ToDictionary(node => node.Id);
            movementFilter = new HashSet<Guid>();
            foreach (var node in all.Where(node => node.Account.HasJournalLines))
            {
                for (var current = node.Account; current is not null; current = current.ParentId is { } parent && byId.TryGetValue(parent, out var p) ? p.Account : null)
                {
                    if (!movementFilter.Add(current.Id)) break;
                }
            }
        }

        if (term.Length > 0)
        {
            var byId = all.ToDictionary(node => node.Id);
            var include = new HashSet<Guid>();
            foreach (var node in all.Where(node => Matches(node.Account, term) && (movementFilter == null || movementFilter.Contains(node.Id))))
            {
                for (var current = node.Account; current is not null; current = current.ParentId is { } parent && byId.TryGetValue(parent, out var p) ? p.Account : null)
                {
                    if (!include.Add(current.Id)) break;
                }
            }

            foreach (var node in all.Where(node => include.Contains(node.Id))) Nodes.Add(node);
            return;
        }

        var hidden = new HashSet<Guid>();
        foreach (var node in all)
        {
            if (movementFilter != null && !movementFilter.Contains(node.Id)) continue;
            if (node.Account.ParentId is { } parent && hidden.Contains(parent)) { hidden.Add(node.Id); continue; }
            Nodes.Add(node);
            if (!node.IsExpanded) hidden.Add(node.Id);
        }
    }

    private static bool Matches(AccountResponse account, string term) => Controls.LookupText.Matches(term, account.Code, account.NameArabic, account.NameEnglish);

    private bool RequiredFieldsComplete => !string.IsNullOrWhiteSpace(Code) && !string.IsNullOrWhiteSpace(NameArabic)
        && (!IsFixedCurrency || Controls.RequiredInput.Currency(FixedCurrencyCode));

    private void Reveal(AccountNode node)
    {
        var byId = all.ToDictionary(item => item.Id);
        var changed = false;
        for (var parent = node.Account.ParentId; parent is { } id && byId.TryGetValue(id, out var ancestor); parent = ancestor.Account.ParentId)
        {
            if (!ancestor.IsExpanded) { ancestor.IsExpanded = true; changed = true; }
        }

        if (changed) Rebuild();
    }

    public void ExpandToLevel(int level)
    {
        foreach (var node in all.Where(node => node.HasChildren))
            node.IsExpanded = level <= 0 || node.Level < level;
        Rebuild();
    }

    private void UpdateSelectedChildren(AccountNode? node)
    {
        SelectedNodeChildren.Clear();
        if (node is null) return;
        var children = all.Where(item => item.Account.ParentId == node.Id).ToList();
        if (children.Count == 0 && node.Account.ParentId is { } parentId)
            children = all.Where(item => item.Account.ParentId == parentId).ToList();
        foreach (var child in children) SelectedNodeChildren.Add(child);
    }

    private void SetExpanded(bool value)
    {
        foreach (var node in all.Where(node => node.HasChildren)) node.IsExpanded = value;
        Rebuild();
    }

    partial void OnFilterChanged(string value) => Rebuild();
    partial void OnOnlyWithMovementsChanged(bool value) => Rebuild();

    partial void OnSelectedNodeChanged(AccountNode? value)
    {
        if (value is not null) Edit(value.Account);
        UpdateSelectedChildren(value);
        NotifyCommands();
    }

    partial void OnAccountTypeChanged(AccountType value) => RefreshParents();
    partial void OnCurrencyModeChanged(AccountCurrencyMode value)
    {
        if (value != AccountCurrencyMode.FixedCurrency) FixedCurrencyCode = null;
        else FixedCurrencyCode ??= Currencies.FirstOrDefault();
        OnPropertyChanged(nameof(IsFixedCurrency));
    }

    private void Edit(AccountResponse account)
    {
        IsDeletePending = false; DeleteReason = string.Empty;
        editing = account;
        IsNew = false;
        Code = account.Code; NameArabic = account.NameArabic; NameEnglish = account.NameEnglish;
        AccountType = account.AccountType; PostingAllowed = account.PostingAllowed; CostCenterPolicy = account.CostCenterPolicy;
        CurrencyMode = account.CurrencyMode; FixedCurrencyCode = account.FixedCurrencyCode; ParentId = account.ParentId;
        IsEditorOpen = true;
        RefreshParents();
        NotifyEditor();
    }

    /// <summary>F2 / Edit: reloads the selected account into the editor (discarding nothing that is not already shown) and asks the view to focus it.</summary>
    private void EditSelected()
    {
        if (SelectedNode is null) return;
        if (IsNew || editing?.Id != SelectedNode.Id) Edit(SelectedNode.Account);
        EditRequested?.Invoke(this, EventArgs.Empty);
    }

    private void NewAccount() => StartNew(null);

    private void NewChild() => StartNew(SelectedNode?.Account);

    private void StartNew(AccountResponse? parent)
    {
        IsDeletePending = false; DeleteReason = string.Empty;
        editing = null;
        IsNew = true;
        Code = parent is null
            ? string.Empty
            : GenerateNextChildCode(parent.Code, all.Where(n => n.Account.ParentId == parent.Id).Select(n => n.Account.Code));
        NameArabic = string.Empty; NameEnglish = null;
        AccountType = parent?.AccountType ?? AccountType.Assets;
        PostingAllowed = true; CostCenterPolicy = CostCenterPolicy.Optional; CurrencyMode = AccountCurrencyMode.BaseCurrencyOnly;
        FixedCurrencyCode = null; ParentId = parent?.Id;
        IsEditorOpen = true;
        RefreshParents();
        NotifyEditor();
    }

    /// <summary>
    /// Al-Ameen style auto-generation of next available sub-account code.
    /// E.g. parent "1101" with children "110101", "110102" -> suggests "110103".
    /// </summary>
    public static string GenerateNextChildCode(string parentCode, IEnumerable<string> existingChildrenCodes)
    {
        if (string.IsNullOrWhiteSpace(parentCode)) return string.Empty;

        var suffixes = new List<(long Value, int Length)>();
        foreach (var childCode in existingChildrenCodes)
        {
            if (childCode.StartsWith(parentCode, StringComparison.Ordinal) && childCode.Length > parentCode.Length)
            {
                var rawSuffix = childCode[parentCode.Length..];
                if (long.TryParse(rawSuffix, out var num) && num > 0)
                {
                    suffixes.Add((num, rawSuffix.Length));
                }
            }
        }

        if (suffixes.Count == 0)
        {
            // Default 2-digit padding ("01") for sub-accounts
            return parentCode + "01";
        }

        var maxNum = suffixes.Max(s => s.Value);
        var targetLength = suffixes.Max(s => s.Length);
        var nextNum = maxNum + 1;
        var nextSuffix = nextNum.ToString().PadLeft(targetLength, '0');
        return parentCode + nextSuffix;
    }

    /// <summary>Valid parents: active control accounts of the same type that are not the account itself or its descendants.</summary>
    private void RefreshParents()
    {
        var excluded = new HashSet<Guid>();
        if (editing is not null)
        {
            excluded.Add(editing.Id);
            foreach (var node in all)
            {
                if (node.Account.ParentId is { } parent && excluded.Contains(parent)) excluded.Add(node.Id);
            }
        }

        ParentOptions.Clear();
        foreach (var node in all.Where(node => !node.Account.PostingAllowed && node.Account.IsActive && node.Account.AccountType == AccountType && !excluded.Contains(node.Id)))
        {
            ParentOptions.Add(new LookupItem(node.Id, node.Account.Code, node.Account.NameArabic, node.Account.NameEnglish));
        }

        if (ParentId is { } id && !ParentOptions.Any(option => option.Id == id) && !HasJournalLines) ParentId = null;
    }

    private bool ValidateInput()
    {
        CodeError = string.IsNullOrWhiteSpace(Code) ? Localization["D_Required"] : string.Empty;
        NameError = string.IsNullOrWhiteSpace(NameArabic) ? Localization["D_Required"] : string.Empty;
        return CodeError.Length == 0 && NameError.Length == 0;
    }

    private AccountRequest Request() => new()
    {
        Code = Code.Trim(),
        NameArabic = NameArabic.Trim(),
        NameEnglish = string.IsNullOrWhiteSpace(NameEnglish) ? null : NameEnglish.Trim(),
        AccountType = AccountType,
        PostingAllowed = PostingAllowed,
        CostCenterPolicy = CostCenterPolicy,
        CurrencyMode = CurrencyMode,
        FixedCurrencyCode = CurrencyMode == AccountCurrencyMode.FixedCurrency ? FixedCurrencyCode : null,
        ParentId = ParentId,
    };

    private async Task SaveAsync()
    {
        if (!CanManage || !Idle) return;
        if (!ValidateInput()) return;
        var request = Request();
        var saved = await RunAsync(async token =>
        {
            var result = IsNew || editing is null
                ? await client.CreateAccountAsync(CompanyId, BranchId, request, token).ConfigureAwait(true)
                : await client.UpdateAccountAsync(CompanyId, BranchId, editing.Id, editing.Version, request, token).ConfigureAwait(true);
            await ReloadAsync(result.Id, token).ConfigureAwait(true);
        }).ConfigureAwait(true);
        if (saved) notifications.Success("D_Saved");
    }

    private async Task ToggleActiveAsync()
    {
        if (!CanManage || !Idle) return;
        if (editing is null) return;
        var target = !editing.IsActive;
        var request = new SetActiveRequest { ExpectedVersion = editing.Version, IsActive = target };
        var id = editing.Id;
        if (await RunAsync(async token =>
            {
                await client.SetAccountActiveAsync(CompanyId, BranchId, id, request, token).ConfigureAwait(true);
                await ReloadAsync(id, token).ConfigureAwait(true);
            }).ConfigureAwait(true))
        {
            notifications.Success(target ? "D_Activated" : "D_Deactivated");
        }
    }

    private void RequestDelete()
    {
        if (SelectedNode is null) return;
        if (IsNew || editing?.Id != SelectedNode.Id) Edit(SelectedNode.Account);
        if (!CanDelete) return;
        IsDeletePending = true;
        DeleteRequested?.Invoke(this, EventArgs.Empty);
    }

    private async Task DeleteAsync()
    {
        if (!CanDelete || !IsDeletePending || editing is null || string.IsNullOrWhiteSpace(DeleteReason)) return;
        if (!await confirmation.ConfirmAsync("D_ConfirmDeleteAccount").ConfigureAwait(true)) return;
        var account = editing;
        var request = new DeleteAccountRequest { ExpectedVersion = account.Version, Reason = DeleteReason.Trim() };
        if (await RunAsync(async token =>
            {
                await client.DeleteAccountAsync(CompanyId, BranchId, account.Id, request, token).ConfigureAwait(true);
                editing = null; IsEditorOpen = false; IsDeletePending = false; DeleteReason = string.Empty; SelectedNode = null;
                await ReloadAsync(account.ParentId, token).ConfigureAwait(true);
            }).ConfigureAwait(true))
        {
            notifications.Success("D_AccountDeleted");
            AccountDeleted?.Invoke(this, account.Id);
        }
    }

    partial void OnIsDeletePendingChanged(bool value) => DeleteCommand?.NotifyCanExecuteChanged();
    partial void OnDeleteReasonChanged(string value) => DeleteCommand?.NotifyCanExecuteChanged();

    private void NotifyEditor()
    {
        CodeError = string.Empty; NameError = string.Empty;
        OnPropertyChanged(nameof(HasJournalLines)); OnPropertyChanged(nameof(HasChildren)); OnPropertyChanged(nameof(CanEditStructure)); OnPropertyChanged(nameof(CanEditPosting));
        OnPropertyChanged(nameof(CanEditDetails)); OnPropertyChanged(nameof(IsActive)); OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusKind)); OnPropertyChanged(nameof(ToggleActiveLabel)); OnPropertyChanged(nameof(EditorTitle));
        OnPropertyChanged(nameof(IsFixedCurrency)); OnPropertyChanged(nameof(CanDelete));
        NotifyCommands();
    }

    protected override void NotifyCommands()
    {
        PreviewTemplateFileCommand.NotifyCanExecuteChanged(); ImportTemplateCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanManage)); OnPropertyChanged(nameof(CanEditStructure)); OnPropertyChanged(nameof(CanEditPosting)); OnPropertyChanged(nameof(CanEditDetails));
        NewCommand?.NotifyCanExecuteChanged(); NewChildCommand?.NotifyCanExecuteChanged(); SaveCommand?.NotifyCanExecuteChanged();
        ToggleActiveCommand?.NotifyCanExecuteChanged(); RefreshCommand?.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanViewLedger));
        OpenCardCommand?.NotifyCanExecuteChanged(); OpenLedgerCommand?.NotifyCanExecuteChanged(); EditCommand?.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanDelete)); RequestDeleteCommand?.NotifyCanExecuteChanged(); DeleteCommand?.NotifyCanExecuteChanged();
    }

    partial void OnIsEditorOpenChanged(bool value) => NotifyEditor();
}
