using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Accounting;
using PetraERP.Desktop.Controls;
using PetraERP.Desktop.Shell;
using PetraERP.Desktop.Sales;

namespace PetraERP.Desktop.GeneralLedger;

/// <summary>Shell integration for the journal screen: activation, drill-down, lookups and header presentation.</summary>
public sealed partial class GeneralLedgerViewModel : IShellPage, ISavablePage
{
    public SalesDocumentLinksViewModel? DocumentLinks { get; init; }
    public Relationships.RelatedDocumentsViewModel? VoucherLinks { get; init; }
    private ShellContext? linkContext;
    private readonly Dictionary<Guid, string> periodNames = [];
    private AsyncRelayCommand? openOriginalCommand;
    private AsyncRelayCommand? openReversalCommand;
    private AsyncRelayCommand<JournalLineRow>? openLineLedgerCommand;

    /// <summary>Optional calendar client used to name the posting period (requires Ledger.Periods.View).</summary>
    public ShellNavigator? Navigator { get; set; }

    public IFiscalCalendarClient? Calendar { get; set; }

    /// <summary>
    /// True for a journal document (one tab per journal or new draft, created by <see cref="JournalDocumentFactory"/>);
    /// false for the shared "journals" route page that owns the list.
    /// </summary>
    public bool IsDocument { get; init; }

    /// <summary>Raised by a journal document after a successful server write (save, workflow step, posting, delete).</summary>
    public event EventHandler? JournalChanged;

    /// <summary>List page only: another document changed a journal since the list was loaded.</summary>
    public bool IsListStale { get; private set; }

    public void MarkListStale() => IsListStale = !IsDocument && currentCompanyId != Guid.Empty;

    /// <summary>Reloads the current list page with the current filters (no editor or lookup change).</summary>
    public async Task RefreshListAsync(CancellationToken cancellationToken)
    {
        if (IsDocument || currentCompanyId == Guid.Empty) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(context.Token, cancellationToken);
        await LoadJournalsAsync(Page, linked.Token).ConfigureAwait(true);
    }

    /// <summary>Raised by a journal document's close button so the shell closes its tab under the tab-close rules.</summary>
    public event EventHandler? DocumentCloseRequested;

    public bool CanSaveBeforeClose => HasUnsavedChanges && CanSaveDraft;

    public async Task<bool> SaveBeforeCloseAsync()
    {
        if (!CanSaveBeforeClose) return false;
        await SaveDraftCommand.ExecuteAsync(null).ConfigureAwait(true);
        return !HasUnsavedChanges;
    }

    /// <summary>Server id of the open journal (null for an unsaved new draft); the shell keys the document by it.</summary>
    public Guid? CurrentJournalId => currentJournal?.Id;

