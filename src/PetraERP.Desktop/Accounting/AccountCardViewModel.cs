using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Controls;
using PetraERP.Desktop.GeneralLedger;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Accounting;

/// <summary>
/// Account card: one document per account (scoped to company and branch) with general data, posting and currency
/// policies, the hierarchy around the account and a server-computed ledger summary. It opens in View mode; Edit changes
/// the code (only while no journal line exists), names and cost-center policy with the account's row version, and New
/// creates a sub-account of a control account. Type, currency policy, posting capability and parent stay in the
/// chart-of-accounts editor. The server re-validates everything.
/// </summary>
public sealed partial class AccountCardViewModel : EntityCardViewModel
{
    public const int RecentMovementCount = 10;
    protected override string RequiredPermission => ChartOfAccountsViewModel.ViewPermission;

    private readonly IChartOfAccountsClient accounts;
    private readonly ILedgerReportsClient reports;
    private readonly ShellNavigator navigator;

    [ObservableProperty] private AccountResponse? account;
    [ObservableProperty] private AccountResponse? parent;
    [ObservableProperty] private AccountLedgerResponse? summary;
    [ObservableProperty] private AccountLedgerLineResponse? selectedLine;
    [ObservableProperty] private string navigationHint = string.Empty;
    [ObservableProperty] private string draftCode = string.Empty;
    [ObservableProperty] private string draftNameArabic = string.Empty;
    [ObservableProperty] private string? draftNameEnglish;
    [ObservableProperty] private CostCenterPolicy draftCostCenterPolicy = CostCenterPolicy.Optional;
    [ObservableProperty] private bool draftPostingAllowed = true;

    public AccountCardViewModel(IChartOfAccountsClient accounts, ILedgerReportsClient reports, ShellNavigator navigator, DesktopOperation operation, ILocalizationService localization,
        IUserConfirmation confirmation)
        : base(operation, localization, confirmation)
    {
        this.accounts = accounts; this.reports = reports; this.navigator = navigator;
        RefreshCommand = new AsyncRelayCommand(() => IsViewMode ? RunAsync(LoadAccountAsync) : Task.CompletedTask,
            () => HasContext && Idle && AccountId is not null && IsViewMode);
        OpenLedgerCommand = new AsyncRelayCommand(OpenLedgerAsync, () => HasContext && Account is not null && Can("Ledger.Reports.View"));
        EditCommand = new AsyncRelayCommand(() => Account is null ? Task.CompletedTask : navigator.NavigateAsync("accounts", new OpenAccountRequest(Account.Id)),
            () => HasContext && Account is not null && Can(ChartOfAccountsViewModel.ManagePermission));
        OpenParentCommand = new AsyncRelayCommand(() => Parent is null ? Task.CompletedTask : navigator.NavigateAsync("accounts", new OpenAccountCardRequest(Parent.Id)), () => Parent is not null);
        OpenChildCommand = new AsyncRelayCommand<AccountResponse>(child => child is null ? Task.CompletedTask : navigator.NavigateAsync("accounts", new OpenAccountCardRequest(child.Id)));
        OpenJournalCommand = new AsyncRelayCommand<AccountLedgerLineResponse>(line => (line ?? SelectedLine) is { } target && Can("Ledger.Journals.View")
            ? navigator.NavigateAsync("journals", new OpenJournalRequest(target.JournalId)) : Task.CompletedTask, _ => Can("Ledger.Journals.View"));
        // Record navigation in the server's stable code order; one keyset row per step, never the whole chart.
        FirstCommand = new AsyncRelayCommand(() => MoveAsync(AccountNavigationDirection.First), CanMove);
        PreviousCommand = new AsyncRelayCommand(() => MoveAsync(AccountNavigationDirection.Previous), CanMove);
        NextCommand = new AsyncRelayCommand(() => MoveAsync(AccountNavigationDirection.Next), CanMove);
        LastCommand = new AsyncRelayCommand(() => MoveAsync(AccountNavigationDirection.Last), CanMove);
        InitializeAuditCommands();
        InitializeChildrenCommands();
    }

    public IReadOnlyList<ChoiceOption<CostCenterPolicy>> CostCenterPolicies => Options.For<CostCenterPolicy>(Localization, "D_CostPolicy_");
    /// <summary>The code is fixed once journal lines reference the account (server rule); a new sub-account always needs one.</summary>
    public bool CanEditCode => IsNewMode || (IsEditMode && (!EntryOptionsHub.Current.PreventModifyingCodeWithMovements || Account is { HasJournalLines: false }));
    /// <summary>Boolean for WPF IsReadOnly (never bound through a converter).</summary>
    public bool IsCodeReadOnly => !CanEditCode;
    public string ModeLabel => Localization["D_CardMode_" + Mode];

