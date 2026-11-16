using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.GeneralLedger;

public sealed partial class GeneralLedgerViewModel : ObservableObject, IDisposable, ILoadingAware
{
    private readonly PostingAttemptTracker postingAttempts;
    private readonly IGeneralLedgerClient client;
    private readonly IUserConfirmation confirmation;
    private readonly DesktopOperation operation;
    private readonly ILocalizationService localization;
    private HashSet<string> permissions = new(StringComparer.Ordinal);
    private CancellationTokenSource context = new();

    private Guid currentCompanyId;
    private Guid currentBranchId;
    private string companyBaseCurrencyCode = string.Empty;
    private JournalDetailsResponse? currentJournal;

    [ObservableProperty] private string search = string.Empty;
    [ObservableProperty] private JournalState? stateFilter;
    [ObservableProperty] private DateOnly? fromDate;
    [ObservableProperty] private DateOnly? toDate;
    [ObservableProperty] private int page = 1;
    [ObservableProperty] private long totalCount;
    [ObservableProperty] private JournalSummaryItemResponse? selectedJournal;

    [ObservableProperty] private bool isEditorOpen;
    [ObservableProperty] private bool isNewDraft;
    [ObservableProperty] private DateOnly accountingDate = DateOnly.FromDateTime(DateTime.Today);
    [ObservableProperty] private string journalDescription = string.Empty;
    [ObservableProperty] private string actionReason = string.Empty;
    [ObservableProperty] private string baseCurrencyCode = string.Empty;
    [ObservableProperty] private decimal totalDebit;
    [ObservableProperty] private decimal totalCredit;
    [ObservableProperty] private decimal difference;
    [ObservableProperty] private bool isBalanced;
    [ObservableProperty] private string validationMessage = string.Empty;
    [ObservableProperty] private IReadOnlyList<string> validationErrors = [];
    public bool HasValidationErrors => ValidationErrors.Count > 0 || !string.IsNullOrWhiteSpace(ValidationMessage);
    partial void OnValidationMessageChanged(string value) => OnPropertyChanged(nameof(HasValidationErrors));
    partial void OnValidationErrorsChanged(IReadOnlyList<string> value) => OnPropertyChanged(nameof(HasValidationErrors));

    public ILocalizationService Localization => localization;
    public bool HasReasonCommands => CanEditDraft || CanReturnToDraft || CanPostJournal || CanPostReversal || CanCreateReversal;
    [ObservableProperty] private bool hasInputErrors;
    [ObservableProperty] private bool isTotalsPreview;
    [ObservableProperty] private bool isPreviewValid = true;
    partial void OnHasInputErrorsChanged(bool value) => NotifyCommands();
    partial void OnIsPreviewValidChanged(bool value) => NotifyCommands();
    public ObservableCollection<JournalSummaryItemResponse> Journals { get; } = [];
    public ObservableCollection<JournalLineRow> Lines { get; } = [];
    public ObservableCollection<JournalAuditResponse> AuditTrail { get; } = [];
    public ObservableCollection<AccountLookupResponse> Accounts { get; } = [];
    public ObservableCollection<CostCenterLookupResponse> CostCenters { get; } = [];
    /// <summary>Base plus additional currencies enabled for the selected company (server currency context).</summary>
    public ObservableCollection<string> Currencies { get; } = [];

    public string PageStatus => $"{Page} Â· {Journals.Count} / {TotalCount}";
    public string CurrentStateName => currentJournal?.StateName ?? (IsNewDraft ? localization["D_State_Draft"] : string.Empty);
    public string CurrentVersion => currentJournal?.Version ?? string.Empty;
    /// <summary>Typed server state of the open journal; null for a new unsaved draft or no journal.</summary>
    public JournalState? CurrentState => currentJournal?.State;

    public bool CanManageJournals => permissions.Contains(JournalCommandPolicy.Manage);
    public bool CanManageBulkPosting => permissions.Contains(DocumentProcessing.PostingManagementViewModel.ManagePermission);
    public bool CanApproveJournals => permissions.Contains(JournalCommandPolicy.Approve);
    public bool CanViewJournals => permissions.Contains("Ledger.Journals.View");

