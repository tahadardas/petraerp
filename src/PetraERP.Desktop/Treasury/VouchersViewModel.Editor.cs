using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Connection;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Treasury;

public sealed partial class VouchersViewModel
{
    private string? pendingPostKey;
    private string? pendingPostVersion;
    private bool showingDocument;
    private string? pendingSaveKey;
    private string? pendingSaveFingerprint;
    /// <summary>Identity returned by the server for the pending save; survives a failed follow-up read so the editor never starts a second document.</summary>
    private Guid? uncertainDocumentId;

    [ObservableProperty] private string saveOutcomeMessage = string.Empty;
    [ObservableProperty] private bool saveOutcomeIsWarning;
    public bool HasSaveOutcome => !string.IsNullOrWhiteSpace(SaveOutcomeMessage);
    partial void OnSaveOutcomeMessageChanged(string value) => OnPropertyChanged(nameof(HasSaveOutcome));

    [ObservableProperty] private VoucherDetailDto? current;
    [ObservableProperty] private bool isEditing;
    [ObservableProperty] private VoucherKind draftKind = VoucherKind.Receipt;
    [ObservableProperty] private Guid? draftPatternRevisionId;
    [ObservableProperty] private Guid? draftPartyId;
    [ObservableProperty] private SettlementMethod draftSettlement = SettlementMethod.Cash;
    [ObservableProperty] private DateTime? draftDate = DateTime.Today;
    [ObservableProperty] private string draftCurrency = string.Empty;
    [ObservableProperty] private string draftRate = string.Empty;
    /// <summary>Where the displayed rate came from, or why it is missing; the operator is never left guessing.</summary>
    [ObservableProperty] private string rateInfo = string.Empty;
    [ObservableProperty] private string draftAmount = string.Empty;
    [ObservableProperty] private string? draftNotes;
    /// <summary>The number and date printed on the paper document this voucher records (Al-Ameen "رقم الأصل / تاريخ الأصل").</summary>
    [ObservableProperty] private string? draftSourceNumber;
    [ObservableProperty] private DateTime? draftSourceDate;
    [ObservableProperty] private string reverseReason = string.Empty;
    [ObservableProperty] private VoucherLineItem? selectedLine;
    [ObservableProperty] private VoucherAllocationItem? selectedAllocation;

    public ObservableCollection<VoucherAllocationItem> AllocationRows { get; } = [];
    public ObservableCollection<VoucherLineItem> LineRows { get; } = [];
    public bool IsReceipt => DraftKind == VoucherKind.Receipt;
    public bool IsPayment => DraftKind == VoucherKind.Payment;
    public bool IsPartySettlement => IsReceipt || DraftPartyId is not null;
    public bool IsDistributionPayment => IsPayment && DraftPartyId is null;
    public bool ShowDistributionPanel => IsPayment && (DraftPartyId is null || LineRows.Count > 0);
    public bool HasAllocationHistory => OriginalAllocations.Count > 0 || SettlementBatches.Count > 0;
    public bool HasDistributionConflict => IsPayment && DraftPartyId is not null && LineRows.Count > 0;
    public bool IsDraft => Current is null || Current.Status == VoucherStatus.Draft;
    public bool CanAllocate => Can("Treasury.Vouchers.Allocate") || Can("Treasury.Vouchers.Manage");
    public bool CanApprovePermission => Can("Treasury.Vouchers.Approve");
    public bool CanEditFields => IsEditing && IsDraft;
    public bool CanEditDistribution => CanEditFields && IsDistributionPayment && Idle;
    public bool CanEnterFields => CanEditFields && Idle;
    private Guid? lockedPatternRevisionId;
    /// <summary>A new voucher started from a pattern button keeps that pattern while it is unsaved.</summary>
    public bool PatternPickerEnabled => CanEnterFields && !(lockedPatternRevisionId.HasValue && Current is null);
    public bool IsWorkspaceIdle => HasContext && Idle;
    public bool CanEditAllocations => CanAllocate && (CanEditFields || IsApplyingAdvance);
    public bool IsAllocationReadOnly => !CanEditAllocations || !Idle;
    public bool IsFieldsReadOnly => !CanEditFields;
    public IEnumerable<Sales.PatternLookupItem> Patterns => IsReceipt ? ReceiptPatterns : PaymentPatterns;
    public IEnumerable<PartyLookupDto> Parties => IsReceipt ? Customers : Suppliers;