    protected override bool CanEditRecord => CanManage && Account is not null;
    protected override bool CanCreateRecord => CanManage && Account is { PostingAllowed: false, IsActive: true };
    public override bool CanSaveDraft => !string.IsNullOrWhiteSpace(DraftCode) && !string.IsNullOrWhiteSpace(DraftNameArabic);

    public override IReadOnlyList<string> GetValidationErrors()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(DraftCode))
            errors.Add(Localization["D_Code"] + ": " + Localization["D_RequiredField"]);
        if (string.IsNullOrWhiteSpace(DraftNameArabic))
            errors.Add(Localization["D_NameArabic"] + ": " + Localization["D_RequiredField"]);
        errors.AddRange(ProfileValidationErrors());
        return errors;
    }

    protected override string? GetValidationFocusTarget() => string.IsNullOrWhiteSpace(DraftCode)
        ? "code"
        : string.IsNullOrWhiteSpace(DraftNameArabic) ? "name-ar" : null;
    private bool CoreHasDraftChanges => IsNewMode
        ? DraftCode.Length > 0 || DraftNameArabic.Length > 0 || !string.IsNullOrEmpty(DraftNameEnglish)
        : Account is { } current && (DraftCode != current.Code || DraftNameArabic != current.NameArabic
            || (string.IsNullOrWhiteSpace(DraftNameEnglish) ? null : DraftNameEnglish.Trim()) != current.NameEnglish || DraftCostCenterPolicy != current.CostCenterPolicy);

    protected override void LoadDraft(EntityCardMode mode)
    {
        LoadProfileDraft(mode == EntityCardMode.New ? null : savedProfile);
        if (mode == EntityCardMode.New || Account is null)
        {
            DraftCode = Account is not null
                ? ChartOfAccountsViewModel.GenerateNextChildCode(Account.Code, Children.Select(c => c.Code))
                : string.Empty;
            DraftNameArabic = string.Empty; DraftNameEnglish = null;
            DraftCostCenterPolicy = CostCenterPolicy.Optional; DraftPostingAllowed = true;
        }
        else
        {
            DraftCode = Account.Code; DraftNameArabic = Account.NameArabic; DraftNameEnglish = Account.NameEnglish;
            DraftCostCenterPolicy = Account.CostCenterPolicy; DraftPostingAllowed = Account.PostingAllowed;
        }
    }

    protected override async Task<bool> SaveDraftAsync(EntityCardMode mode)
    {
        if (Account is not { } current) return false;
        var englishName = string.IsNullOrWhiteSpace(DraftNameEnglish) ? null : DraftNameEnglish.Trim();
        if (mode == EntityCardMode.Edit)
        {
            // Unchanged structure is sent back as read; the row version rejects a concurrent change (reload, then edit again).
            var request = new AccountRequest
            {
                Code = DraftCode.Trim(), NameArabic = DraftNameArabic.Trim(), NameEnglish = englishName, AccountType = current.AccountType,
                PostingAllowed = current.PostingAllowed, CostCenterPolicy = DraftCostCenterPolicy, CurrencyMode = current.CurrencyMode,
                FixedCurrencyCode = current.FixedCurrencyCode, ParentId = current.ParentId,
            };
            await accounts.UpdateAccountAsync(CompanyId, BranchId, current.Id, current.Version, request, Token).ConfigureAwait(true);
            await SaveProfileAsync(current.Id, Token).ConfigureAwait(true);
            await LoadAccountAsync(Token).ConfigureAwait(true);
            return true;
        }

        var child = new AccountRequest
        {
            Code = DraftCode.Trim(), NameArabic = DraftNameArabic.Trim(), NameEnglish = englishName, AccountType = current.AccountType,
            PostingAllowed = DraftPostingAllowed, CostCenterPolicy = DraftCostCenterPolicy, CurrencyMode = AccountCurrencyMode.BaseCurrencyOnly, ParentId = current.Id,
        };
        var created = await accounts.CreateAccountAsync(CompanyId, BranchId, child, Token).ConfigureAwait(true);
        await SaveProfileAsync(created.Id, Token).ConfigureAwait(true);
        // The new sub-account is shown in this card (same tab, its own key); the parent's card can be reopened from Hierarchy.
        ResetMode();
        await navigator.NavigateAsync("accounts", new OpenAccountCardRequest(created.Id, current.Id)).ConfigureAwait(true);
        return true;
    }

    partial void OnDraftCodeChanged(string value) => DraftChanged();
    partial void OnDraftNameArabicChanged(string value) => DraftChanged();
    partial void OnDraftNameEnglishChanged(string? value) => DraftChanged();
    partial void OnDraftCostCenterPolicyChanged(CostCenterPolicy value) => DraftChanged();
    partial void OnDraftPostingAllowedChanged(bool value) => DraftChanged();

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        ProfileChanged(e.PropertyName);
        if (e.PropertyName is nameof(Mode) or nameof(Account)) { OnPropertyChanged(nameof(CanEditCode)); OnPropertyChanged(nameof(IsCodeReadOnly)); OnPropertyChanged(nameof(ModeLabel)); }
    }

    public AsyncRelayCommand FirstCommand { get; }
    public AsyncRelayCommand PreviousCommand { get; }
    public AsyncRelayCommand NextCommand { get; }
    public AsyncRelayCommand LastCommand { get; }

    private bool CanMove() => HasContext && Idle && Account is not null && IsViewMode;

    /// <summary>Finds the target account, then asks the shell to move this card to it (the card's key follows the account).</summary>
    private async Task MoveAsync(AccountNavigationDirection direction)
    {
        if (Account is not { } current) return;
        AccountResponse? target = null;
        if (!await RunAsync(async token =>
            {
                target = (await accounts.NavigateAccountAsync(CompanyId, BranchId, direction, current.Id, token).ConfigureAwait(true)).Account;
            }).ConfigureAwait(true)) return;
        if (target is null || target.Id == current.Id)
        {
            NavigationHint = Localization[direction is AccountNavigationDirection.Previous or AccountNavigationDirection.First ? "D_NoPreviousRecord" : "D_NoNextRecord"];
            return;
        }

        NavigationHint = string.Empty;
        await navigator.NavigateAsync("accounts", new OpenAccountCardRequest(target.Id, current.Id)).ConfigureAwait(true);
    }

    public Guid? AccountId { get; private set; }
    public ObservableCollection<AccountResponse> Children { get; } = [];
    public ObservableCollection<AccountResponse> Path { get; } = [];
    public ObservableCollection<AccountLedgerLineResponse> RecentLines { get; } = [];
    public bool HasAccount => Account is not null;
    public string Title => Account is null ? Localization["D_AccountCard"] : Account.Code + " — " + Name(Account);
    public string Breadcrumb => string.Join(" › ", Path.Select(item => item.Code + " " + Name(item)));
    public string ParentLabel => Parent is null ? Localization["D_RootAccount"] : Parent.Code + " — " + Name(Parent);
    public string StatusText => Localization[Account?.IsActive != false ? "D_Active" : "D_Inactive"];
    public BadgeKind StatusKind => Account?.IsActive != false ? BadgeKind.Success : BadgeKind.Neutral;
    public string CurrencyText => Account is null ? string.Empty : Account.CurrencyMode == AccountCurrencyMode.FixedCurrency
        ? Localization["D_CurrencyMode_" + Account.CurrencyMode] + " · " + Account.FixedCurrencyCode
        : Localization["D_CurrencyMode_" + Account.CurrencyMode];
    public string BaseCurrency => Summary?.BaseCurrencyCode ?? Context?.BaseCurrencyCode ?? string.Empty;
    public bool CanViewLedger => Can("Ledger.Reports.View");
    public bool CanManage => Can(ChartOfAccountsViewModel.ManagePermission);
    public bool HasSummary => Summary is not null;
    public bool HasChildAccounts => ChildrenTotalCount > 0;
    public bool IncludesDescendants => Summary is { IncludedAccountCount: > 1 };
    public string Opening => MoneyText.Format(Summary?.OpeningBalance, true);
    public string TotalDebit => MoneyText.Format(Summary?.PeriodDebit);
    public string TotalCredit => MoneyText.Format(Summary?.PeriodCredit);
    public string Closing => MoneyText.Format(Summary?.ClosingBalance, true);
    public long MovementCount => Summary?.TotalCount ?? 0;

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand OpenLedgerCommand { get; }
    public AsyncRelayCommand EditCommand { get; }
    public AsyncRelayCommand OpenParentCommand { get; }
    public AsyncRelayCommand<AccountResponse> OpenChildCommand { get; }
    public AsyncRelayCommand<AccountLedgerLineResponse> OpenJournalCommand { get; }

    public void Localize()
    {
        foreach (var name in new[] { nameof(Title), nameof(Breadcrumb), nameof(ParentLabel), nameof(StatusText), nameof(CurrencyText), nameof(CostCenterPolicies), nameof(ModeLabel) }) OnPropertyChanged(name);
        OnSelectedAuditChanged(SelectedAudit); NotifyAudit();
        NotifyChildren();
    }

    protected override Task LoadAsync(object? parameter, CancellationToken cancellationToken)
    {
        if (parameter is OpenAccountCardRequest request) { if (request.AccountId != AccountId) ResetMode(); AccountId = request.AccountId; NavigationHint = string.Empty; }
        // Reopening reauthorizes the context, but an editing card must retain the snapshot/version that its draft began
        // with. Adopting a newer version here would let an old draft overwrite another user's committed changes.
        if (IsEditing) return Task.CompletedTask;
        return AccountId is null ? Task.CompletedTask : LoadAccountAsync(cancellationToken);
    }

    protected override void ClearPage()
    {
        ResetMode();
        Account = null; Parent = null; Summary = null; SelectedLine = null;
        ClearChildren(); Path.Clear(); RecentLines.Clear();
        ClearAudit();
        NotifyAll();
    }

    /// <summary>Load only the ancestor path and one child page; financial totals remain server-authoritative.</summary>
    private async Task LoadAccountAsync(CancellationToken cancellationToken)
    {
        if (AccountId is not { } id) return;
        var result = await accounts.GetAccountCardContextAsync(CompanyId, BranchId, id, 1, ChildrenPageSize, cancellationToken).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        if (AccountId != id) return;
        var current = result.Path[^1];
        Path.Clear();
        foreach (var node in result.Path) Path.Add(node);
        ApplyChildren(result.Children);
        Parent = result.Path.Count > 1 ? result.Path[^2] : null;
        Account = current;
        if (IsViewMode)
        {
            DraftCode = current.Code;
            DraftNameArabic = current.NameArabic;
            DraftNameEnglish = current.NameEnglish;
            DraftCostCenterPolicy = current.CostCenterPolicy;
            DraftPostingAllowed = current.PostingAllowed;
        }
        ClearAudit();
        RecentLines.Clear(); Summary = null;
        if (Can("Ledger.Reports.View"))
        {
            var ledger = await reports.GetAccountLedgerAsync(CompanyId, BranchId, id, null, null, false, 1, RecentMovementCount, null, cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var line in ledger.Lines) RecentLines.Add(line);
            Summary = ledger;
        }

        await LoadAuditAsync(1, cancellationToken).ConfigureAwait(true);
        await LoadProfileAsync(id, cancellationToken).ConfigureAwait(true);
        NotifyAll();
    }

    private Task OpenLedgerAsync() => Account is null || !Can("Ledger.Reports.View")
        ? Task.CompletedTask
        : navigator.NavigateAsync("account-ledger", new OpenAccountLedgerRequest(Account.Id, null, null, false));

    private string Name(AccountResponse item) => Localization.IsRightToLeft ? item.NameArabic : item.NameEnglish ?? item.NameArabic;

    private void NotifyAll()
    {
        foreach (var name in new[] { nameof(HasAccount), nameof(Title), nameof(Breadcrumb), nameof(ParentLabel), nameof(StatusText), nameof(StatusKind),
                     nameof(CurrencyText), nameof(BaseCurrency), nameof(CanViewLedger), nameof(CanManage), nameof(HasSummary), nameof(HasChildAccounts),
                     nameof(IncludesDescendants), nameof(Opening), nameof(TotalDebit), nameof(TotalCredit), nameof(Closing), nameof(MovementCount),
                     nameof(HasNatureViolation), nameof(NatureViolationWarning) })
        {
            OnPropertyChanged(name);
        }

        NotifyCommands();
    }

    protected override void NotifyCommands()
    {
        RefreshCommand?.NotifyCanExecuteChanged(); OpenLedgerCommand?.NotifyCanExecuteChanged(); EditCommand?.NotifyCanExecuteChanged();
        OpenParentCommand?.NotifyCanExecuteChanged(); OpenJournalCommand?.NotifyCanExecuteChanged();
        FirstCommand?.NotifyCanExecuteChanged(); PreviousCommand?.NotifyCanExecuteChanged(); NextCommand?.NotifyCanExecuteChanged(); LastCommand?.NotifyCanExecuteChanged();
        NotifyAudit();
        NotifyChildren();
        NotifyCardCommands();
    }
}

/// <summary>Creates one card view model per opened account document (no service locator; dependencies are explicit).</summary>
public sealed class AccountCardFactory(IChartOfAccountsClient accounts, ILedgerReportsClient reports, ShellNavigator navigator, DesktopOperation operation, ILocalizationService localization,
    IUserConfirmation confirmation)
{
    public AccountCardViewModel Create() => new(accounts, reports, navigator, operation, localization, confirmation);
}