    /// <summary>Commands allowed for the open journal (none when the editor is closed). The server re-checks everything.</summary>
    public JournalCommandAvailability Availability => !IsEditorOpen || (currentJournal is null && !IsNewDraft)
        ? JournalCommandAvailability.None
        : JournalCommandPolicy.Evaluate(currentJournal?.State, currentJournal?.ReversesEntryId is not null,
            currentJournal?.ReversedByEntryId is not null, permissions);

    public bool CanEditDraft => Availability.Edit;
    /// <summary>Boolean for WPF IsReadOnly: everything except an editable draft is read-only.</summary>
    public bool IsDraftReadOnly => !CanEditDraft;
    public bool HasUnsavedChanges => IsEditorOpen && CanEditDraft && (IsNewDraft || IsTotalsPreview || HasInputErrors || Lines.Any(l => !l.IsPersisted && !l.IsPristineBlank));
    public bool CanValidateDraft => Availability.Validate && !HasUnsavedChanges;
    public bool CanApproveDraft => Availability.Approve;
    public bool CanReturnToDraft => Availability.ReturnToDraft;
    public bool CanCreateReversal => Availability.CreateReversal;
    public bool CanDeleteDraft => Availability.Delete;
    public bool CanPostJournal => Availability.Post;
    public bool CanPostReversal => Availability.PostReversal;

    public AsyncRelayCommand SearchJournalsCommand { get; }
    public AsyncRelayCommand PreviousPageCommand { get; }
    public AsyncRelayCommand NextPageCommand { get; }
    public AsyncRelayCommand OpenJournalCommand { get; }
    public AsyncRelayCommand NewDraftCommand { get; }
    public AsyncRelayCommand CloseEditorCommand { get; }
    public RelayCommand AddLineCommand { get; }
    public RelayCommand<JournalLineRow> RemoveLineCommand { get; }
    public RelayCommand AutoGrowCommand { get; }
    public AsyncRelayCommand FirstCommand { get; }
    public AsyncRelayCommand PreviousCommand { get; }
    public AsyncRelayCommand NextCommand { get; }
    public AsyncRelayCommand LastCommand { get; }
    public AsyncRelayCommand SaveDraftCommand { get; }
    public AsyncRelayCommand ValidateDraftCommand { get; }
    public AsyncRelayCommand ApproveDraftCommand { get; }
    public AsyncRelayCommand ReturnToDraftCommand { get; }
    public AsyncRelayCommand CreateReversalCommand { get; }
    public AsyncRelayCommand DeleteDraftCommand { get; }
    public AsyncRelayCommand PostJournalCommand { get; }
    public AsyncRelayCommand PostReversalCommand { get; }
    public AsyncRelayCommand BulkPostCommand { get; }

    public GeneralLedgerViewModel(IGeneralLedgerClient client, IUserConfirmation confirmation, DesktopOperation operation, ILocalizationService localization)
        : this(client, confirmation, operation, localization, new PostingAttemptTracker())
    {
    }