    public Func<string, CancellationToken, Task<IReadOnlyList<Controls.LookupItem>>> SearchPartiesAsync => async (query, ct) =>
    {
        if (CompanyId == Guid.Empty || BranchId == Guid.Empty) return [];
        var role = IsReceipt ? PartyRole.Customer : PartyRole.Supplier;
        var parties = await partiesClient.LookupPartiesAsync(CompanyId, BranchId, 50, query, role, ct).ConfigureAwait(false);
        var targetCollection = IsReceipt ? Customers : Suppliers;
        foreach (var party in parties)
        {
            if (party.Id.HasValue && !targetCollection.Any(p => p.Id == party.Id.Value))
            {
                App.Current?.Dispatcher.BeginInvoke(() =>
                {
                    if (!targetCollection.Any(p => p.Id == party.Id.Value))
                    {
                        targetCollection.Add(party);
                    }
                });
            }
        }
        return parties.Where(p => p.Id.HasValue).Select(p => new Controls.LookupItem(p.Id!.Value, p.Code ?? string.Empty, p.NameArabic ?? string.Empty, p.NameEnglish)).ToList();
    };
    public string StatusText => Current is null ? Localization["D_VoucherNew"] : Current.Status switch
    {
        VoucherStatus.Approved => Localization["ARP_Approved"],
        VoucherStatus.Posted => Localization["D_VoucherPosted"],
        VoucherStatus.Reversed => Localization["D_VoucherReversed"],
        _ => Localization["D_VoucherDraft"],
    };
    public string SelectionTotalDisplay => Total(AllocationRows.Select(r => r.SettlementAmount)).ToString("F4", CultureInfo.InvariantCulture);
    public string AllocatedDisplay => Current is { Status: VoucherStatus.Posted or VoucherStatus.Reversed }
        ? Current.AllocatedAmount ?? "0.0000" : SelectionTotalDisplay;
    public string AdvanceDisplay => Current is { Status: VoucherStatus.Posted or VoucherStatus.Reversed } ? Current.AdvanceAmount ?? "0.0000"
        : IsPartySettlement && Parse(DraftAmount) is { } amount ? (amount - Total(AllocationRows.Select(r => r.SettlementAmount))).ToString("F4", CultureInfo.InvariantCulture) : "0.0000";
    public string LinesTotalDisplay => Total(LineRows.Where(r => r.IsComplete).Select(r => r.Amount)).ToString("F4", CultureInfo.InvariantCulture);

    public AsyncRelayCommand SaveCommand { get; private set; } = null!;
    public AsyncRelayCommand DeleteCommand { get; private set; } = null!;
    public AsyncRelayCommand ApproveCommand { get; private set; } = null!;
    public AsyncRelayCommand PostCommand { get; private set; } = null!;
    public AsyncRelayCommand ReverseCommand { get; private set; } = null!;
    public AsyncRelayCommand LoadOutstandingCommand { get; private set; } = null!;
    public AsyncRelayCommand CancelCommand { get; private set; } = null!;
    public RelayCommand EditCommand { get; private set; } = null!;
    public RelayCommand AddLineCommand { get; private set; } = null!;
    public RelayCommand RemoveLineCommand { get; private set; } = null!;
    public RelayCommand AutoGrowLinesCommand { get; private set; } = null!;
    public AsyncRelayCommand FirstVoucherCommand { get; private set; } = null!;
    public AsyncRelayCommand PreviousVoucherCommand { get; private set; } = null!;
    public AsyncRelayCommand NextVoucherCommand { get; private set; } = null!;
    public AsyncRelayCommand LastVoucherCommand { get; private set; } = null!;
    public AsyncRelayCommand ClearDistributionCommand { get; private set; } = null!;
    private string validationMessage = string.Empty;
    private IReadOnlyList<string> validationErrors = [];
    public string ValidationMessage { get => validationMessage; set { if (SetProperty(ref validationMessage, value)) OnPropertyChanged(nameof(HasValidationErrors)); } }
    public IReadOnlyList<string> ValidationErrors { get => validationErrors; set { if (SetProperty(ref validationErrors, value)) OnPropertyChanged(nameof(HasValidationErrors)); } }
    public bool HasValidationErrors => ValidationErrors.Count > 0 || !string.IsNullOrWhiteSpace(ValidationMessage);
    public AsyncRelayCommand OpenJournalCommand { get; private set; } = null!;
    public AsyncRelayCommand OpenReversalJournalCommand { get; private set; } = null!;
    public AsyncRelayCommand OpenPartyCommand { get; private set; } = null!;
    public AsyncRelayCommand OpenInvoiceCommand { get; private set; } = null!;

