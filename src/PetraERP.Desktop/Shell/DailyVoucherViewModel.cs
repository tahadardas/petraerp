using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Accounting;
using PetraERP.Desktop.Controls;
using PetraERP.Desktop.GeneralLedger;
using PetraERP.Desktop.Localization;

namespace PetraERP.Desktop.Shell;

/// <summary>Opens the daily voucher on an existing journal that follows the daily-voucher layout.</summary>
public sealed record OpenDailyVoucherRequest(Guid JournalId);

/// <summary>One counter-account row of a daily voucher: money received from the account or paid to it.</summary>
public sealed partial class DailyVoucherRow : ObservableObject
{
    [ObservableProperty] private Guid? accountId;
    [ObservableProperty] private decimal receive;
    [ObservableProperty] private decimal pay;
    [ObservableProperty] private string description = string.Empty;
    [ObservableProperty] private Guid? costCenterId;
    public bool IsBlank => AccountId is null && Receive == 0m && Pay == 0m && string.IsNullOrWhiteSpace(Description) && CostCenterId is null;
    public bool IsPristineBlank => IsBlank;
    public bool IsComplete => AccountId is not null && ((Receive > 0m && Pay == 0m) || (Pay > 0m && Receive == 0m));
    public bool IsIncomplete => !IsBlank && !IsComplete;
}

/// <summary>
/// Al-Ameen "سند يومية": a cash-book voucher. The header names the cash account; every row names a counter account and either an
/// amount received (قبض) or paid (دفع). It is stored as an ordinary journal (no parallel ledger): for each row the journal holds
/// a cash line and a counter line, and a small marker row names the cash account so the document can be reopened.
/// Numbering, periods, approval, posting and reversal are the journal's own.
/// </summary>
public sealed partial class DailyVoucherViewModel : PageViewModel
{
    internal const string RouteId = "daily-vouchers";
    private readonly IGeneralLedgerClient ledger;
    private readonly Accounting.ILedgerReportsClient reports;
    private readonly TimeProvider time;
    private readonly IUserConfirmation confirmation;
    private JournalDetailsResponse? current;
    private Guid? pendingPostKey;
    private string baseCurrency = string.Empty;

    [ObservableProperty] private Guid? cashAccountId;
    [ObservableProperty] private DateTime voucherDate;
    [ObservableProperty] private string currencyCode = string.Empty;
    public ObservableCollection<string> CurrencyOptions { get; } = [];
    [ObservableProperty] private string rateText = "1";
    [ObservableProperty] private string voucherDescription = string.Empty;
    [ObservableProperty] private string previousBalance = "0";
    [ObservableProperty] private string statusText = string.Empty;
    [ObservableProperty] private string validationMessage = string.Empty;
    [ObservableProperty] private DailyVoucherRow? selectedRow;