    public GeneralLedgerViewModel(IGeneralLedgerClient client, IUserConfirmation confirmation, DesktopOperation operation, ILocalizationService localization,
        PostingAttemptTracker postingAttempts)
    {
        this.postingAttempts = postingAttempts ?? throw new ArgumentNullException(nameof(postingAttempts));
        this.client = client;
        this.confirmation = confirmation;
        this.operation = operation;
        this.localization = localization;

        SearchJournalsCommand = new AsyncRelayCommand(() => RunAsync(() => LoadJournalsAsync(1, context.Token)), () => !operation.IsBusy && CanViewJournals && currentCompanyId != Guid.Empty && currentBranchId != Guid.Empty);
        PreviousPageCommand = new AsyncRelayCommand(() => RunAsync(() => LoadJournalsAsync(Page - 1, context.Token)), () => !operation.IsBusy && CanViewJournals && Page > 1);
        NextPageCommand = new AsyncRelayCommand(() => RunAsync(() => LoadJournalsAsync(Page + 1, context.Token)), () => !operation.IsBusy && CanViewJournals && Page * 50L < TotalCount);
        OpenJournalCommand = new AsyncRelayCommand(() => Navigator is not null && SelectedJournal is { } selected ? Navigator.NavigateAsync("journals", new OpenJournalRequest(selected.Id)) : RunAsync(OpenJournalAsync), () => !operation.IsBusy && CanViewJournals && SelectedJournal is not null);
        NewDraftCommand = new AsyncRelayCommand(() => Navigator is not null ? Navigator.NavigateAsync("journals", NewJournalRequest.Instance) : StartNewAsync(), () => !operation.IsBusy && CanManageJournals && currentCompanyId != Guid.Empty && currentBranchId != Guid.Empty
            && companyBaseCurrencyCode.Length > 0);
        CloseEditorCommand = new AsyncRelayCommand(async () =>
        {
            // A journal document closes through the shell, which applies the same dirty and pending-posting rules as its tab.
            if (IsDocument) { DocumentCloseRequested?.Invoke(this, EventArgs.Empty); return; }
            if (!HasUnsavedChanges || await confirmation.ConfirmAsync("D_ConfirmDiscardChanges").ConfigureAwait(true)) IsEditorOpen = false;
        });

        AddLineCommand = new RelayCommand(AddLine, () => !operation.IsBusy && CanEditDraft && !HasInputErrors);
        RemoveLineCommand = new RelayCommand<JournalLineRow>(RemoveLine, _ => !operation.IsBusy && CanEditDraft);
        AutoGrowCommand = new RelayCommand(() => AutoGrowLines(10), () => !operation.IsBusy && CanEditDraft);
        FirstCommand = new AsyncRelayCommand(() => MoveJournalAsync(int.MinValue), () => !operation.IsBusy && CanViewJournals && Journals.Count > 0);
        PreviousCommand = new AsyncRelayCommand(() => MoveJournalAsync(-1), () => !operation.IsBusy && CanViewJournals && Journals.Count > 0);
        NextCommand = new AsyncRelayCommand(() => MoveJournalAsync(1), () => !operation.IsBusy && CanViewJournals && Journals.Count > 0);
        LastCommand = new AsyncRelayCommand(() => MoveJournalAsync(int.MaxValue), () => !operation.IsBusy && CanViewJournals && Journals.Count > 0);
        BulkPostCommand = new AsyncRelayCommand(BulkPostAsync, () => Navigator is not null && !operation.IsBusy && CanManageBulkPosting);

        SaveDraftCommand = new AsyncRelayCommand(SaveDraftAsync, () => !operation.IsBusy && CanEditDraft);
        ValidateDraftCommand = new AsyncRelayCommand(ValidateDraftAsync, () => !operation.IsBusy && !HasInputErrors && CanValidateDraft);
        ApproveDraftCommand = new AsyncRelayCommand(ApproveDraftAsync, () => !operation.IsBusy && !HasInputErrors && CanApproveDraft);
        ReturnToDraftCommand = new AsyncRelayCommand(ReturnToDraftAsync, () => !operation.IsBusy && !HasInputErrors && CanReturnToDraft);
        CreateReversalCommand = new AsyncRelayCommand(CreateReversalAsync, () => !operation.IsBusy && !HasInputErrors && CanCreateReversal && HasActionReason);
        DeleteDraftCommand = new AsyncRelayCommand(DeleteDraftAsync, () => !operation.IsBusy && !HasInputErrors && CanDeleteDraft);
        PostJournalCommand = new AsyncRelayCommand(PostJournalAsync, () => !operation.IsBusy && !HasInputErrors && CanPostJournal && HasActionReason);
        PostReversalCommand = new AsyncRelayCommand(PostReversalAsync, () => !operation.IsBusy && !HasInputErrors && CanPostReversal && HasActionReason);

        Lines.CollectionChanged += OnLinesCollectionChanged;
        operation.PropertyChanged += OperationChanged;
    }

    public void Configure(IReadOnlyCollection<string> permissions)
    {
        this.permissions = new HashSet<string>(permissions, StringComparer.Ordinal);
        NotifyCommands();
    }