    private void InitializeEditor()
    {
        // Document-local: a lost response must not trigger the shell's offline reset, which would discard the editor and its pending save identity.
        SaveCommand = new(() => RunDocumentAsync(() => SaveAsync(Token), OperationKind.Save), () => HasContext && Idle && CanEditFields);
        DeleteCommand = new(DeleteAsync, () => HasContext && Idle && CanManage && Current is { Status: VoucherStatus.Draft } && !EditingSession);
        ApproveCommand = new(() => RunAsync(ApproveAsync), () => HasContext && Idle && CanApprovePermission && Current is { Status: VoucherStatus.Draft }
            && !EditingSession && !string.IsNullOrWhiteSpace(ReverseReason));
        PostCommand = new(PostAsync, () => HasContext && Idle && CanPostPermission && Current is { Status: VoucherStatus.Draft or VoucherStatus.Approved } && !EditingSession
            && (CanAllocate || !AllocationRows.Any(r => !string.IsNullOrWhiteSpace(r.Amount))));
        ReverseCommand = new(ReverseAsync, () => HasContext && Idle && CanReversePermission && Current is { Status: VoucherStatus.Posted }
            && !EditingSession && !string.IsNullOrWhiteSpace(ReverseReason));
        LoadOutstandingCommand = new(() => RunAsync(t => LoadOutstandingCoreAsync(null, t)), CanLoadInvoices);
        EditCommand = new(() => {
            if (Current is not null && Current.Status != VoucherStatus.Draft)
            {
                ValidationMessage = Localization["D_DocumentPostedLocked"];
                return;
            }
            IsEditing = true; if (IsDistributionPayment) EnsureDistributionBuffer(50); CaptureDraftBaseline(); DraftChanged(); RefreshNumberPreview();
        }, () => HasContext && Idle && CanManage && (Current is null || Current.Status == VoucherStatus.Draft) && !EditingSession);
        CancelCommand = new(CancelAsync, () => HasContext && Idle && EditingSession);
        AddLineCommand = new(() =>
        {
            var target = LineRows.FirstOrDefault(l => l.IsPristineBlank);
            if (target is null)
            {
                if (LineRows.Count >= 200) return;
                target = VoucherLineItem.CreateBlankSlot(DraftChanged);
                LineRows.Add(target);
            }
            SelectedLine = target;
            DraftChanged();
        }, () => HasContext && Idle && CanEditDistribution && LineRows.Count < 200);
        AutoGrowLinesCommand = new(() => AutoGrowDistribution(10), () => HasContext && Idle && CanEditDistribution && LineRows.Count < 200);
        FirstVoucherCommand = new(() => MoveVoucherAsync(int.MinValue), () => HasContext && Idle && Vouchers.Count > 0 && !IsEditing);
        PreviousVoucherCommand = new(() => MoveVoucherAsync(-1), () => HasContext && Idle && Vouchers.Count > 0 && !IsEditing);
        NextVoucherCommand = new(() => MoveVoucherAsync(1), () => HasContext && Idle && Vouchers.Count > 0 && !IsEditing);
        LastVoucherCommand = new(() => MoveVoucherAsync(int.MaxValue), () => HasContext && Idle && Vouchers.Count > 0 && !IsEditing);
        RemoveLineCommand = new(() => { if (SelectedLine is not null) LineRows.Remove(SelectedLine); DraftChanged(); },
            () => HasContext && Idle && CanEditFields && IsPayment && SelectedLine is not null);
        ClearDistributionCommand = new(async () =>
        {
            if (!await confirmation.ConfirmAsync("D_ConfirmDiscardChanges").ConfigureAwait(true)) return;
            LineRows.Clear(); SelectedLine = null; DraftChanged();
        }, () => CanEditFields && Idle && HasDistributionConflict);
        OpenJournalCommand = new(() => Current?.JournalEntryId is { } id ? navigator.NavigateAsync("journals", new OpenJournalRequest(id)) : Task.CompletedTask,
            () => HasContext && Idle && Current?.JournalEntryId is not null && !EditingSession && Can("Ledger.Journals.View"));
        OpenReversalJournalCommand = new(() => Current?.ReversalJournalEntryId is { } id ? navigator.NavigateAsync("journals", new OpenJournalRequest(id)) : Task.CompletedTask,
            () => HasContext && Idle && Current?.ReversalJournalEntryId is not null && !EditingSession && Can("Ledger.Journals.View"));
        OpenPartyCommand = new(() => Current?.PartyId is { } id ? navigator.NavigateAsync("party-card", new OpenPartyCardRequest(id)) : Task.CompletedTask,
            () => HasContext && Idle && Current?.PartyId is not null && !EditingSession && Can("Parties.Directory.View"));
        OpenInvoiceCommand = new(OpenInvoiceAsync, () => HasContext && Idle && SelectedAllocation is { InvoiceId: var id } && id != Guid.Empty
            && Can(IsReceipt ? "Sales.Invoices.View" : "Purchases.Invoices.View"));
        InitializeSettlements();
    }

    public bool CanSaveDraft => IsDraftComplete();
    public bool IsDraftComplete()
    {
        if (!CanManage || !CanEditFields || !Controls.RequiredInput.Selection(DraftPatternRevisionId) || Parse(DraftAmount) is not > 0m
            || !DraftDate.HasValue || !Controls.RequiredInput.Currency(DraftCurrency)
            || !Controls.RequiredInput.AmountText(DraftRate, 8, true)) return false;
        if (IsPartySettlement) return DraftPartyId is not null && !HasDistributionConflict && AllocationsComplete()
            && Total(AllocationRows.Select(r => r.SettlementAmount)) <= Parse(DraftAmount);
        var completeLines = LineRows.Where(l => l.IsComplete).ToList();
        return completeLines.Count > 0 && !LineRows.Any(l => l.IsIncomplete)
            && Total(completeLines.Select(r => r.Amount)) == Parse(DraftAmount);
    }

    public void EnsureDistributionBuffer(int targetCount = 50)
    {
        if (!CanEditFields || !IsDistributionPayment) return;
        var limit = Math.Min(targetCount, 200);
        while (LineRows.Count < limit)
        {
            LineRows.Add(VoucherLineItem.CreateBlankSlot(DraftChanged));
        }
    }

    public void AutoGrowDistribution(int additional = 10)
    {
        if (!CanEditFields || !IsDistributionPayment) return;
        EnsureDistributionBuffer(LineRows.Count + additional);
    }