    public DailyVoucherViewModel(IGeneralLedgerClient ledger, Accounting.ILedgerReportsClient reports, DesktopOperation operation, ILocalizationService localization,
        TimeProvider time, IUserConfirmation confirmation) : base(operation, localization)
    {
        this.ledger = ledger; this.reports = reports; this.time = time; this.confirmation = confirmation;
        ReverseCommand = new AsyncRelayCommand(ReverseVoucherAsync, () => CanReverse);
        Lookups = new LedgerLookups(ledger);
        voucherDate = time.GetLocalNow().Date;
        NewCommand = new RelayCommand(NewVoucher, () => HasContext && Idle);
        SaveCommand = new AsyncRelayCommand(() => RunAsync(SaveDraftAsync, OperationKind.Save), () => CanSave);
        PostCommand = new AsyncRelayCommand(() => RunAsync(PostAsync, OperationKind.Post), () => CanPost);
        DeleteCommand = new AsyncRelayCommand(() => RunAsync(DeleteAsync, OperationKind.Save), () => CanDelete);
        RefreshCommand = new AsyncRelayCommand(async () =>
        {
            await RefreshPreviousBalanceSafeAsync().ConfigureAwait(true);
            if (current is { } j)
            {
                var fresh = await ledger.GetJournalAsync(CompanyId, BranchId, j.Id, Token).ConfigureAwait(true);
                if (CashAccountId is { } cash) Display(fresh, cash);
            }
        }, () => HasContext && Idle);
        FirstVoucherCommand = new AsyncRelayCommand(() => RunAsync(token => MoveVoucherAsync(int.MinValue, token), OperationKind.Read), () => HasContext && Idle);
        PreviousVoucherCommand = new AsyncRelayCommand(() => RunAsync(token => MoveVoucherAsync(-1, token), OperationKind.Read), () => HasContext && Idle);
        NextVoucherCommand = new AsyncRelayCommand(() => RunAsync(token => MoveVoucherAsync(1, token), OperationKind.Read), () => HasContext && Idle);
        LastVoucherCommand = new AsyncRelayCommand(() => RunAsync(token => MoveVoucherAsync(int.MaxValue, token), OperationKind.Read), () => HasContext && Idle);
        JumpCommand = new AsyncRelayCommand<string>(target => RunAsync(token => JumpToVoucherAsync(target, token), OperationKind.Read), _ => HasContext && Idle);
        AutoGrowCommand = new RelayCommand(() => AutoGrow(10), () => IsEditable);
        AutoBalanceCommand = new RelayCommand(AutoBalance, () => IsEditable && !IsBalanced);
        AddRowCommand = new RelayCommand(AddRow, () => IsEditable);
        RemoveRowCommand = new RelayCommand<DailyVoucherRow>(RemoveRow, row => IsEditable && row is not null);
        Rows.CollectionChanged += (_, e) => { Hook(e); Recalculate(); };
        EnsureRowBuffer(50);
    }

    protected override string RequiredPermission => "Ledger.Journals.View";

    public LedgerLookups Lookups { get; }
    public ObservableCollection<DailyVoucherRow> Rows { get; } = [];
    public RelayCommand NewCommand { get; }
    public AsyncRelayCommand SaveCommand { get; }
    public AsyncRelayCommand PostCommand { get; }
    public AsyncRelayCommand DeleteCommand { get; }
    public AsyncRelayCommand ReverseCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand FirstVoucherCommand { get; }
    public AsyncRelayCommand PreviousVoucherCommand { get; }
    public AsyncRelayCommand NextVoucherCommand { get; }
    public AsyncRelayCommand LastVoucherCommand { get; }
    public AsyncRelayCommand<string> JumpCommand { get; }
    public RelayCommand AutoGrowCommand { get; }
    public RelayCommand AutoBalanceCommand { get; }
    public RelayCommand AddRowCommand { get; }
    public RelayCommand<DailyVoucherRow> RemoveRowCommand { get; }

    public Guid? JournalId => current?.Id;
    public bool IsPosted => current is { State: not (JournalState.Draft or JournalState.Validated or JournalState.Approved) };
    public bool IsEditable => HasContext && Can("Ledger.Journals.Manage") && !IsPosted;
    public bool CanSave => IsEditable && Idle;
    public bool CanPost => IsEditable && Idle && Can("Ledger.Journals.Post") && Can("Ledger.Journals.Approve");
    public bool CanDelete => IsEditable && Idle && current is not null;
    /// <summary>"إلغاء ترحيل": a posted, not yet reversed voucher gets a posted reversal journal (the original is never edited).</summary>
    public bool CanReverse => HasContext && Idle && Can("Ledger.Journals.Reverse") && Can("Ledger.Journals.Post")
        && current is { State: JournalState.Posted, ReversedByEntryId: null, ReversesEntryId: null };
    public string VoucherNumber => current?.PostingSequence is { } n ? n.ToString(CultureInfo.InvariantCulture) : string.Empty;
    public decimal TotalReceive => Rows.Sum(r => r.Receive);
    public decimal TotalPay => Rows.Sum(r => r.Pay);
    public string TotalReceiveText => Format(TotalReceive);
    public string TotalPayText => Format(TotalPay);
    public decimal Difference => Math.Abs(TotalReceive - TotalPay);
    public string DifferenceText => Format(Difference);
    public bool IsBalanced => TotalReceive == TotalPay;
    public int FilledRowCount => Rows.Count(r => r.IsComplete);
    /// <summary>The cash account balance before this voucher plus what it receives minus what it pays.</summary>
    public string FinalBalance => Format(ParseDecimal(PreviousBalance) + TotalReceive - TotalPay);
    public string BaseCurrencyCode => baseCurrency;
    public string TafqeetText => ArabicTafqeet.ToWordsOrEmpty(Math.Max(TotalPay, TotalReceive), CurrencyCode);