    /// <summary>Loads the page for the selected company/branch. The base currency comes from the selected company context.</summary>
    public async Task LoadPageAsync(Guid companyId, Guid branchId, string companyBaseCurrency, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(companyBaseCurrency);
        if (companyId != currentCompanyId || branchId != currentBranchId)
        {
            // A different company never inherits journal, line, lookup or currency context from the previous one.
            Clear();
        }

        currentCompanyId = companyId;
        currentBranchId = branchId;
        companyBaseCurrencyCode = companyBaseCurrency;
        if (!IsEditorOpen) BaseCurrencyCode = companyBaseCurrency;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(context.Token, cancellationToken);
        cancellationToken = linked.Token;
        await LoadLookupsAsync(cancellationToken).ConfigureAwait(true);
        if (!IsDocument) await LoadJournalsAsync(1, cancellationToken).ConfigureAwait(true);
    }

    public void Clear()
    {
        linkContext = null;
        DocumentLinks?.SetSource(null, Sales.SalesLinkSource.Journal, null);
        VoucherLinks?.SetSource(null, Relationships.RelatedDocumentSource.JournalVouchers, null);
        context.Cancel(); context.Dispose(); context = new CancellationTokenSource();
        SelectedJournal = null; HasInputErrors = false; ValidationMessage = string.Empty;
        periodNames.Clear(); LastPostingReceipt = null;
        Journals.Clear();
        Lines.Clear();
        AuditTrail.Clear();
        Accounts.Clear();
        CostCenters.Clear();
        Currencies.Clear();
        AccountItems.Clear();
        CostCenterItems.Clear();
        currentJournal = null;
        currentCompanyId = Guid.Empty;
        currentBranchId = Guid.Empty;
        companyBaseCurrencyCode = string.Empty;
        BaseCurrencyCode = string.Empty;
        postingAttempts.Reset();
        IsEditorOpen = false;
        IsNewDraft = false;
        TotalCount = 0;
        Page = 1;
        NotifyCommands();
    }

    public void Localize()
    {
        OnPropertyChanged(nameof(FilterStates));
        OnPropertyChanged(nameof(CurrentStateLabel));
        OnPropertyChanged(nameof(CurrentStateName));
        OnPropertyChanged(nameof(PageStatus));
        NotifyCommands();
    }

    private async Task LoadLookupsAsync(CancellationToken cancellationToken)
    {
        if (currentCompanyId == Guid.Empty) return;
        // Lookups are requested only with the matching view permission, so a journal user without them still works.
        Accounts.Clear();
        if (permissions.Contains("Ledger.Accounts.View"))
        {
            var accountsList = await client.GetAccountsAsync(currentCompanyId, currentBranchId, cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var acc in accountsList.Where(a => a.IsActive && a.PostingAllowed))
            {
                Accounts.Add(acc);
            }
        }

        CostCenters.Clear();
        if (permissions.Contains("Ledger.CostCenters.View"))
        {
            var costCentersList = await client.GetCostCentersAsync(currentCompanyId, currentBranchId, cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var cc in costCentersList.Where(c => c.IsActive))
            {
                CostCenters.Add(cc);
            }
        }

        RebuildLookupItems();

        var currencyContext = await client.GetCurrencyContextAsync(currentCompanyId, currentBranchId, cancellationToken).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        Currencies.Clear();
        Currencies.Add(currencyContext.BaseCurrency.Code);
        foreach (var currency in currencyContext.AdditionalCurrencies)
        {
            Currencies.Add(currency.Code);
        }
    }

    private async Task LoadJournalsAsync(int requestedPage, CancellationToken cancellationToken)
    {
        // A journal document shows one journal; the list belongs to the "journals" route page. Every server write of a
        // document ends here, so this is where the document tells the shell that the list is out of date.
        if (IsDocument) { JournalChanged?.Invoke(this, EventArgs.Empty); return; }
        if (currentCompanyId == Guid.Empty || currentBranchId == Guid.Empty) return;
        var result = await client.SearchJournalsAsync(
            currentCompanyId,
            currentBranchId,
            StateFilter,
            FromDate,
            ToDate,
            string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
            requestedPage,
            50,
            cancellationToken).ConfigureAwait(true);

        cancellationToken.ThrowIfCancellationRequested();
        Journals.Clear();
        foreach (var item in result.Items)
        {
            Journals.Add(item);
        }

        Page = result.Page;
        TotalCount = result.TotalCount;
        IsListStale = false;
        OnPropertyChanged(nameof(PageStatus));
        NotifyCommands();
    }