    private async Task MoveVoucherAsync(int offset)
    {
        if (Vouchers.Count == 0) return;
        var currentId = Current?.Id;
        var currentIdx = currentId.HasValue ? Vouchers.ToList().FindIndex(v => v.Id == currentId.Value) : -1;
        int targetIdx;
        if (offset == int.MinValue) targetIdx = 0;
        else if (offset == int.MaxValue) targetIdx = Vouchers.Count - 1;
        else targetIdx = Math.Clamp(currentIdx + offset, 0, Vouchers.Count - 1);
        if (targetIdx >= 0 && targetIdx < Vouchers.Count && Vouchers[targetIdx].Id != currentId && Vouchers[targetIdx].Id is { } targetId)
        {
            await OpenAsync(targetId, CancellationToken.None).ConfigureAwait(true);
        }
    }

    private bool AllocationsComplete() => AllocationRows.Count(r => !string.IsNullOrWhiteSpace(r.Amount)) <= 200 && AllocationRows.All(r =>
        string.IsNullOrWhiteSpace(r.Amount) && string.IsNullOrWhiteSpace(r.SettlementAmount)
        || CanAllocate && r.InvoiceId != Guid.Empty && Parse(r.Amount) is > 0m && Parse(r.SettlementAmount) is > 0m
        && Rate(r.CrossRate) is > 0m && (string.IsNullOrWhiteSpace(DraftCurrency)
            || !string.Equals(DraftCurrency.Trim(), r.CurrencyCode, StringComparison.OrdinalIgnoreCase)
            || Parse(r.Amount) == Parse(r.SettlementAmount) && Rate(r.CrossRate) == 1m));

    private async Task<bool> StartNewAsync(VoucherKind kind, Guid? partyId)
    {
        if (!await DiscardAsync().ConfigureAwait(true)) return false;
        ResetEditor(); DraftKind = kind;
        var patternList = kind == VoucherKind.Receipt ? ReceiptPatterns : PaymentPatterns;
        var defaultPattern = patternList.Count > 0 ? patternList[0] : null;
        DraftPatternRevisionId = defaultPattern?.RevisionId;
        DraftPartyId = partyId;
        DraftCurrency = !string.IsNullOrWhiteSpace(defaultPattern?.DefaultCurrencyCode) ? defaultPattern.DefaultCurrencyCode : baseCurrency;
        if (!string.IsNullOrWhiteSpace(defaultPattern?.DefaultPaymentMethod) &&
            Enum.TryParse<SettlementMethod>(defaultPattern.DefaultPaymentMethod, true, out var method))
        {
            DraftSettlement = method;
        }
        IsEditing = true;
        if (kind == VoucherKind.Payment && partyId is null)
        {
            LineRows.Clear();
            EnsureDistributionBuffer(50);
        }
        WorkspaceTab = 1;
        CaptureDraftBaseline();
        RefreshEditor(); RefreshNumberPreview(); RefreshRate(); return true;
    }

    private async Task<bool> DiscardAsync() => !HasUnsavedChanges || await confirmation.ConfirmAsync("D_ConfirmDiscardChanges").ConfigureAwait(true);
    private async Task CancelAsync()
    {
        if (!await DiscardAsync().ConfigureAwait(true)) return;
        if (Current is { } saved) Show(saved); else ResetEditor();
    }
    private async Task OpenAsync(Guid voucherId, CancellationToken cancellationToken)
    {
        if (!await DiscardAsync().ConfigureAwait(true)) return;
        var detail = await client.GetAsync(CompanyId, BranchId, voucherId, cancellationToken).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested(); Show(detail);
    }

    private void Show(VoucherDetailDto detail)
    {
        showingDocument = true;
        try
        {
            ResetEditor(); Current = detail; DraftKind = detail.Kind ?? VoucherKind.Receipt; WorkspaceTab = 1;
            DraftPatternRevisionId = detail.PatternRevisionId; DraftPartyId = detail.PartyId;
            DraftSettlement = detail.SettlementMethod ?? SettlementMethod.Cash;
            DraftDate = (detail.VoucherDateUtc ?? DateTimeOffset.UtcNow).UtcDateTime.Date;
            DraftCurrency = detail.CurrencyCode ?? string.Empty; DraftRate = detail.ExchangeRate ?? string.Empty;
            DraftAmount = detail.Amount ?? string.Empty; DraftNotes = detail.Notes;
            DraftSourceNumber = detail.SourceNumber; DraftSourceDate = detail.SourceDate?.ToDateTime(TimeOnly.MinValue);
            foreach (var a in detail.InvoiceAllocations ?? []) AllocationRows.Add(FromApplication(a));
            if (AllocationRows.Count == 0)
                foreach (var a in detail.Allocations ?? []) AllocationRows.Add(new VoucherAllocationItem
                {
                    InvoiceId = a.SalesInvoiceId ?? Guid.Empty, InvoiceNumber = a.InvoiceNumber ?? string.Empty,
                    DateDisplay = a.InvoiceDateUtc?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
                    CurrencyCode = detail.CurrencyCode ?? string.Empty, Amount = a.Amount ?? string.Empty,
                    SettlementAmount = a.Amount ?? string.Empty, CrossRate = "1", Changed = DraftChanged,
                });
            foreach (var allocation in AllocationRows) OriginalAllocations.Add(new VoucherAllocationItem
            {
                InvoiceId = allocation.InvoiceId, InvoiceNumber = allocation.InvoiceNumber, DateDisplay = allocation.DateDisplay,
                CurrencyCode = allocation.CurrencyCode, Amount = allocation.Amount, SettlementAmount = allocation.SettlementAmount, CrossRate = allocation.CrossRate,
            });
            foreach (var l in detail.Lines ?? []) LineRows.Add(new VoucherLineItem { PartyId = l.PartyId, AccountId = l.AccountId,
                CostCenterId = l.CostCenterId, Amount = l.Amount ?? string.Empty, Description = l.Description, Changed = DraftChanged, IsPersisted = true });
            if (detail.Status == VoucherStatus.Draft && IsEditing && IsDistributionPayment)
            {
                EnsureDistributionBuffer(50);
            }
            foreach (var batch in detail.SettlementBatches ?? []) SettlementBatches.Add(new VoucherSettlementHistory(batch, Localization));
        }
        finally { showingDocument = false; RefreshEditor(); }
    }