    partial void OnCurrencyCodeChanged(string value)
    {
        if (string.Equals(value, baseCurrency, StringComparison.OrdinalIgnoreCase))
        {
            RateText = "1";
        }
        OnPropertyChanged(nameof(TafqeetText));
    }

    public void Localize() => OnPropertyChanged(nameof(StatusText));

    protected override async Task LoadAsync(object? parameter, CancellationToken cancellationToken)
    {
        if (Lookups.Accounts.Count == 0 && Lookups.CostCenters.Count == 0) await Lookups.LoadAsync(Context!, cancellationToken).ConfigureAwait(true);
        var currencyContext = await ledger.GetCurrencyContextAsync(CompanyId, BranchId, cancellationToken).ConfigureAwait(true);
        baseCurrency = currencyContext.BaseCurrency.Code;
        OnPropertyChanged(nameof(BaseCurrencyCode));
        CurrencyOptions.Clear();
        if (!string.IsNullOrEmpty(baseCurrency)) CurrencyOptions.Add(baseCurrency);
        foreach (var cur in currencyContext.AdditionalCurrencies)
        {
            if (!string.IsNullOrEmpty(cur.Code) && !CurrencyOptions.Contains(cur.Code))
                CurrencyOptions.Add(cur.Code);
        }
        foreach (var fallback in new[] { "SYP", "USD", "EUR", "SAR", "AED", "JOD" })
        {
            if (!CurrencyOptions.Contains(fallback)) CurrencyOptions.Add(fallback);
        }
        if (string.IsNullOrEmpty(CurrencyCode)) CurrencyCode = baseCurrency;
        if (parameter is OpenDailyVoucherRequest request)
        {
            var journal = await ledger.GetJournalAsync(CompanyId, BranchId, request.JournalId, cancellationToken).ConfigureAwait(true);
            var header = await ledger.GetVoucherHeaderAsync(CompanyId, BranchId, request.JournalId, cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            if (header.CashAccountId is not { } cashId || cashId == Guid.Empty) { ValidationMessage = Localization["D_DV_NotAVoucher"]; return; }
            Display(journal, cashId);
            await RefreshPreviousBalanceAsync(cancellationToken).ConfigureAwait(true);
        }
        NotifyCommands();
    }

    protected override void ClearPage()
    {
        current = null; pendingPostKey = null; baseCurrency = string.Empty; CashAccountId = null; CurrencyCode = string.Empty; RateText = "1";
        VoucherDescription = string.Empty; PreviousBalance = "0"; StatusText = string.Empty; ValidationMessage = string.Empty;
        Lookups.Clear(); ResetRows(); VoucherDate = time.GetLocalNow().Date;
        OnPropertyChanged(nameof(IsPosted)); OnPropertyChanged(nameof(VoucherNumber)); OnPropertyChanged(nameof(JournalId));
    }

    protected override void NotifyCommands()
    {
        OnPropertyChanged(nameof(IsEditable)); OnPropertyChanged(nameof(CanSave)); OnPropertyChanged(nameof(CanPost)); OnPropertyChanged(nameof(CanDelete)); OnPropertyChanged(nameof(CanReverse));
        ReverseCommand?.NotifyCanExecuteChanged();
        NewCommand?.NotifyCanExecuteChanged(); SaveCommand?.NotifyCanExecuteChanged(); PostCommand?.NotifyCanExecuteChanged();
        DeleteCommand?.NotifyCanExecuteChanged(); AddRowCommand?.NotifyCanExecuteChanged(); RemoveRowCommand?.NotifyCanExecuteChanged();
        AutoGrowCommand?.NotifyCanExecuteChanged();
        AutoBalanceCommand?.NotifyCanExecuteChanged();
        RefreshCommand?.NotifyCanExecuteChanged();
        FirstVoucherCommand?.NotifyCanExecuteChanged(); PreviousVoucherCommand?.NotifyCanExecuteChanged();
        NextVoucherCommand?.NotifyCanExecuteChanged(); LastVoucherCommand?.NotifyCanExecuteChanged();
        JumpCommand?.NotifyCanExecuteChanged();
    }

    partial void OnCashAccountIdChanged(Guid? value) { _ = RefreshPreviousBalanceSafeAsync(); }
    partial void OnVoucherDateChanged(DateTime value) { _ = RefreshPreviousBalanceSafeAsync(); }
    partial void OnPreviousBalanceChanged(string value) => OnPropertyChanged(nameof(FinalBalance));

    private async Task RefreshPreviousBalanceSafeAsync()
    {
        try { await RefreshPreviousBalanceAsync(Token).ConfigureAwait(true); }
        catch (Exception exception) when (exception is OperationCanceledException or InvalidOperationException) { }
    }

    private async Task RefreshPreviousBalanceAsync(CancellationToken cancellationToken)
    {
        if (!HasContext || CashAccountId is not { } account || !Can("Ledger.Reports.View")) { PreviousBalance = "0"; return; }
        var until = DateOnly.FromDateTime(VoucherDate.Date).AddDays(-1);
        var report = await reports.GetAccountLedgerAsync(CompanyId, BranchId, account, null, until, false, 1, 1, null, cancellationToken).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        PreviousBalance = report.ClosingBalance;
    }

    /// <summary>
    /// Moves to the first / previous / next / last daily voucher (offset int.MinValue / -1 / 1 / int.MaxValue) in accounting-date then
    /// posting-sequence order. Journals that are not cash vouchers are skipped, so the arrows only ever land on a voucher this window can show.
    /// </summary>
    private async Task MoveVoucherAsync(int offset, CancellationToken cancellationToken)
    {
        var all = new List<JournalSummaryItemResponse>();
        for (var page = 1; page <= 25; page++)
        {
            var result = await ledger.SearchJournalsAsync(CompanyId, BranchId, null, null, null, null, page, 200, cancellationToken).ConfigureAwait(true);
            all.AddRange(result.Items);
            if (result.Items.Count == 0 || all.Count >= result.TotalCount) break;
        }

        var ordered = all.OrderBy(j => j.AccountingDate).ThenBy(j => j.PostingSequence ?? long.MaxValue).ToList();
        if (ordered.Count == 0) { ValidationMessage = Localization["D_DV_NotAVoucher"]; return; }
        var index = current is null ? -1 : ordered.FindIndex(j => j.Id == current.Id);
        var step = offset == int.MinValue ? 1 : offset == int.MaxValue ? -1 : Math.Sign(offset);
        var start = offset == int.MinValue ? 0 : offset == int.MaxValue ? ordered.Count - 1 : index < 0 ? (offset > 0 ? 0 : ordered.Count - 1) : index + offset;
        for (var i = start; i >= 0 && i < ordered.Count; i += step)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var journal = await ledger.GetJournalAsync(CompanyId, BranchId, ordered[i].Id, cancellationToken).ConfigureAwait(true);
            var header = await ledger.GetVoucherHeaderAsync(CompanyId, BranchId, ordered[i].Id, cancellationToken).ConfigureAwait(true);
            if (header.CashAccountId is not { } cashId || cashId == Guid.Empty) continue;
            ValidationMessage = string.Empty;
            Display(journal, cashId);
            await RefreshPreviousBalanceAsync(cancellationToken).ConfigureAwait(true);
            NotifyCommands();
            return;
        }
    }