    private async Task OpenJournalAsync()
    {
        var token = context.Token;
        if (SelectedJournal is null) return;
        var details = await client.GetJournalAsync(currentCompanyId, currentBranchId, SelectedJournal.Id, token).ConfigureAwait(true);
        token.ThrowIfCancellationRequested();
        DisplayJournal(details);
    }

    private void DisplayJournal(JournalDetailsResponse details)
    {
        if (details.CompanyId != currentCompanyId || details.BranchId != currentBranchId) return;
        if (details.State is JournalState.Posted or JournalState.Reversed && postingAttempts.PendingIntentFor(details.Id) is not null)
        {
            // A fresh authorized read resolves a lost response without sending another financial write.
            postingAttempts.Reset(); OnPropertyChanged(nameof(HasPendingPostingAttempt));
        }
        currentJournal = details;
        IsNewDraft = false;
        AccountingDate = details.AccountingDate;
        JournalDescription = details.Description;
        // Existing journals keep their historical base currency; a journal without lines falls back to the company base.
        BaseCurrencyCode = string.IsNullOrEmpty(details.BaseCurrencyCode) ? companyBaseCurrencyCode : details.BaseCurrencyCode;
        ActionReason = string.Empty;

        Lines.Clear();
        foreach (var line in details.Lines)
        {
            // Historical names remain readable even after an account/center is deactivated or lookup access changes.
            // These entries are display-only and are never offered as new lookup selections.
            if (!AccountItems.Any(item => item.Id == line.AccountId))
                AccountItems.Add(new(line.AccountId, line.AccountCode, line.AccountNameArabic, line.AccountNameEnglish, false));
            if (line.CostCenterId is { } center && !CostCenterItems.Any(item => item.Id == center))
                CostCenterItems.Add(new(center, line.CostCenterCode ?? string.Empty, line.CostCenterNameArabic ?? string.Empty, line.CostCenterNameEnglish, false));
            if (!Currencies.Contains(line.TransactionCurrencyCode)) Currencies.Add(line.TransactionCurrencyCode);
            var row = new JournalLineRow
            {
                Localize = key => localization[key],
                AccountId = line.AccountId,
                CostCenterId = line.CostCenterId,
                CurrencyCode = line.TransactionCurrencyCode,
                Debit = decimal.Parse(line.TransactionDebit, CultureInfo.InvariantCulture),
                Credit = decimal.Parse(line.TransactionCredit, CultureInfo.InvariantCulture),
                Rate = decimal.Parse(line.RateValue, CultureInfo.InvariantCulture),
                Description = line.Description,
                IsPersisted = true,
            };
            Lines.Add(row);
        }
        if (details.State == JournalState.Draft && CanEditDraft)
        {
            EnsureEntryBuffer(50);
        }

        AuditTrail.Clear();
        foreach (var audit in details.AuditTrail)
        {
            AuditTrail.Add(audit);
        }

        RecalculateTotals();
        TotalDebit = decimal.Parse(details.TotalDebit, CultureInfo.InvariantCulture);
        TotalCredit = decimal.Parse(details.TotalCredit, CultureInfo.InvariantCulture);
        Difference = Math.Abs(TotalDebit - TotalCredit); IsBalanced = Difference == 0 && TotalDebit > 0;
        IsTotalsPreview = false; ValidationMessage = string.Empty; HasInputErrors = false;
        IsEditorOpen = true;
        NotifyCommands();
    }

    private void NewDraft()
    {
        currentJournal = null;
        IsNewDraft = true;
        BaseCurrencyCode = companyBaseCurrencyCode;
        AccountingDate = DateOnly.FromDateTime(DateTime.Today);
        JournalDescription = string.Empty;
        ActionReason = string.Empty;

        Lines.Clear();
        EnsureEntryBuffer(50);

        AuditTrail.Clear();
        RecalculateTotals();
        IsTotalsPreview = false;
        IsEditorOpen = true;
        NotifyCommands();
    }

    private Task StartNewAsync() { NewDraft(); return Task.CompletedTask; }