    private SaveVoucherRequest BuildRequest() => new()
    {
        Kind = DraftKind, PatternRevisionId = DraftPatternRevisionId!.Value, PartyId = DraftPartyId, SettlementMethod = DraftSettlement,
        VoucherDateUtc = UtcDate(DraftDate!.Value), CurrencyCode = string.IsNullOrWhiteSpace(DraftCurrency) ? null : DraftCurrency.Trim().ToUpperInvariant(),
        ExchangeRate = string.IsNullOrWhiteSpace(DraftRate) ? null : DraftRate.Trim(), Amount = DraftAmount.Trim(), Notes = DraftNotes, ExpectedVersion = Current?.Version,
        SourceNumber = string.IsNullOrWhiteSpace(DraftSourceNumber) ? null : DraftSourceNumber.Trim(), SourceDate = DraftSourceDate is { } sourceDate ? DateOnly.FromDateTime(sourceDate) : null,
        Allocations = IsPartySettlement ? AllocationRows.Where(r => !string.IsNullOrWhiteSpace(r.Amount)).Select(r => new VoucherAllocationRequest { SalesInvoiceId = r.InvoiceId, Amount = r.Amount.Trim() }).ToList() : [],
        InvoiceAllocations = IsPartySettlement ? AllocationInputs() : [],
        Lines = IsDistributionPayment ? LineRows.Where(l => l.IsComplete).Select(l => new VoucherLineRequest { PartyId = l.PartyId, AccountId = l.AccountId,
            CostCenterId = l.CostCenterId, Amount = l.Amount.Trim(), Description = l.Description }).ToList() : [],
    };
    private List<InvoiceSettlementInputDto> AllocationInputs() => AllocationRows.Where(r => !string.IsNullOrWhiteSpace(r.Amount)).Select(r =>
        new InvoiceSettlementInputDto { InvoiceId = r.InvoiceId, InvoiceAmount = r.Amount.Trim(), SettlementAmount = r.SettlementAmount.Trim(), CrossRate = r.CrossRate.Trim() }).ToList();

    public IReadOnlyList<string> GetValidationErrors()
    {
        var errors = new List<string>();
        if (!Controls.RequiredInput.Selection(DraftPatternRevisionId))
            errors.Add(Localization["D_DocumentPatterns"] + ": " + Localization["D_RequiredField"]);
        if (Parse(DraftAmount) is not > 0m)
            errors.Add(Localization["D_Amount"] + ": " + Localization["UI_InvalidNumber"]);
        if (!DraftDate.HasValue)
            errors.Add(Localization["D_DateTime"] + ": " + Localization["D_RequiredField"]);
        if (!Controls.RequiredInput.Currency(DraftCurrency))
            errors.Add(Localization["CUR_Currency"] + ": " + Localization["UI_InvalidCurrency"]);
        if (!Controls.RequiredInput.AmountText(DraftRate, 8, true))
            errors.Add(Localization["CUR_Rate"] + ": " + Localization["UI_InvalidNumber"]);
        if (IsPartySettlement && DraftPartyId is null)
            errors.Add(Localization["D_Party"] + ": " + Localization["D_RequiredField"]);
        if (HasDistributionConflict)
            errors.Add(Localization["D_DistributionConflict"]);
        if (IsDistributionPayment)
        {
            var completeLines = LineRows.Where(l => l.IsComplete).ToList();
            if (completeLines.Count == 0)
                errors.Add(Localization["D_VoucherLines"] + ": " + Localization["D_AtLeastOneLineRequired"]);
            Controls.DocumentEntryValidation.AddIncompleteLineErrors(errors, key => Localization[key], "D_VoucherLines", LineRows);
        }
        return errors;
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        var errors = GetValidationErrors();
        if (errors.Count > 0 || !IsDraftComplete())
        {
            ValidationErrors = errors;
            ValidationMessage = errors.Count > 0 ? string.Join("\n", errors) : Localization["D_RequiredFieldsIncomplete"];
            return;
        }
        ValidationErrors = [];
        ValidationMessage = string.Empty;
        if (!await DocumentDateGuard.AllowsAsync(DraftDate, Context, Localization, confirmation, message => ValidationMessage = message).ConfigureAwait(true)) return;
        var request = BuildRequest();
        SaveOutcomeMessage = string.Empty; SaveOutcomeIsWarning = false;
        // One server call applies the company posting policy (draft / approve / approve+post); the key is stable for an unchanged
        // payload so an ambiguous retry resolves to the same voucher, and renewed as soon as the user changes anything financial.
        // Target resolution. A saved document the editor could not reload (Get/List failed after a successful save) is still the
        // same document: an unchanged payload retries the ORIGINAL key through the create endpoint (the server resolves it to the
        // stored result); a changed payload updates that document after reading its authoritative version. Never a new document.
        var targetId = Current?.Id;
        var expectedVersion = Current?.Version;
        var retryOriginalKey = false;
        if (targetId is null && uncertainDocumentId is { } uncertainId)
        {
            if (pendingSaveKey is not null && string.Equals(RequestFingerprint(request, null), pendingSaveFingerprint, StringComparison.Ordinal)) retryOriginalKey = true;
            else
            {
                var authoritative = await client.GetAsync(CompanyId, BranchId, uncertainId, cancellationToken).ConfigureAwait(true);
                targetId = uncertainId; expectedVersion = authoritative.Version;
            }
        }
        var envelope = new SaveAndProcessVoucherRequest
        {
            Document = request, SaveAsDraft = false, IdempotencyKey = retryOriginalKey ? pendingSaveKey! : SaveKeyFor(request, targetId),
            VoucherId = targetId, ExpectedVersion = expectedVersion,
            ManualNumber = string.IsNullOrWhiteSpace(ManualDocumentNumber) ? null : ManualDocumentNumber.Trim(),
        };
        var outcome = targetId is { } id ? await client.SaveAndProcessUpdateAsync(CompanyId, BranchId, id, envelope, cancellationToken).ConfigureAwait(true)
            : await client.SaveAndProcessAsync(CompanyId, BranchId, envelope, cancellationToken).ConfigureAwait(true);
        // The server accepted the request: remember the identity BEFORE any read-back. The key is released only after the
        // document has been reloaded, so a failed Get/List can be retried with the same key and never duplicates the document.
        uncertainDocumentId = outcome.DocumentId;
        cancellationToken.ThrowIfCancellationRequested();
        ApplySaveOutcome(outcome);
        if (outcome.DocumentId is { } savedId) Show(await client.GetAsync(CompanyId, BranchId, savedId, cancellationToken).ConfigureAwait(true));
        pendingSaveKey = null; pendingSaveFingerprint = null; uncertainDocumentId = null;
        await ListAsync(cancellationToken).ConfigureAwait(true);
    }