    public string DocumentTitle => currentJournal is { } journal
        ? localization["D_JournalEntry"] + " " + (journal.PostingSequence is { } number
            ? "#" + number.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : journal.AccountingDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))
          + (string.IsNullOrWhiteSpace(journal.Description) ? string.Empty : " — " + journal.Description)
        : IsNewDraft ? localization["D_NewJournal"] : localization["D_JournalEntry"];

    public IReadOnlyList<ChoiceOption<JournalState?>> FilterStates => new[] { new ChoiceOption<JournalState?>(null, localization["D_AllStates"]) }
        .Concat(Enum.GetValues<JournalState>().Select(state => new ChoiceOption<JournalState?>(state, localization["D_State_" + state]))).ToArray();
    public ObservableCollection<LookupItem> AccountItems { get; } = [];
    public ObservableCollection<LookupItem> CostCenterItems { get; } = [];

    public AsyncRelayCommand OpenOriginalCommand => openOriginalCommand ??=
        new AsyncRelayCommand(() => NavigateRelatedAsync(currentJournal!.ReversesEntryId!.Value), () => CanViewJournals && !operation.IsBusy && currentJournal?.ReversesEntryId is not null);

    public AsyncRelayCommand OpenReversalCommand => openReversalCommand ??=
        new AsyncRelayCommand(() => NavigateRelatedAsync(currentJournal!.ReversedByEntryId!.Value), () => CanViewJournals && !operation.IsBusy && currentJournal?.ReversedByEntryId is not null);

    public bool CanViewAccountLedgers => permissions.Contains("Ledger.Reports.View");

    /// <summary>Opens a line's account ledger in the current authorized scope; never saves or changes the journal.</summary>
    public AsyncRelayCommand<JournalLineRow> OpenLineLedgerCommand => openLineLedgerCommand ??=
        new AsyncRelayCommand<JournalLineRow>(line => CanOpenLineLedger(line)
            ? Navigator!.NavigateAsync("account-ledger", new OpenAccountLedgerRequest(line!.AccountId, null, null, false))
            : Task.CompletedTask, CanOpenLineLedger);

    private bool CanOpenLineLedger(JournalLineRow? line) => CanViewAccountLedgers && !operation.IsBusy && Navigator is not null
        && IsEditorOpen && currentCompanyId != Guid.Empty && currentBranchId != Guid.Empty
        && line is { AccountId: var accountId } && accountId != Guid.Empty && Lines.Contains(line);

    private Task NavigateRelatedAsync(Guid id) => Navigator?.NavigateAsync("journals", new OpenJournalRequest(id)) ?? Task.CompletedTask;

    public string? PostingNumber => currentJournal?.PostingSequence?.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public string? PostingPeriod => currentJournal?.PostingPeriodId is { } id && periodNames.TryGetValue(id, out var name) ? name : null;
    public bool IsPosted => currentJournal?.State is JournalState.Posted or JournalState.Reversed;
    public bool IsReversal => currentJournal?.ReversesEntryId is not null;
    public bool IsReversed => currentJournal?.ReversedByEntryId is not null;
    public string BranchLabel { get; private set; } = string.Empty;
    public BadgeKind CurrentStateKind => StateKind(CurrentState);
    public string CurrentStateLabel => CurrentState is { } state ? localization["D_State_" + state] : IsNewDraft ? localization["D_State_Draft"] : string.Empty;

    public static BadgeKind StateKind(JournalState? state) => state switch
    {
        JournalState.Validated => BadgeKind.Info,
        JournalState.Approved => BadgeKind.Accent,
        JournalState.Posted => BadgeKind.Success,
        JournalState.Reversed => BadgeKind.Warning,
        _ => BadgeKind.Neutral,
    };

    public async Task ActivateAsync(ShellContext shellContext, object? parameter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shellContext);
        if (!shellContext.Can("Ledger.Journals.View")) { Clear(); Configure([]); operation.SetError("PermissionDenied"); return; }
        Configure(shellContext.Permissions);
        BranchLabel = shellContext.Branch.Code + " — " + (localization.IsRightToLeft ? shellContext.Branch.NameArabic : shellContext.Branch.NameEnglish ?? shellContext.Branch.NameArabic);
        OnPropertyChanged(nameof(BranchLabel));
        await LoadPageAsync(shellContext.CompanyId, shellContext.BranchId, shellContext.BaseCurrencyCode, cancellationToken).ConfigureAwait(true);
        linkContext = shellContext;
        if (Calendar is not null && shellContext.Can("Ledger.Periods.View"))
        {
            periodNames.Clear();
            foreach (var year in await Calendar.ListFiscalYearsAsync(shellContext.CompanyId, shellContext.BranchId, cancellationToken).ConfigureAwait(true))
            {
                foreach (var period in year.Periods) periodNames[period.Id] = year.Code + " / " + period.Code;
            }
        }

        switch (parameter)
        {
            case OpenJournalRequest open:
                await OpenByIdAsync(open.JournalId, cancellationToken).ConfigureAwait(true);
                break;
            case NewJournalRequest when CanManageJournals:
                NewDraft();
                break;
        }
        DocumentLinks?.SetSource(shellContext, SalesLinkSource.Journal, currentJournal?.Id);
        VoucherLinks?.SetSource(shellContext, Relationships.RelatedDocumentSource.JournalVouchers, currentJournal?.Id);
    }

    /// <summary>Opens a journal by id (drill-down from reports, palette, recent items and reversal links).</summary>
    public async Task OpenByIdAsync(Guid journalId, CancellationToken cancellationToken)
    {
        var details = await client.GetJournalAsync(currentCompanyId, currentBranchId, journalId, cancellationToken).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        DisplayJournal(details);
        SelectedJournal = Journals.FirstOrDefault(item => item.Id == journalId);
    }

    private void RebuildLookupItems()
    {
        AccountItems.Clear();
        foreach (var account in Accounts) AccountItems.Add(new LookupItem(account.Id, account.Code, account.NameArabic, account.NameEnglish));
        CostCenterItems.Clear();
        foreach (var center in CostCenters) CostCenterItems.Add(new LookupItem(center.Id, center.Code, center.NameArabic, center.NameEnglish));
    }

    private void NotifyShell()
    {
        DocumentLinks?.SetSource(linkContext, SalesLinkSource.Journal, currentJournal?.Id);
        VoucherLinks?.SetSource(HasUnsavedChanges ? null : linkContext, Relationships.RelatedDocumentSource.JournalVouchers, currentJournal?.Id);
        foreach (var name in new[] { nameof(PostingNumber), nameof(PostingPeriod), nameof(IsPosted), nameof(IsReversal), nameof(IsReversed),
                     nameof(CurrentStateKind), nameof(CurrentStateLabel), nameof(CurrentJournalId), nameof(DocumentTitle) })
        {
            OnPropertyChanged(name);
        }

        openOriginalCommand?.NotifyCanExecuteChanged();
        openReversalCommand?.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanViewAccountLedgers));
        openLineLedgerCommand?.NotifyCanExecuteChanged();
    }
}