    partial void OnJournalDescriptionChanged(string value) { if (CanEditDraft) IsTotalsPreview = true; NotifyCommands(); }
    partial void OnAccountingDateChanged(DateOnly value) { if (CanEditDraft) IsTotalsPreview = true; NotifyCommands(); }

    public bool CanSaveDraft => !operation.IsBusy && CanEditDraft && !HasInputErrors && IsPreviewValid && RequiredFieldsComplete;

    private bool RequiredFieldsComplete
    {
        get
        {
            if (string.IsNullOrWhiteSpace(JournalDescription) || AccountingDate == default) return false;
            var completeLines = Lines.Where(l => l.IsComplete).ToList();
            if (completeLines.Count < 2) return false;
            if (Lines.Any(l => l.IsIncomplete)) return false;
            return completeLines.All(line => line.AccountId != Guid.Empty && Controls.RequiredInput.Currency(line.CurrencyCode));
        }
    }

    public void EnsureEntryBuffer(int targetCount = 50)
    {
        if (!CanEditDraft && !IsNewDraft) return;
        while (Lines.Count < targetCount)
        {
            Lines.Add(JournalLineRow.CreateBlankSlot(key => localization[key], BaseCurrencyCode));
        }
    }

    public void AutoGrowLines(int additional = 10)
    {
        if (!CanEditDraft && !IsNewDraft) return;
        EnsureEntryBuffer(Lines.Count + additional);
    }

    private async Task MoveJournalAsync(int offset)
    {
        if (Journals.Count == 0) return;
        var currentId = currentJournal?.Id;
        var currentIdx = currentId.HasValue ? Journals.ToList().FindIndex(j => j.Id == currentId.Value) : -1;
        int targetIdx;
        if (offset == int.MinValue) targetIdx = 0;
        else if (offset == int.MaxValue) targetIdx = Journals.Count - 1;
        else targetIdx = Math.Clamp(currentIdx + offset, 0, Journals.Count - 1);
        if (targetIdx >= 0 && targetIdx < Journals.Count && Journals[targetIdx].Id != currentId)
        {
            SelectedJournal = Journals[targetIdx];
            await OpenJournalAsync().ConfigureAwait(true);
        }
    }
    partial void OnIsTotalsPreviewChanged(bool value) => NotifyCommands();

    private void AddLine()
    {
        var target = Lines.FirstOrDefault(l => l.IsPristineBlank);
        if (target is null)
        {
            target = JournalLineRow.CreateBlankSlot(key => localization[key], BaseCurrencyCode);
            Lines.Add(target);
        }
        else
        {
            target.CurrencyCode = BaseCurrencyCode;
            target.Rate = 1.0m;
        }
    }

    private void RemoveLine(JournalLineRow? row)
    {
        if (row is null) return;
        Lines.Remove(row);
    }

    private void OnLinesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (JournalLineRow item in e.OldItems)
            {
                item.Changed -= RecalculateTotals;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (JournalLineRow item in e.NewItems)
            {
                item.Changed -= RecalculateTotals;
                item.Changed += RecalculateTotals;
            }
        }