    private static string RequestFingerprint(SaveVoucherRequest request, Guid? voucherId) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { request, voucherId })));

    private string SaveKeyFor(SaveVoucherRequest request, Guid? voucherId)
    {
        var fingerprint = RequestFingerprint(request, voucherId);
        if (pendingSaveKey is null || !string.Equals(fingerprint, pendingSaveFingerprint, StringComparison.Ordinal))
        {
            pendingSaveKey = $"voucher-save:{Guid.NewGuid():N}";
            pendingSaveFingerprint = fingerprint;
        }
        return pendingSaveKey;
    }

    /// <summary>A saved-but-incomplete workflow is never shown as plain success: the saved status and the next step are stated.</summary>
    private void ApplySaveOutcome(DocumentProcessingResultDto outcome)
    {
        var incomplete = outcome.Outcome is "ApprovalPermissionMissing" or "PostingPermissionMissing" or "PostingFailed" or "ApprovalFailed";
        SaveOutcomeIsWarning = incomplete;
        SaveOutcomeMessage = !incomplete ? outcome.Message ?? string.Empty
            : $"⚠ {outcome.Outcome}{(string.IsNullOrEmpty(outcome.FailureCode) ? string.Empty : $" [{outcome.FailureCode}]")} — السند محفوظ بحالة «{outcome.Status}» {outcome.DocumentNumber}. {outcome.Message} الخطوة التالية: عالج السبب أو اطلب الصلاحية ثم أكمل الاعتماد/الترحيل يدوياً.";
    }
    private async Task DeleteAsync()
    {
        if (Current is not { Id: { } id, Version: { } version } || !await confirmation.ConfirmAsync("D_ConfirmDeleteVoucher").ConfigureAwait(true)) return;
        await RunAsync(async t => { await client.DeleteAsync(CompanyId, BranchId, id, version, t).ConfigureAwait(true); t.ThrowIfCancellationRequested();
            ResetEditor(); await ListAsync(t).ConfigureAwait(true); }).ConfigureAwait(true);
    }
    private async Task ApproveAsync(CancellationToken cancellationToken)
    {
        if (Current is not { Id: { } id, Version: { } version }) return;
        var result = await client.ApproveAsync(CompanyId, BranchId, id, new VoucherActionRequest { ExpectedVersion = version, Reason = ReverseReason.Trim() }, cancellationToken).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested(); Show(result); await ListAsync(cancellationToken).ConfigureAwait(true);
    }
    private async Task PostAsync()
    {
        if (Current is not { Id: { } id, Version: { } version } || !await confirmation.ConfirmAsync("D_ConfirmPostVoucher").ConfigureAwait(true)) return;
        if (pendingPostVersion != version) { pendingPostVersion = version; pendingPostKey = $"voucher-post:{id:N}:{Guid.NewGuid():N}"; }
        await RunDocumentAsync(async () =>
        {
            var posted = await client.PostAsync(CompanyId, BranchId, id, new PostVoucherRequest { ExpectedVersion = version, IdempotencyKey = pendingPostKey!, ManualNumber = string.IsNullOrWhiteSpace(ManualDocumentNumber) ? null : ManualDocumentNumber.Trim() }, Token).ConfigureAwait(true);
            pendingPostKey = null; pendingPostVersion = null; Show(posted); await ListAsync(Token).ConfigureAwait(true);
        }, OperationKind.Post).ConfigureAwait(true);
    }
    private async Task ReverseAsync()
    {
        if (Current is not { Id: { } id, Version: { } version } || !await confirmation.ConfirmAsync("D_ConfirmReverseVoucher").ConfigureAwait(true)) return;
        var reason = ReverseReason.Trim();
        await RunAsync(async t => { var result = await client.ReverseAsync(CompanyId, BranchId, id,
            new ReverseVoucherRequest { ExpectedVersion = version, Reason = reason }, t).ConfigureAwait(true);
            t.ThrowIfCancellationRequested(); Show(result); await ListAsync(t).ConfigureAwait(true); }).ConfigureAwait(true);
    }
    private Task OpenInvoiceAsync() => SelectedAllocation is { InvoiceId: var id } && id != Guid.Empty
        ? IsReceipt ? navigator.NavigateAsync("sales-invoice-card", new OpenSalesInvoiceCardRequest(id))
            : navigator.NavigateAsync("purchase-invoices", new Purchases.OpenPurchaseRequest(id)) : Task.CompletedTask;

    private void ResetEditor()
    {
        draftBaseline = null; uncertainDocumentId = null; pendingSaveKey = null; pendingSaveFingerprint = null; // an abandoned/new editor never inherits an old save identity
        lockedPatternRevisionId = null; Current = null; IsEditing = false; IsApplyingAdvance = false; DraftPatternRevisionId = null; DraftPartyId = null; DraftSettlement = SettlementMethod.Cash;
        DraftDate = DateTime.Today; DraftCurrency = string.Empty; DraftRate = string.Empty; DraftAmount = string.Empty; DraftNotes = null; DraftSourceNumber = null; DraftSourceDate = null; ReverseReason = string.Empty;
        AllocationRows.Clear(); OriginalAllocations.Clear(); LineRows.Clear(); SelectedLine = null; SelectedAllocation = null; SettlementBatches.Clear(); SelectedBatch = null;
        invoicePage = 1; invoiceTotal = 0; NumberPreview = null; RateInfo = string.Empty; lastAutoRate = null; RefreshEditor();
    }
    private void DraftChanged() => RefreshEditor();
    private void RefreshEditor()
    {
        foreach (var name in new[] { nameof(IsReceipt), nameof(IsPayment), nameof(IsPartySettlement), nameof(IsDistributionPayment), nameof(HasDistributionConflict),
            nameof(ShowDistributionPanel), nameof(HasAllocationHistory), nameof(IsDraft), nameof(CanEditFields), nameof(PatternPickerEnabled), nameof(HasAudit), nameof(AuditText), nameof(IsDateLocked), nameof(DateEnabled), nameof(CanEditDistribution), nameof(CanEnterFields), nameof(IsWorkspaceIdle), nameof(CanEditAllocations), nameof(IsAllocationReadOnly), nameof(IsFieldsReadOnly), nameof(Patterns), nameof(Parties),
            nameof(StatusText), nameof(Lifecycle), nameof(HasDocumentStatus), nameof(DocumentStatusKind), nameof(AllocatedDisplay), nameof(AdvanceDisplay), nameof(SelectionTotalDisplay), nameof(LinesTotalDisplay), nameof(HasUnsavedChanges), nameof(CanSaveBeforeClose),
            nameof(CanAllocate), nameof(CanApprovePermission), nameof(InvoicePageStatus), nameof(SelectedBatchApplications), nameof(ShowPartyBalance), nameof(PreviousBalanceDisplay), nameof(FinalBalanceDisplay) }) OnPropertyChanged(name);
        NotifyEditorCommands();
    }
    private void NotifyEditorCommands()
    {
        foreach (var command in new IRelayCommand?[] { SaveCommand, DeleteCommand, ApproveCommand, PostCommand, ReverseCommand, LoadOutstandingCommand, EditCommand, CancelCommand,
            AddLineCommand, RemoveLineCommand, ClearDistributionCommand, OpenJournalCommand, OpenReversalJournalCommand, OpenPartyCommand, OpenInvoiceCommand, StartAdvanceCommand, ApplyAdvanceCommand,
            ReverseSettlementCommand, PreviousInvoicesCommand, NextInvoicesCommand, OpenSettlementJournalCommand, OpenSettlementReversalJournalCommand, OpenHistoryInvoiceCommand }) command?.NotifyCanExecuteChanged();
    }
    partial void OnDraftKindChanged(VoucherKind value) { RefreshEditor(); RefreshNumberPreview(); }
    partial void OnDraftDateChanged(DateTime? value) { RefreshEditor(); RefreshNumberPreview(); RefreshRate(); }
    partial void OnDraftPartyIdChanged(Guid? value)
    {
        if (!showingDocument && IsEditing)
        {
            AllocationRows.Clear(); invoicePage = 1; invoiceTotal = 0;
            _ = LoadPartyBalanceAsync(value);
            if (Current is null && value is { } selectedParty) _ = ApplyPartyAccountCurrencyAsync(selectedParty);
            if (IsPayment && value is not null && LineRows.All(l => l.PartyId is null && l.AccountId is null && string.IsNullOrWhiteSpace(l.Amount))) LineRows.Clear();
        }
        RefreshEditor();
    }
    /// <summary>A new voucher for a party opens in the currency of the party's own receivable/payable account.</summary>
    private async Task ApplyPartyAccountCurrencyAsync(Guid partyId)
    {
        try
        {
            var party = Customers.Concat(Suppliers).FirstOrDefault(p => p.Id == partyId);
            var accountId = IsReceipt ? party?.ReceivableAccountId : party?.PayableAccountId;
            if (accountId is not { } id || id == Guid.Empty) return;
            var account = await accountsClient.GetAccountAsync(CompanyId, BranchId, id, CancellationToken.None).ConfigureAwait(true);
            if (DraftPartyId != partyId || Current is not null) return;
            if (account.FixedCurrencyCode is { Length: 3 } code && CurrencyOptions.Contains(code)) ApplySystemValue(() => DraftCurrency = code);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { /* the currency stays as the operator or pattern set it */ }
    }
    partial void OnDraftAmountChanged(string value) => RefreshEditor();
    partial void OnDraftCurrencyChanged(string value)
    {
        if (!showingDocument && IsEditing)
        {
            // The rate belongs to the currency, so the register is asked again; the previous value was auto-filled, not typed.
            lastAutoRate = null;
            if (DraftRate.Length > 0) DraftRate = string.Empty;
            foreach (var row in AllocationRows) { row.SettlementAmount = string.Empty; row.CrossRate = string.Empty; }
            invoicePage = 1; invoiceTotal = 0;
            RefreshRate();
        }
        RefreshEditor();
    }
    partial void OnDraftRateChanged(string value) => RefreshEditor();
    partial void OnDraftPatternRevisionIdChanged(Guid? value)
    {
        if (value is { } revId && Current is null)
        {
            var pattern = Patterns.FirstOrDefault(p => p.RevisionId == revId);
            if (pattern is not null)
            {
                if (!string.IsNullOrWhiteSpace(pattern.DefaultCurrencyCode))
                    DraftCurrency = pattern.DefaultCurrencyCode;
                if (!string.IsNullOrWhiteSpace(pattern.DefaultPaymentMethod) &&
                    Enum.TryParse<SettlementMethod>(pattern.DefaultPaymentMethod, true, out var method))
                {
                    DraftSettlement = method;
                }
            }
        }
        // A pattern that fixes the date carries the day the voucher is added; the picker is locked (see IsDateLocked).
        if (Current is null && IsDateLocked) DraftDate = DateTime.Today;
        RefreshEditor(); RefreshNumberPreview(); NotifyHeaders();
    }

    /// <summary>The voucher's pattern fixes the date to the day the voucher is added.</summary>
    public bool IsDateLocked => Patterns.Any(p => p.RevisionId == (Current?.PatternRevisionId ?? DraftPatternRevisionId) && p.LockDocumentDate);
    public bool DateEnabled => CanEnterFields && !IsDateLocked;

    /// <summary>Grid headers come from the voucher pattern's line-field captions (pattern management); the standard caption is the fallback.</summary>
    private string Caption(string field, string standard) =>
        Patterns.FirstOrDefault(p => p.RevisionId == (Current?.PatternRevisionId ?? DraftPatternRevisionId))?.FieldCaptions is { } captions
            && captions.TryGetValue(field, out var caption) ? caption : standard;
    public string HeaderParty => Caption("Party", Localization["D_Supplier"]);
    public string HeaderAccount => Caption("Account", Localization["D_ExpenseAccount"]);
    public string HeaderCostCenter => Caption("CostCenter", Localization["D_CostCenter"]);
    public string HeaderAmount => Caption("Amount", Localization["D_Amount"]);
    public string HeaderDescription => Caption("Description", Localization["D_Description"]);
    private void NotifyHeaders()
    {
        OnPropertyChanged(nameof(HeaderParty)); OnPropertyChanged(nameof(HeaderAccount)); OnPropertyChanged(nameof(HeaderCostCenter));
        OnPropertyChanged(nameof(HeaderAmount)); OnPropertyChanged(nameof(HeaderDescription));
    }
    partial void OnIsEditingChanged(bool value) => RefreshEditor();
    partial void OnReverseReasonChanged(string value) { NotifyEditorCommands(); DraftFieldChanged(); }
    partial void OnSelectedLineChanged(VoucherLineItem? value) => RemoveLineCommand?.NotifyCanExecuteChanged();
    partial void OnSelectedAllocationChanged(VoucherAllocationItem? value) => OpenInvoiceCommand?.NotifyCanExecuteChanged();
    partial void OnCurrentChanged(VoucherDetailDto? value) { RefreshEditor(); NotifyHeaders(); }
    private static DateTimeOffset UtcDate(DateTime date) => new(DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified), TimeSpan.Zero);
    private static decimal? Parse(string? text) => decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
        && value <= 999999999999999.9999m && decimal.Round(value, 4) == value ? value : null;
    private static decimal? Rate(string? text) => decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
        && decimal.Round(value, 8) == value ? value : null;
    private static decimal Total(IEnumerable<string> amounts) => amounts.Sum(a => Parse(a) ?? 0m);
}