    private void NewVoucher()
    {
        current = null; pendingPostKey = null; VoucherDescription = string.Empty; ValidationMessage = string.Empty; StatusText = string.Empty;
        ResetRows(); VoucherDate = time.GetLocalNow().Date; CurrencyCode = baseCurrency; RateText = "1";
        OnPropertyChanged(nameof(IsPosted)); OnPropertyChanged(nameof(VoucherNumber)); OnPropertyChanged(nameof(JournalId)); NotifyCommands();
    }

    public void EnsureRowBuffer(int targetCount = 50)
    {
        var limit = Math.Min(targetCount, 200);
        while (Rows.Count < limit)
        {
            var row = new DailyVoucherRow();
            Rows.Add(row);
        }
        if (SelectedRow is null && Rows.Count > 0) SelectedRow = Rows[0];
    }

    public void AutoGrow(int additional = 10)
    {
        if (!IsEditable) return;
        EnsureRowBuffer(Rows.Count + additional);
    }

    private void ResetRows()
    {
        foreach (var row in Rows.ToArray()) Rows.Remove(row);
        EnsureRowBuffer(50);
    }

    private void AddRow()
    {
        var row = new DailyVoucherRow();
        Rows.Add(row); SelectedRow = row;
    }

    private void RemoveRow(DailyVoucherRow? row)
    {
        if (row is null) return;
        Rows.Remove(row);
        if (Rows.Count == 0) EnsureRowBuffer(50);
    }