        RecalculateTotals();
    }

    private void RecalculateTotals()
    {
        var nonBlankLines = Lines.Where(l => !l.IsPristineBlank).ToList();
        try
        {
            var debit = nonBlankLines.Sum(l => l.BaseDebit);
            var credit = nonBlankLines.Sum(l => l.BaseCredit);
            TotalDebit = debit; TotalCredit = credit;
        }
        catch (OverflowException) { RejectPreview(); return; }

        Difference = Math.Abs(TotalDebit - TotalCredit);

        if (Lines.Any(line => line.Error.Length > 0)) { RejectPreview(); return; }

        if (!IsPreviewValid) ValidationMessage = string.Empty;
        IsPreviewValid = true;
        IsBalanced = Difference == 0 && nonBlankLines.Count >= 2 && TotalDebit > 0;
        NotifyCommands();
    }

    private void RejectPreview()
    {
        IsPreviewValid = false; IsBalanced = false;
        ValidationMessage = localization["D_JournalNumericValidation"];
        NotifyCommands();
    }

    private Task<bool> RunAsync(Func<Task> action) => operation.RunAsync(action);

    /// <summary>Save/validate/approve/post run with their kind so the shell blocks a second submit and reports unknown outcomes.</summary>
    private Task<bool> RunAsync(OperationKind kind, Func<Task> action) => operation.RunAsync(action, kind, Loading);

    /// <summary>This journal view's operation state (saving, posting, outcome unknown).</summary>
    public PetraLoadingState Loading { get; } = new();

    private void NotifyCommands()
    {
        OnPropertyChanged(nameof(CanViewJournals)); OnPropertyChanged(nameof(CanManageJournals));
        OnPropertyChanged(nameof(CanManageBulkPosting));
        OnPropertyChanged(nameof(Availability));
        OnPropertyChanged(nameof(CurrentState));
        OnPropertyChanged(nameof(CanEditDraft));
        OnPropertyChanged(nameof(IsDraftReadOnly));
        OnPropertyChanged(nameof(CanPostJournal));
        OnPropertyChanged(nameof(CanPostReversal));
        OnPropertyChanged(nameof(CanValidateDraft));
        OnPropertyChanged(nameof(CanApproveDraft));
        OnPropertyChanged(nameof(CanReturnToDraft));
        OnPropertyChanged(nameof(CanCreateReversal));
        OnPropertyChanged(nameof(CanDeleteDraft));
        OnPropertyChanged(nameof(CurrentStateName));
        OnPropertyChanged(nameof(CurrentVersion));
        OnPropertyChanged(nameof(HasReasonCommands));

        SearchJournalsCommand?.NotifyCanExecuteChanged();
        PreviousPageCommand?.NotifyCanExecuteChanged();
        NextPageCommand?.NotifyCanExecuteChanged();
        OpenJournalCommand?.NotifyCanExecuteChanged();
        NewDraftCommand?.NotifyCanExecuteChanged();
        AddLineCommand?.NotifyCanExecuteChanged();
        RemoveLineCommand?.NotifyCanExecuteChanged();
        AutoGrowCommand?.NotifyCanExecuteChanged();
        FirstCommand?.NotifyCanExecuteChanged();
        PreviousCommand?.NotifyCanExecuteChanged();
        NextCommand?.NotifyCanExecuteChanged();
        LastCommand?.NotifyCanExecuteChanged();
        SaveDraftCommand?.NotifyCanExecuteChanged();
        ValidateDraftCommand?.NotifyCanExecuteChanged();
        ApproveDraftCommand?.NotifyCanExecuteChanged();
        ReturnToDraftCommand?.NotifyCanExecuteChanged();
        CreateReversalCommand?.NotifyCanExecuteChanged();
        DeleteDraftCommand?.NotifyCanExecuteChanged();
        PostJournalCommand?.NotifyCanExecuteChanged();
        PostReversalCommand?.NotifyCanExecuteChanged();
        BulkPostCommand?.NotifyCanExecuteChanged();
        NotifyShell();
    }

    private bool HasActionReason => !string.IsNullOrWhiteSpace(ActionReason);

    partial void OnActionReasonChanged(string value) => NotifyCommands();

    partial void OnIsEditorOpenChanged(bool value) => NotifyCommands();

    partial void OnSelectedJournalChanged(JournalSummaryItemResponse? value) => NotifyCommands();

    /// <summary>Opens the central posting page scoped to journals with the current list filter.</summary>
    private Task BulkPostAsync() => Navigator is null
        ? Task.CompletedTask
        : Navigator.NavigateAsync("posting-management", new DocumentProcessing.BulkPostingNavigation(BulkPostingDocumentType.Journal,
            FromDate is { } from ? new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : null,
            ToDate is { } to ? new DateTimeOffset(to.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : null,
            string.IsNullOrWhiteSpace(Search) ? null : Search.Trim()));

    private static string FormatMoney(decimal value) => value.ToString("F4", CultureInfo.InvariantCulture);
    private static string FormatRate(decimal value) => value.ToString("F8", CultureInfo.InvariantCulture);

    private void OperationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args) => NotifyCommands();

    public void Dispose()
    {
        operation.PropertyChanged -= OperationChanged;
        context.Cancel();
        context.Dispose();
        GC.SuppressFinalize(this);
    }
}