    private void Hook(System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null) foreach (DailyVoucherRow row in e.NewItems) row.PropertyChanged += (_, _) => Recalculate();
    }

    private void Recalculate()
    {
        OnPropertyChanged(nameof(TotalReceive)); OnPropertyChanged(nameof(TotalPay)); OnPropertyChanged(nameof(TotalReceiveText));
        OnPropertyChanged(nameof(TotalPayText)); OnPropertyChanged(nameof(FinalBalance));
        OnPropertyChanged(nameof(Difference)); OnPropertyChanged(nameof(DifferenceText));
        OnPropertyChanged(nameof(IsBalanced)); OnPropertyChanged(nameof(FilledRowCount));
        OnPropertyChanged(nameof(TafqeetText));
        AutoBalanceCommand?.NotifyCanExecuteChanged();
    }

    private async Task JumpToVoucherAsync(string? target, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(target)) return;
        var trimmed = target.Trim();
        var result = await ledger.SearchJournalsAsync(CompanyId, BranchId, null, null, null, trimmed, 1, 10, cancellationToken).ConfigureAwait(true);
        if (result.Items.Count == 0) return;
        JournalSummaryItemResponse? match = null;
        for (var i = 0; i < result.Items.Count; i++)
        {
            var item = result.Items[i];
            if (string.Equals(item.PostingSequence?.ToString(CultureInfo.InvariantCulture), trimmed, StringComparison.OrdinalIgnoreCase))
            {
                match = item;
                break;
            }
        }
        match ??= result.Items[0];
        var fresh = await ledger.GetJournalAsync(CompanyId, BranchId, match.Id, cancellationToken).ConfigureAwait(true);
        if (CashAccountId is { } cash) Display(fresh, cash);
    }

    public void AutoBalance()
    {
        if (!IsEditable || IsBalanced) return;
        var diff = TotalReceive - TotalPay;
        if (diff == 0m) return;

        var target = SelectedRow ?? Rows.FirstOrDefault(r => r.IsIncomplete || r.IsPristineBlank);
        if (target is null)
        {
            AutoGrow(10);
            target = Rows.FirstOrDefault(r => r.IsPristineBlank);
        }
        if (target is null) return;

        if (diff > 0m)
        {
            target.Pay = diff;
            target.Receive = 0m;
        }
        else
        {
            target.Receive = Math.Abs(diff);
            target.Pay = 0m;
        }
        Recalculate();
    }

    /// <summary>Shows a journal as a voucher: lines on the cash account are the cash side, every other line becomes a row.</summary>
    private void Display(JournalDetailsResponse journal, Guid cashAccount)
    {
        current = journal; pendingPostKey = null;
        var lines = journal.Lines.ToList();
        CashAccountId = cashAccount;
        VoucherDate = journal.AccountingDate.ToDateTime(TimeOnly.MinValue);
        VoucherDescription = journal.Description;
        if (lines.FirstOrDefault(l => l.AccountId == cashAccount) is { } cashLine)
        {
            CurrencyCode = cashLine.TransactionCurrencyCode;
            RateText = cashLine.RateValue;
        }
        foreach (var row in Rows.ToArray()) Rows.Remove(row);
        foreach (var line in lines.Where(l => l.AccountId != CashAccountId))
        {
            Rows.Add(new DailyVoucherRow
            {
                AccountId = line.AccountId,
                Receive = ParseDecimal(line.TransactionCredit),
                Pay = ParseDecimal(line.TransactionDebit),
                Description = line.Description,
                CostCenterId = line.CostCenterId,
            });
        }
        if (Rows.Count == 0) EnsureRowBuffer(50);
        else if (IsEditable) EnsureRowBuffer(Math.Max(50, Rows.Count));
        StatusText = journal.StateName;
        OnPropertyChanged(nameof(IsPosted)); OnPropertyChanged(nameof(VoucherNumber)); OnPropertyChanged(nameof(JournalId)); NotifyCommands(); Recalculate();
    }

    /// <summary>Checks the voucher and returns the journal lines, or null with <see cref="ValidationMessage"/> set.</summary>
    internal List<JournalLineDraftDto>? BuildLines()
    {
        ValidationMessage = string.Empty;
        if (CashAccountId is not { } cash) return Fail("D_DV_CashRequired");
        var rows = Rows.Where(r => !r.IsBlank).ToList();
        if (rows.Count == 0) return Fail("D_DV_RowsRequired");
        var incompleteIndex = Rows.ToList().FindIndex(r => r.IsIncomplete);
        if (incompleteIndex >= 0)
        {
            ValidationMessage = string.Format(CultureInfo.CurrentCulture, Localization["D_LineRowError"], Localization["D_DV_Rows"], incompleteIndex + 1, Localization["D_DV_RowIncomplete"]);
            return null;
        }
        if (rows.Any(r => r.AccountId == cash)) return Fail("D_DV_CounterIsCash");
        if (!decimal.TryParse(RateText?.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var rate) || rate <= 0m || rate >= 100_000_000_000m)
            return Fail("D_DV_RateInvalid");
        var currency = string.IsNullOrWhiteSpace(CurrencyCode) ? baseCurrency : CurrencyCode.Trim().ToUpperInvariant();
        var lines = new List<JournalLineDraftDto>();
        JournalLineDraftDto Line(Guid account, Guid? center, decimal debit, decimal credit, string description) => new()
        {
            AccountId = account,
            CostCenterId = center,
            TransactionCurrencyCode = currency,
            TransactionDebit = Money(debit),
            TransactionCredit = Money(credit),
            RateValue = rate.ToString("0.00000000", CultureInfo.InvariantCulture),
            RateSourceCurrencyCode = currency,
            RateTargetCurrencyCode = baseCurrency,
            BaseDebit = Money(Math.Round(debit * rate, 4, MidpointRounding.AwayFromZero)),
            BaseCredit = Money(Math.Round(credit * rate, 4, MidpointRounding.AwayFromZero)),
            Rounding = 0,
            Description = string.IsNullOrWhiteSpace(description) ? VoucherDescription.Trim() : description.Trim(),
        };
        foreach (var row in rows)
        {
            // Money received debits the cash account and credits the counter account; money paid does the opposite.
            lines.Add(Line(cash, null, row.Receive, row.Pay, row.Description));
            lines.Add(Line(row.AccountId!.Value, row.CostCenterId, row.Pay, row.Receive, row.Description));
        }

        return lines;
    }

    private List<JournalLineDraftDto>? Fail(string key)
    {
        ValidationMessage = Localization[key];
        return null;
    }

    private async Task<JournalDetailsResponse?> SaveCoreAsync(CancellationToken token)
    {
        var lines = BuildLines();
        if (lines is null) return null;
        var description = string.IsNullOrWhiteSpace(VoucherDescription) ? Localization["D_DV_DefaultDescription"] : VoucherDescription.Trim();
        var date = DateOnly.FromDateTime(VoucherDate.Date);
        JournalDetailsResponse saved;
        if (current is null)
            saved = await ledger.CreateDraftAsync(CompanyId, BranchId, new CreateJournalDraftRequest { AccountingDate = date, Description = description, Lines = lines }, token).ConfigureAwait(true);
        else
            saved = await ledger.ReviseDraftAsync(CompanyId, BranchId, current.Id, new ReviseJournalDraftRequest
            {
                ExpectedVersion = current.Version, AccountingDate = date, Description = description, Lines = lines, Reason = Localization["D_ReasonRevision"],
            }, token).ConfigureAwait(true);
        token.ThrowIfCancellationRequested();
        var cash = CashAccountId!.Value;
        await ledger.SetVoucherHeaderAsync(CompanyId, BranchId, saved.Id, new JournalVoucherHeaderDto { Kind = "DAILY", CashAccountId = cash }, token).ConfigureAwait(true);
        Display(saved, cash);
        return saved;
    }

    private async Task SaveDraftAsync(CancellationToken token) => await SaveCoreAsync(token).ConfigureAwait(true);

    private async Task PostAsync(CancellationToken token)
    {
        var saved = await SaveCoreAsync(token).ConfigureAwait(true);
        if (saved is null) return;
        var reason = Localization["D_DV_PostReason"];
        var validated = await ledger.ValidateDraftAsync(CompanyId, BranchId, saved.Id, new JournalActionRequest { ExpectedVersion = saved.Version, Reason = reason }, token).ConfigureAwait(true);
        var approved = await ledger.ApproveDraftAsync(CompanyId, BranchId, saved.Id, new JournalActionRequest { ExpectedVersion = validated.Version, Reason = reason }, token).ConfigureAwait(true);
        pendingPostKey ??= Guid.NewGuid();
        await ledger.PostJournalAsync(CompanyId, BranchId, saved.Id, new PostJournalRequest { ExpectedVersion = approved.Version, Reason = reason }, pendingPostKey.Value, token).ConfigureAwait(true);
        pendingPostKey = null;
        Display(await ledger.GetJournalAsync(CompanyId, BranchId, saved.Id, token).ConfigureAwait(true), CashAccountId!.Value);
        await RefreshPreviousBalanceAsync(token).ConfigureAwait(true);
    }

    private async Task DeleteAsync(CancellationToken token)
    {
        if (current is null) return;
        await ledger.DeleteDraftAsync(CompanyId, BranchId, current.Id, current.Version, token).ConfigureAwait(true);
        NewVoucher();
    }

    private async Task ReverseVoucherAsync()
    {
        if (current is not { } original || CashAccountId is not { } cash || !CanReverse) return;
        if (!await confirmation.ConfirmAsync("D_ConfirmCreateReversal").ConfigureAwait(true)) return;
        await RunAsync(async token =>
        {
            var reason = Localization["D_DV_ReverseReason"];
            var draft = await ledger.CreateReversalDraftAsync(CompanyId, BranchId, original.Id, new CreateReversalDraftRequest
            {
                OriginalJournalId = original.Id, OriginalExpectedVersion = original.Version,
                AccountingDate = DateOnly.FromDateTime(time.GetLocalNow().Date), Reason = reason,
            }, token).ConfigureAwait(true);
            if (draft.State == JournalState.Draft)
                draft = await ledger.ValidateDraftAsync(CompanyId, BranchId, draft.Id, new JournalActionRequest { ExpectedVersion = draft.Version, Reason = reason }, token).ConfigureAwait(true);
            if (draft.State == JournalState.Validated)
                draft = await ledger.ApproveDraftAsync(CompanyId, BranchId, draft.Id, new JournalActionRequest { ExpectedVersion = draft.Version, Reason = reason }, token).ConfigureAwait(true);
            var fresh = await ledger.GetJournalAsync(CompanyId, BranchId, original.Id, token).ConfigureAwait(true);
            await ledger.PostJournalReversalAsync(CompanyId, BranchId, draft.Id, new PostJournalReversalRequest
            {
                ExpectedVersion = draft.Version, OriginalJournalId = fresh.Id, OriginalExpectedVersion = fresh.Version, Reason = reason,
            }, Guid.NewGuid(), token).ConfigureAwait(true);
            Display(await ledger.GetJournalAsync(CompanyId, BranchId, original.Id, token).ConfigureAwait(true), cash);
            await RefreshPreviousBalanceAsync(token).ConfigureAwait(true);
        }, OperationKind.Post).ConfigureAwait(true);
    }

    private static string Money(decimal value) => value.ToString("0.0000", CultureInfo.InvariantCulture);
    private static string Format(decimal value) => PetraNumberFormat.Format(Money(value), NumberKind.Money, false);
    private static decimal ParseDecimal(string? text) =>
        decimal.TryParse(text, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) ? value : 0m;
}
