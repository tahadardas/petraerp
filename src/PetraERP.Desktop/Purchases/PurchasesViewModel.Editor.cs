using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Connection;
using PetraERP.Desktop.Controls;
using PetraERP.Desktop.Sales;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Purchases;

public sealed partial class PurchasesViewModel
{
    private string? pendingPostKey;
    private string? pendingPostVersion;
    private string? pendingReverseKey;
    private string? pendingReverseFingerprint;
    private string? pendingReturnKey;
    private string? pendingReturnFingerprint;

    [ObservableProperty] private PurchaseDetailDto? current;
    [ObservableProperty] private bool isEditing;
    [ObservableProperty] private Guid? draftPatternRevisionId;
    [ObservableProperty] private Guid? draftSupplierId;
    [ObservableProperty] private Guid? draftWarehouseId;
    [ObservableProperty] private DateTime? draftDate = DateTime.Today;
    [ObservableProperty] private PaymentMethod draftPaymentMethod = PaymentMethod.Credit;
    [ObservableProperty] private string draftCurrency = string.Empty;
    [ObservableProperty] private string draftRate = string.Empty;
    /// <summary>Where the displayed rate came from, or why it is missing; the operator is never left guessing.</summary>
    [ObservableProperty] private string rateInfo = string.Empty;
    [ObservableProperty] private string? draftSupplierReference;
    [ObservableProperty] private string? draftNotes;
    [ObservableProperty] private string draftSellerText = string.Empty;
    [ObservableProperty] private string draftDistributorText = string.Empty;

    public PartyLookupDto? SelectedSupplier => Suppliers.FirstOrDefault(s => s.Id == DraftSupplierId);
    public bool HasSelectedSupplier => SelectedSupplier is not null;
    public string SupplierCurrentBalanceDisplay => SelectedSupplier?.CurrentBalance is { Length: > 0 } bal ? bal : "0.00";
    public string SupplierCreditLimitDisplay => SelectedSupplier?.CreditLimit is { Length: > 0 } lim ? lim : "0.00";
    public string SupplierAvailableCreditDisplay => SelectedSupplier?.AvailableCredit is { Length: > 0 } av ? av : "0.00";
    public bool IsSupplierCreditBlocked => SelectedSupplier?.IsCreditBlocked == true;
    public bool HasSupplierCreditLimit => decimal.TryParse(SelectedSupplier?.CreditLimit, NumberStyles.Any, CultureInfo.InvariantCulture, out var limit) && limit > 0;

    [ObservableProperty] private bool isInvoiceInfoOpen;
    public RelayCommand ToggleInvoiceInfoCommand => toggleInvoiceInfoCommand ??= new(() => IsInvoiceInfoOpen = !IsInvoiceInfoOpen);
    private RelayCommand? toggleInvoiceInfoCommand;

    public string InvoiceAuditStatusDisplay => Lifecycle switch
    {
        DocumentLifecycle.Posted => "مرحلة ومحققة محاسبياً",
        DocumentLifecycle.Approved => "معتمدة بانتظار الترحيل",
        DocumentLifecycle.Reversed => "معكوسة / ملغاة",
        _ => "مسودة قيد التحرير"
    };

    public string InvoiceJournalNumberDisplay => Current?.JournalEntryId is { } id
        ? id.ToString()[..8].ToUpperInvariant()
        : (Lifecycle == DocumentLifecycle.Posted ? "سند قيد آلي" : "غير مولد بعد");

    public bool HasGeneratedJournal => Current?.JournalEntryId.HasValue == true;
    public int FilledLinesCount => Lines.Count(l => !l.IsPristineBlank);
    public string InvoiceDateUtcDisplay => Current?.InvoiceDateUtc?.LocalDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
        ?? (DraftDate.HasValue ? DraftDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "-");
    public string PaymentMethodDisplay => DraftPaymentMethod switch
    {
        PaymentMethod.Cash => "نقداً",
        PaymentMethod.Credit => "ذمم (آجل)",
        _ => DraftPaymentMethod.ToString()
    };
    [ObservableProperty] private Guid? draftCostCenterId;

    private static int? ParseCode(string? text) =>
        int.TryParse(text?.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : null;
    [ObservableProperty] private PurchaseLineItem? selectedLine;
    [ObservableProperty] private string returnReason = string.Empty;
    [ObservableProperty] private string reverseReason = string.Empty;
    [ObservableProperty] private Guid? returnVarianceAccountId;
    public ObservableCollection<Controls.LookupItem> ReturnVarianceAccounts { get; } = [];

    public ObservableCollection<PurchaseLineItem> Lines { get; } = [];
    public ObservableCollection<SalesReturnLineItem> ReturnLines { get; } = [];

    public Func<string, CancellationToken, Task<IReadOnlyList<Controls.LookupItem>>> SearchItemsAsync => async (query, ct) =>
    {
        if (CompanyId == Guid.Empty || BranchId == Guid.Empty) return [];
        var items = await inventoryClient.LookupItemsAsync(CompanyId, BranchId, 50, query, ct).ConfigureAwait(false);
        foreach (var item in items)
        {
            if (item.Id.HasValue && !Items.Any(i => i.Id == item.Id.Value))
            {
                App.Current?.Dispatcher.BeginInvoke(() =>
                {
                    if (!Items.Any(i => i.Id == item.Id.Value))
                    {
                        Items.Add(item);
                    }
                });
            }
        }
        return items.Where(i => i.Id.HasValue).Select(i => new Controls.LookupItem(i.Id!.Value, i.Code ?? string.Empty, i.NameArabic ?? string.Empty, i.NameEnglish)).ToList();
    };

    public Func<string, CancellationToken, Task<IReadOnlyList<Controls.LookupItem>>> SearchSuppliersAsync => async (query, ct) =>
    {
        if (CompanyId == Guid.Empty || BranchId == Guid.Empty) return [];
        var parties = await partiesClient.LookupPartiesAsync(CompanyId, BranchId, 50, query, PartyRole.Supplier, ct).ConfigureAwait(false);
        foreach (var party in parties)
        {
            if (party.Id.HasValue && !Suppliers.Any(s => s.Id == party.Id.Value))
            {
                App.Current?.Dispatcher.BeginInvoke(() =>
                {
                    if (!Suppliers.Any(s => s.Id == party.Id.Value))
                    {
                        Suppliers.Add(party);
                    }
                });
            }
        }
        return parties.Where(p => p.Id.HasValue).Select(p => new Controls.LookupItem(p.Id!.Value, p.Code ?? string.Empty, p.NameArabic ?? string.Empty, p.NameEnglish)).ToList();
    };

    public bool IsDraft => Current is null || Current.Status == PurchaseInvoiceStatus.Draft;
    public bool CanEditFields => IsEditing && IsDraft;
    private Guid? lockedPatternRevisionId;
    /// <summary>A new invoice started from a pattern button keeps that pattern; the picker stays open for existing drafts.</summary>
    public bool PatternPickerEnabled => CanEditFields && !(lockedPatternRevisionId.HasValue && Current is null);
    public bool IsFieldsReadOnly => !CanEditFields;
    public bool IsReturnDocument => Current?.ReturnOfInvoiceId is not null;

    /// <summary>The document this one reverses, or the reversal it produced; the card can jump between the pair.</summary>
    public Guid? LinkedReversalId => Current?.ReversesInvoiceId ?? Current?.ReversedByInvoiceId;

    /// <summary>Only an original posted invoice that nobody has reversed yet, with no returns against it.</summary>
    public bool CanReverseDocument => HasContext && Idle && !IsEditing && Can(PostPermission)
        && Current is { Status: PurchaseInvoiceStatus.Posted, ReversesInvoiceId: null, ReturnOfInvoiceId: null, ReversedByInvoiceId: null };
    public bool IsReturnable => Current is { Status: PurchaseInvoiceStatus.Posted, ReturnOfInvoiceId: null, ReversesInvoiceId: null }
        && ReturnLines.Any(l => l.Returnable > 0m);
    public string StatusText => Current is null ? Localization["D_PurchaseNew"]
        : IsReturnDocument ? Localization["D_PurchaseReturnDocument"]
        : Current.Status == PurchaseInvoiceStatus.Posted ? Localization["D_PurchasePosted"]
        : Current.Status == PurchaseInvoiceStatus.Reversed ? Localization["D_PurchaseReversed"]
        : Current?.Status == PurchaseInvoiceStatus.Approved ? "معتمدة"
        : Localization["D_PurchaseDraft"];

    private string subTotalDisplay = "0.0000";
    private string discountTotalDisplay = "0.0000";
    private string taxTotalDisplay = "0.0000";
    private string additionsTotalDisplay = "0.0000";
    private string grandTotalDisplay = "0.0000";
    private string totalQuantityDisplay = "0.0000";

    public string SubTotalDisplay { get => subTotalDisplay; private set => SetProperty(ref subTotalDisplay, value); }
    public string DiscountTotalDisplay { get => discountTotalDisplay; private set => SetProperty(ref discountTotalDisplay, value); }
    public string TaxTotalDisplay { get => taxTotalDisplay; private set => SetProperty(ref taxTotalDisplay, value); }
    public string AdditionsTotalDisplay { get => additionsTotalDisplay; private set => SetProperty(ref additionsTotalDisplay, value); }
    public string GrandTotalDisplay { get => grandTotalDisplay; private set => SetProperty(ref grandTotalDisplay, value); }
    public string TotalQuantityDisplay { get => totalQuantityDisplay; private set => SetProperty(ref totalQuantityDisplay, value); }

    /// <summary>The settlement columns only exist once the server has posted the invoice; a draft shows the entered preview alone.</summary>
    public bool HasSettlement => Current?.OutstandingAmount is not null;
    public string SettledDisplay => Current?.SettledAmount ?? string.Empty;
    public string ReturnedDisplay => Current?.ReturnedAmount ?? string.Empty;
    public string OutstandingDisplay => Current?.OutstandingAmount ?? string.Empty;
    public string DisplayCurrency => Controls.RequiredInput.Currency(DraftCurrency) ? DraftCurrency.Trim().ToUpperInvariant() : Current?.CurrencyCode ?? string.Empty;

    /// <summary>What the operator typed while editing; a saved document always shows the totals the server computed for it.</summary>
    private void RecomputeTotals()
    {
        if (Current is not null && !IsEditing)
        {
            SubTotalDisplay = Current.SubTotal ?? "0.0000";
            DiscountTotalDisplay = Current.DiscountTotal ?? "0.0000";
            TaxTotalDisplay = Current.TaxTotal ?? "0.0000";
            AdditionsTotalDisplay = Current.AdditionsTotal ?? "0.0000";
            GrandTotalDisplay = Current.GrandTotal ?? "0.0000";
            TotalQuantityDisplay = Lines.Where(l => l.IsComplete)
                .Sum(l => PurchaseLineItem.Parse(l.Quantity) ?? 0m).ToString("F4", CultureInfo.InvariantCulture);
            return;
        }

        decimal sub = 0m, discount = 0m, tax = 0m, quantity = 0m;
        foreach (var line in Lines.Where(l => !l.IsPristineBlank))
        {
            var qty = PurchaseLineItem.Parse(line.Quantity) ?? 0m;
            var cost = PurchaseLineItem.Parse(line.UnitCost) ?? 0m;
            quantity += qty;
            sub += Math.Round(qty * cost, 4, MidpointRounding.AwayFromZero);
            discount += PurchaseLineItem.Parse(line.DiscountAmount) ?? 0m;
            tax += PurchaseLineItem.Parse(line.TaxAmount) ?? 0m;
        }

        // A document-level row without a counter account joins the invoice's discount/additions total, exactly as the server stores it.
        var documentDiscounts = Adjustments.Where(a => a is { IsComplete: true, CountsInTotals: true, Kind: SalesInvoiceAdjustmentKind.Discount }).Sum(a => a.AmountValue);
        var documentAdditions = Adjustments.Where(a => a is { IsComplete: true, CountsInTotals: true, Kind: SalesInvoiceAdjustmentKind.Addition }).Sum(a => a.AmountValue);
        discount += documentDiscounts;

        SubTotalDisplay = sub.ToString("F4", CultureInfo.InvariantCulture);
        DiscountTotalDisplay = discount.ToString("F4", CultureInfo.InvariantCulture);
        TaxTotalDisplay = tax.ToString("F4", CultureInfo.InvariantCulture);
        AdditionsTotalDisplay = documentAdditions.ToString("F4", CultureInfo.InvariantCulture);
        GrandTotalDisplay = (sub - discount + tax + documentAdditions).ToString("F4", CultureInfo.InvariantCulture);
        TotalQuantityDisplay = quantity.ToString("F4", CultureInfo.InvariantCulture);
    }

    public AsyncRelayCommand SaveCommand { get; private set; } = null!;
    public AsyncRelayCommand SaveAsDraftCommand { get; private set; } = null!;
    public AsyncRelayCommand DeleteCommand { get; private set; } = null!;
    public AsyncRelayCommand ApproveCommand { get; private set; } = null!;
    public AsyncRelayCommand PostCommand { get; private set; } = null!;
    public AsyncRelayCommand PostReturnCommand { get; private set; } = null!;
    public AsyncRelayCommand ReverseCommand { get; private set; } = null!;
    public AsyncRelayCommand OpenLinkedReversalCommand { get; private set; } = null!;
    public RelayCommand EditCommand { get; private set; } = null!;
    public RelayCommand AddLineCommand { get; private set; } = null!;
    public RelayCommand RemoveLineCommand { get; private set; } = null!;
    public AsyncRelayCommand OpenJournalCommand { get; private set; } = null!;
    public AsyncRelayCommand OpenSupplierCommand { get; private set; } = null!;
    public AsyncRelayCommand OpenSupplierStatementCommand { get; private set; } = null!;
    public AsyncRelayCommand PaySupplierCommand { get; private set; } = null!;
    public AsyncRelayCommand OpenOriginalCommand { get; private set; } = null!;
    public AsyncRelayCommand FirstCommand { get; private set; } = null!;
    public AsyncRelayCommand PreviousCommand { get; private set; } = null!;
    public AsyncRelayCommand NextCommand { get; private set; } = null!;
    public AsyncRelayCommand LastCommand { get; private set; } = null!;
    public RelayCommand AutoGrowCommand { get; private set; } = null!;
    private string validationMessage = string.Empty;
    private IReadOnlyList<string> validationErrors = [];
    public string ValidationMessage { get => validationMessage; set { if (SetProperty(ref validationMessage, value)) OnPropertyChanged(nameof(HasValidationErrors)); } }
    public IReadOnlyList<string> ValidationErrors { get => validationErrors; set { if (SetProperty(ref validationErrors, value)) OnPropertyChanged(nameof(HasValidationErrors)); } }
    public bool HasValidationErrors => ValidationErrors.Count > 0 || !string.IsNullOrWhiteSpace(ValidationMessage);

    private string actionMessage = string.Empty;
    public string ActionMessage
    {
        get => actionMessage;
        private set { if (SetProperty(ref actionMessage, value)) OnPropertyChanged(nameof(HasActionMessage)); }
    }
    public bool HasActionMessage => !string.IsNullOrWhiteSpace(ActionMessage);

    /// <summary>Stable until a save completes, so an uncertain retry replays the server posting instead of posting twice.</summary>
    private string? pendingSaveKey;
    private string? pendingSaveFingerprint;

    /// <summary>Stable for an unchanged payload so an ambiguous retry resolves to the same saved document; renewed when the user edits.</summary>
    private string SaveKeyFor(object payload, bool saveAsDraft)
    {
        var fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { payload, saveAsDraft })));
        if (pendingSaveKey is null || !string.Equals(fingerprint, pendingSaveFingerprint, StringComparison.Ordinal))
        {
            pendingSaveKey = $"purchase-save:{Guid.NewGuid():N}";
            pendingSaveFingerprint = fingerprint;
        }
        return pendingSaveKey;
    }

    private static string FormatOutcome(PetraERP.ApiClient.DocumentProcessingResultDto outcome)
    {
        var incomplete = outcome.Outcome is "ApprovalPermissionMissing" or "PostingPermissionMissing" or "PostingFailed" or "ApprovalFailed";
        return !incomplete ? outcome.Message ?? string.Empty
            : $"⚠ {outcome.Outcome}{(string.IsNullOrEmpty(outcome.FailureCode) ? string.Empty : $" [{outcome.FailureCode}]")} — المستند محفوظ بحالة «{outcome.Status}» {outcome.DocumentNumber}. {outcome.Message} الخطوة التالية: عالج السبب أو اطلب الصلاحية ثم أكمل الاعتماد/الترحيل يدوياً.";
    }

    private void InitializeEditor()
    {
        SaveCommand = new(() => RunAsync(ct => PersistAsync(saveAsDraft: false, ct), OperationKind.Save), () => HasContext && Idle && CanEditFields);
        SaveAsDraftCommand = new(() => RunAsync(ct => PersistAsync(saveAsDraft: true, ct), OperationKind.Save), () => HasContext && Idle && CanEditFields);
        DeleteCommand = new(DeleteAsync, () => HasContext && Idle && CanManage && Current is { Status: PurchaseInvoiceStatus.Draft } && !IsEditing);
        ApproveCommand = new(ApproveAsync, () => HasContext && Idle && CanApprove && Current is { Status: PurchaseInvoiceStatus.Draft } && !IsEditing);
        PostCommand = new(PostAsync, () => HasContext && Idle && Can(PostPermission) && (Current is { Status: PurchaseInvoiceStatus.Draft } || (int?)Current?.Status == 1) && !IsEditing);
        PostReturnCommand = new(PostReturnAsync, () => HasContext && Idle && CanReturn());
        // Reversal is a posting act on the purchase ledger, so it asks for the same grant that posted the original.
        ReverseCommand = new(ReverseAsync, () => HasContext && Idle && !IsEditing && Can(PostPermission)
            && Current is { Status: PurchaseInvoiceStatus.Posted, ReversesInvoiceId: null, ReturnOfInvoiceId: null, ReversedByInvoiceId: null });
        OpenLinkedReversalCommand = new(() => LinkedReversalId is { } linked ? RunAsync(t => OpenAsync(linked, t)) : Task.CompletedTask,
            () => HasContext && Idle && LinkedReversalId is not null);
        EditCommand = new(() => {
            if (Current is not null && Current.Status != PurchaseInvoiceStatus.Draft)
            {
                ValidationMessage = Localization["D_DocumentPostedLocked"];
                return;
            }
            IsEditing = true; EnsureEntryBuffer(50); Refresh();
            CaptureDraftBaseline();
            OnPropertyChanged(nameof(HasUnsavedChanges));
        }, () => HasContext && Idle && CanManage && (Current is null || Current.Status == PurchaseInvoiceStatus.Draft) && !IsEditing);
        AddLineCommand = new(() =>
        {
            var target = Lines.FirstOrDefault(l => l.IsPristineBlank);
            if (target is null)
            {
                target = PurchaseLineItem.CreateBlankSlot(Refresh);
                Lines.Add(target);
            }
            SelectedLine = target;
            Refresh();
        }, () => CanEditFields);
        AutoGrowCommand = new(() => AutoGrowLines(10), () => CanEditFields);
        FirstCommand = new(() => MoveAsync(int.MinValue), CanMove);
        PreviousCommand = new(() => MoveAsync(-1), CanMove);
        NextCommand = new(() => MoveAsync(1), CanMove);
        LastCommand = new(() => MoveAsync(int.MaxValue), CanMove);
        RemoveLineCommand = new(() =>
        {
            if (SelectedLine is not null) Lines.Remove(SelectedLine);
            SelectedLine = Lines.LastOrDefault(); Refresh();
        }, () => CanEditFields && SelectedLine is not null && Lines.Contains(SelectedLine));
        OpenJournalCommand = new(() => Current?.JournalEntryId is { } id ? navigator.NavigateAsync("journals", new OpenJournalRequest(id)) : Task.CompletedTask,
            () => HasContext && Idle && Current?.JournalEntryId is not null && Can("Ledger.Journals.View"));
        OpenSupplierCommand = new(() => Current?.SupplierId is { } id ? navigator.NavigateAsync("party-card", new OpenPartyCardRequest(id)) : Task.CompletedTask,
            () => HasContext && Idle && Current?.SupplierId is not null && Can("Parties.Directory.View"));
        // A posted credit purchase is settled by an ordinary payment voucher: the treasury rules, not this page, decide the account and the allocation.
        PaySupplierCommand = new(() => Current is { } paid ? navigator.NavigateAsync("treasury-vouchers",
                new Treasury.NewVoucherRequest(VoucherKind.Payment, paid.SupplierId, paid.Id)) : Task.CompletedTask,
            CanPaySupplier);
        OpenOriginalCommand = new(() => Current?.ReturnOfInvoiceId is { } id ? RunAsync(t => OpenAsync(id, t)) : Task.CompletedTask,
            () => HasContext && Idle && Current?.ReturnOfInvoiceId is not null);
        OpenSupplierStatementCommand = new(() => Current?.SupplierId is { } supplierId
                ? navigator.NavigateAsync("supplier-statement", new Reports.OpenPartyStatementRequest(supplierId, PartyStatementKind.Supplier)) : Task.CompletedTask,
            () => HasContext && Idle && Current?.SupplierId is not null && Can("Ledger.Reports.View"));
    }

    /// <summary>Client-side completeness only (no idle check: it is re-evaluated inside the running operation).</summary>
    public bool CanSaveDraft => IsDraftComplete();
    public bool IsDraftComplete() => CanManage && CanEditFields && Controls.RequiredInput.Selection(DraftPatternRevisionId) && Controls.RequiredInput.Selection(DraftSupplierId)
        && Controls.RequiredInput.Selection(DraftWarehouseId) && DraftDate.HasValue && Controls.RequiredInput.Currency(DraftCurrency)
        && Controls.RequiredInput.AmountText(DraftRate, 8, true)
        && Lines.Any(l => l.IsComplete)
        && !Lines.Any(l => l.IsIncomplete);

    public void EnsureEntryBuffer(int targetCount = 50)
    {
        if (!CanEditFields) return;
        while (Lines.Count < targetCount)
        {
            var blank = PurchaseLineItem.CreateBlankSlot(Refresh);
            Lines.Add(blank);
        }
    }

    public void AutoGrowLines(int additional = 10)
    {
        if (!CanEditFields) return;
        EnsureEntryBuffer(Lines.Count + additional);
    }

    private bool CanPaySupplier() => HasContext && Idle && Can(Treasury.VouchersViewModel.ViewPermission) && Can(Treasury.VouchersViewModel.ManagePermission)
        && Current is { Status: PurchaseInvoiceStatus.Posted, PaymentMethod: PaymentMethod.Credit, ReturnOfInvoiceId: null };

    private bool CanMove() => HasContext && Idle && Current is not null && !IsEditing;

    private async Task MoveAsync(int offset)
    {
        if (!CanMove()) return;
        if (Invoices.Count == 0) await ListAsync(CancellationToken.None).ConfigureAwait(true);
        if (Invoices.Count == 0) return;
        var currentId = Current?.Id;
        var currentIdx = currentId.HasValue ? Invoices.ToList().FindIndex(i => i.Id == currentId.Value) : -1;
        int targetIdx;
        if (offset == int.MinValue) targetIdx = 0;
        else if (offset == int.MaxValue) targetIdx = Invoices.Count - 1;
        else targetIdx = Math.Clamp(currentIdx + offset, 0, Invoices.Count - 1);
        if (targetIdx >= 0 && targetIdx < Invoices.Count && Invoices[targetIdx].Id != currentId && Invoices[targetIdx].Id is { } targetId)
        {
            await OpenAsync(targetId, CancellationToken.None).ConfigureAwait(true);
        }
    }

    private bool CanReturn()
    {
        if (!Can(ReturnPermission) || !IsReturnable || string.IsNullOrWhiteSpace(ReturnReason)) return false;
        var quantities = ReturnLines.Select(l => l.ParsedQuantity).ToList();
        return quantities.Any(q => q > 0m) && !quantities.Any(q => q < 0m);
    }

    private void StartNew(Guid? supplierId)
    {
        WorkspaceTab = 1;
        ResetEditor();
        DraftPatternRevisionId = PurchasePatterns.FirstOrDefault()?.RevisionId;
        DraftSupplierId = supplierId;
        // The pattern default (applied above) wins; otherwise the warehouse is filled only when there is exactly one to choose.
        DraftWarehouseId ??= Warehouses.Count == 1 ? Warehouses[0].Id : null;
        DraftCurrency = baseCurrency;
        IsEditing = true;
        Lines.Clear();
        EnsureEntryBuffer(EntryOptionsHub.Current.InvoiceRows);
        DraftPaymentMethod = EntryOptionsHub.Current.PurchasesStartOnCredit ? PaymentMethod.Credit : PaymentMethod.Cash;
        Refresh();
        RefreshNumberPreview();
        RefreshRate();
        CaptureDraftBaseline();
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    private async Task OpenAsync(Guid invoiceId, CancellationToken cancellationToken) =>
        Show(await client.GetAsync(CompanyId, BranchId, invoiceId, cancellationToken).ConfigureAwait(true));

    private void Show(PurchaseDetailDto detail)
    {
        WorkspaceTab = 1;
        ResetEditor();
        Current = detail;
        ReturnVarianceAccountId = detail.VarianceAccountId;
        DraftPatternRevisionId = detail.PatternRevisionId; DraftSupplierId = detail.SupplierId; DraftWarehouseId = detail.WarehouseId;
        DraftDate = (detail.InvoiceDateUtc ?? DateTimeOffset.UtcNow).UtcDateTime.Date; DraftPaymentMethod = detail.PaymentMethod ?? PaymentMethod.Credit;
        DraftCurrency = detail.CurrencyCode ?? string.Empty; DraftRate = detail.ExchangeRate ?? string.Empty;
        DraftSupplierReference = detail.SupplierReference; DraftNotes = detail.Notes;
        DraftSellerText = detail.SellerCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        DraftDistributorText = detail.DistributorCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        DraftCostCenterId = detail.Lines?.FirstOrDefault(l => l.CostCenterId.HasValue)?.CostCenterId;
        foreach (var l in detail.Lines ?? [])
        {
            Lines.Add(new PurchaseLineItem
            {
                ItemId = l.ItemId, UnitId = l.UnitId, Quantity = l.Quantity ?? "0", UnitCost = l.UnitCost ?? "0", DiscountAmount = l.DiscountAmount ?? "0",
                TaxAmount = l.TaxAmount ?? "0", CostCenterId = l.CostCenterId, Notes = l.Notes, Changed = Refresh,
                IsPersisted = true,
            });
            if (detail is { Status: PurchaseInvoiceStatus.Posted, ReturnOfInvoiceId: null } && l.Id is { } id)
                ReturnLines.Add(new SalesReturnLineItem
                {
                    SourceLineId = id, ItemCode = l.ItemCode ?? string.Empty, ItemName = l.ItemName ?? string.Empty,
                    Sold = PurchaseLineItem.Parse(l.Quantity) ?? 0m, Returned = PurchaseLineItem.Parse(l.ReturnedQuantity) ?? 0m,
                    Changed = () => { PostReturnCommand.NotifyCanExecuteChanged(); DraftFieldChanged(); },
                });
        }
        PopulateAdjustments(detail);
        if (detail.Status == PurchaseInvoiceStatus.Draft && IsEditing)
        {
            EnsureEntryBuffer(50);
        }
        Refresh();
    }

    private SavePurchaseInvoiceRequest BuildRequest() => new()
    {
        PatternRevisionId = DraftPatternRevisionId!.Value,
        SupplierId = DraftSupplierId!.Value,
        WarehouseId = DraftWarehouseId!.Value,
        InvoiceDateUtc = new DateTimeOffset(DateTime.SpecifyKind(DraftDate!.Value.Date, DateTimeKind.Unspecified), TimeSpan.Zero),
        PaymentMethod = DraftPaymentMethod,
        CurrencyCode = string.IsNullOrWhiteSpace(DraftCurrency) ? null : DraftCurrency.Trim().ToUpperInvariant(),
        ExchangeRate = string.IsNullOrWhiteSpace(DraftRate) ? null : DraftRate.Trim(),
        SupplierReference = DraftSupplierReference,
        Notes = DraftNotes,
        SellerCode = ParseCode(DraftSellerText),
        DistributorCode = ParseCode(DraftDistributorText),
        ExpectedVersion = Current?.Version,
        Adjustments = BuildAdjustmentRequests(),
        Lines = Lines.Where(l => l.IsComplete).Select(l => new PurchaseLineRequest
        {
            ItemId = l.ItemId!.Value, UnitId = l.UnitId!.Value, Quantity = l.Quantity.Trim(), UnitCost = l.UnitCost.Trim(),
            DiscountAmount = l.DiscountAmount.Trim(), TaxAmount = l.TaxAmount.Trim(), CostCenterId = l.CostCenterId ?? DraftCostCenterId, Notes = l.Notes,
        }).ToList(),
    };

    public IReadOnlyList<string> GetValidationErrors()
    {
        var errors = new List<string>();
        if (!Controls.RequiredInput.Selection(DraftPatternRevisionId))
            errors.Add(Localization["D_PurchasePattern"] + ": " + Localization["D_RequiredField"]);
        if (!Controls.RequiredInput.Selection(DraftSupplierId))
            errors.Add(Localization["D_Supplier"] + ": " + Localization["D_RequiredField"]);
        if (!Controls.RequiredInput.Selection(DraftWarehouseId))
            errors.Add(Localization["D_Warehouse"] + ": " + Localization["D_RequiredField"]);
        if (!DraftDate.HasValue)
            errors.Add(Localization["D_DateTime"] + ": " + Localization["D_RequiredField"]);
        if (!Controls.RequiredInput.Currency(DraftCurrency))
            errors.Add(Localization["CUR_Currency"] + ": " + Localization["UI_InvalidCurrency"]);
        if (!Controls.RequiredInput.AmountText(DraftRate, 8, true))
            errors.Add(Localization["CUR_Rate"] + ": " + Localization["UI_InvalidNumber"]);
        if (!Lines.Any(l => l.IsComplete))
            errors.Add(Localization["D_PurchaseLines"] + ": " + Localization["D_AtLeastOneLineRequired"]);
        Controls.DocumentEntryValidation.AddIncompleteLineErrors(errors, key => Localization[key], "D_PurchaseLines", Lines);
        if (HasIncompleteAdjustment) errors.Add(Localization["D_AdjustmentIncomplete"]);
        return errors;
    }

    private async Task PersistAsync(bool saveAsDraft, CancellationToken cancellationToken)
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
        if (!await EntrySaveGuard.AllowsAsync(Lines.Where(l => l.ItemId is not null).Select(l => l.ItemId), confirmation).ConfigureAwait(true)) return;
        var stockLines = Lines.Where(l => l is { ItemId: not null, UnitId: not null } && PurchaseLineItem.Parse(l.Quantity) is > 0m)
            .Select(l => (l.ItemId!.Value, l.UnitId!.Value, PurchaseLineItem.Parse(l.Quantity)!.Value)).ToList();
        if (EntryOptionsHub.Current.MaxStockWarning && stockLines.Count > 0 && Can("Inventory.Items.View")
            && await Inventory.MaxStockGuard.AnyExceedsAsync(inventoryClient, CompanyId, BranchId, stockLines, cancellationToken).ConfigureAwait(true)
            && !await confirmation.ConfirmAsync("D_ConfirmMaxStock").ConfigureAwait(true)) return;
        ActionMessage = string.Empty;
        var request = BuildRequest();
        var key = SaveKeyFor(request, saveAsDraft);
        var manualNumber = string.IsNullOrWhiteSpace(ManualDocumentNumber) ? null : ManualDocumentNumber.Trim();
        // A rejected save keeps the user's draft; the server message explains the rule (conversion, stale version, …).
        var outcome = Current?.Id is { } id
            ? await client.SaveAndProcessUpdateAsync(CompanyId, BranchId, id, new SaveAndProcessPurchaseInvoiceRequest
            {
                Document = request, InvoiceId = id, ExpectedVersion = Current?.Version, SaveAsDraft = saveAsDraft, IdempotencyKey = key, ManualNumber = manualNumber,
            }, cancellationToken).ConfigureAwait(true)
            : await client.SaveAndProcessAsync(CompanyId, BranchId, new SaveAndProcessPurchaseInvoiceRequest
            {
                Document = request, SaveAsDraft = saveAsDraft, IdempotencyKey = key, ManualNumber = manualNumber,
            }, cancellationToken).ConfigureAwait(true);
        pendingSaveKey = null;
        ActionMessage = FormatOutcome(outcome);
        Show(await client.GetAsync(CompanyId, BranchId, outcome.DocumentId!.Value, cancellationToken).ConfigureAwait(true));
        await ListAsync(cancellationToken).ConfigureAwait(true);
    }

    private async Task DeleteAsync()
    {
        if (Current is not { Id: { } id, Version: { } version } || !await confirmation.ConfirmAsync("D_ConfirmDeletePurchase").ConfigureAwait(true)) return;
        await RunAsync(async t =>
        {
            await client.DeleteAsync(CompanyId, BranchId, id, version, t).ConfigureAwait(true);
            ResetEditor();
            await ListAsync(t).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    private async Task ApproveAsync()
    {
        if (Current is not { Id: { } id, Version: { } version } || !await confirmation.ConfirmAsync("D_ConfirmApproveInvoice").ConfigureAwait(true)) return;
        await RunDocumentAsync(async () =>
        {
            var approved = await client.ApproveAsync(CompanyId, BranchId, id, version, Token).ConfigureAwait(true);
            Show(approved);
            await ListAsync(Token).ConfigureAwait(true);
        }, OperationKind.Save).ConfigureAwait(true);
    }

    private async Task PostAsync()
    {
        if (Current is not { Id: { } id, Version: { } version } || !await confirmation.ConfirmAsync("D_ConfirmPostPurchase").ConfigureAwait(true)) return;
        if (pendingPostVersion != version) { pendingPostVersion = version; pendingPostKey = $"purchase-post:{id:N}:{Guid.NewGuid():N}"; }
        // A lost response stays on this page (no shell reset) so the retry reuses the same key and the server replays.
        await RunDocumentAsync(async () =>
        {
            Show(await client.PostAsync(CompanyId, BranchId, id, new PostPurchaseInvoiceRequest { ExpectedVersion = version, IdempotencyKey = pendingPostKey!, ManualNumber = string.IsNullOrWhiteSpace(ManualDocumentNumber) ? null : ManualDocumentNumber.Trim() }, Token)
                .ConfigureAwait(true));
            pendingPostKey = null; pendingPostVersion = null;
            await ListAsync(Token).ConfigureAwait(true);
        }, OperationKind.Post).ConfigureAwait(true);
    }

    private async Task ReverseAsync()
    {
        if (Current is not { Id: { } id, Version: { } version }) return;
        var reason = ReverseReason.Trim();
        if (reason.Length == 0)
        {
            ValidationMessage = Localization["D_ReverseReasonRequired"];
            return;
        }
        if (!await confirmation.ConfirmAsync("D_ConfirmReversePurchase").ConfigureAwait(true)) return;
        // The key is bound to this document and reason, so a lost response retried from the same card replays the reversal.
        var fingerprint = $"{id:N}|{reason}";
        if (pendingReverseFingerprint != fingerprint)
        {
            pendingReverseFingerprint = fingerprint;
            pendingReverseKey = $"purchase-reverse:{id:N}:{Guid.NewGuid():N}";
        }
        await RunDocumentAsync(async () =>
        {
            Show(await client.ReverseAsync(CompanyId, BranchId, id,
                new ReversePurchaseInvoiceRequest { ExpectedVersion = version, IdempotencyKey = pendingReverseKey!, Reason = reason }, Token).ConfigureAwait(true));
            pendingReverseKey = null; pendingReverseFingerprint = null; ReverseReason = string.Empty;
            ValidationMessage = string.Empty;
            await ListAsync(Token).ConfigureAwait(true);
        }, OperationKind.Post).ConfigureAwait(true);
    }

    private async Task PostReturnAsync()
    {
        if (!CanReturn() || Current?.Id is not { } originalId || !await confirmation.ConfirmAsync("D_ConfirmPurchaseReturn").ConfigureAwait(true)) return;
        var lines = ReturnLines.Where(l => l.ParsedQuantity > 0m).Select(l => new PurchaseReturnLineRequest
        { SourceLineId = l.SourceLineId, Quantity = l.ParsedQuantity!.Value.ToString("F4", CultureInfo.InvariantCulture) }).ToList();
        var reason = ReturnReason.Trim();
        var fingerprint = string.Join('|', lines.Select(l => $"{l.SourceLineId:N}:{l.Quantity}")) + "|" + reason + "|" + ReturnVarianceAccountId;
        if (pendingReturnFingerprint != fingerprint) { pendingReturnFingerprint = fingerprint; pendingReturnKey = $"purchase-return:{originalId:N}:{Guid.NewGuid():N}"; }
        try
        {
            var created = await client.ReturnAsync(CompanyId, BranchId, originalId,
                new CreatePurchaseReturnRequest { IdempotencyKey = pendingReturnKey!, Reason = reason, Lines = lines, VarianceAccountId = ReturnVarianceAccountId }, Token).ConfigureAwait(true);
            pendingReturnKey = null; pendingReturnFingerprint = null;
            Show(created);
            await ListAsync(Token).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Operation.SetError(DesktopErrors.Key(exception));
        }
    }

    private void ResetEditor()
    {
        lockedPatternRevisionId = null; draftBaseline = null; Current = null; IsEditing = false; DraftPatternRevisionId = null; DraftSupplierId = null; DraftWarehouseId = null; DraftDate = DateTime.Today;
        DraftPaymentMethod = PaymentMethod.Credit; DraftCurrency = string.Empty; DraftRate = string.Empty; DraftSupplierReference = null; DraftNotes = null; DraftSellerText = string.Empty; DraftDistributorText = string.Empty; DraftCostCenterId = null;
        Lines.Clear(); Adjustments.Clear(); OnPropertyChanged(nameof(HasAdjustments)); ReturnLines.Clear(); SelectedLine = null; ReturnReason = string.Empty; ReturnVarianceAccountId = null;
        NumberPreview = null; RateInfo = string.Empty; lastAutoRate = null;
        Refresh();
    }

    private Controls.LookupItem? UnitOption(Guid? unitId) =>
        unitId is { } id && Units.FirstOrDefault(u => u.Id == id) is { } unit
            ? new(id, unit.Code ?? string.Empty, unit.NameArabic ?? string.Empty, unit.NameEnglish) : null;

    /// <summary>
    /// Keeps every line's unit tied to its item: a chosen item brings its base unit, the picker offers only that item's units, a
    /// replaced item drops the previous item's unit, and a cleared item gives the filled unit back so the row is blank again.
    /// The server is asked once per item (cached), only while the document is being edited.
    /// </summary>
    private void SyncLineUnits()
    {
        foreach (var line in Lines.ToArray())
        {
            if (line.ItemId is not { } itemId)
            {
                if (line.UnitsResolvedFor is null) continue;
                line.MarkUnitsFor(null);
                if (IsEditing && !line.IsPersisted && line.UnitId is not null) line.UnitId = null;
                continue;
            }

            if (line.UnitsResolvedFor != itemId)
            {
                var replaced = line.UnitsResolvedFor is not null;
                line.MarkUnitsFor(itemId);
                if (IsEditing && replaced && line.UnitId is not null) line.UnitId = null;
                if (IsEditing && line.UnitId is null && Items.FirstOrDefault(i => i.Id == itemId)?.BaseUnitId is { } baseUnit) line.UnitId = baseUnit;
                line.ShowUnit(UnitOption(line.UnitId));
            }

            if (IsEditing && !line.UnitsKnown && line.UnitsAskedFor != itemId)
            {
                line.UnitsAskedFor = itemId;
                _ = ResolveUnitsAsync(line, itemId);
            }
        }
    }

    private async Task ResolveUnitsAsync(PurchaseLineItem line, Guid itemId)
    {
        var context = generation;
        var known = Items.FirstOrDefault(i => i.Id == itemId)?.BaseUnitId;
        var choices = await unitCatalog.ForAsync(CompanyId, BranchId, itemId, known, Units, CancellationToken.None).ConfigureAwait(true);
        if (context != generation || line.ItemId != itemId || !Lines.Contains(line)) return;
        line.ApplyUnits(choices, choices.UnitIds.Select(id => UnitOption(id)).OfType<Controls.LookupItem>().ToList());
        line.ShowUnit(UnitOption(line.UnitId));
        Refresh();
    }

    private void Refresh()
    {
        SyncLineUnits();
        RebaseAdjustments();
        RecomputeTotals();
        foreach (var name in new[] { nameof(IsDraft), nameof(CanEditFields), nameof(PatternPickerEnabled), nameof(IsFieldsReadOnly), nameof(IsReturnDocument), nameof(IsReturnable),
            nameof(StatusText), nameof(Lifecycle), nameof(HasDocumentStatus), nameof(DocumentStatusKind), nameof(CanReverseDocument), nameof(LinkedReversalId), nameof(SubTotalDisplay), nameof(DiscountTotalDisplay), nameof(TaxTotalDisplay), nameof(AdditionsTotalDisplay),
            nameof(TotalQuantityDisplay), nameof(HasSettlement), nameof(SettledDisplay), nameof(ReturnedDisplay), nameof(OutstandingDisplay), nameof(DisplayCurrency),
            nameof(ShowDueDate), nameof(DueDateDisplay), nameof(HasUnsavedChanges) })
            OnPropertyChanged(name);
        NotifyEditorCommands();
    }

    private void NotifyEditorCommands()
    {
        foreach (var command in new IRelayCommand?[] { SaveCommand, DeleteCommand, ApproveCommand, PostCommand, PostReturnCommand, ReverseCommand, OpenLinkedReversalCommand,
            EditCommand, AddLineCommand,
            RemoveLineCommand, OpenJournalCommand, OpenSupplierCommand, OpenSupplierStatementCommand, PaySupplierCommand, OpenOriginalCommand, OpenItemCommand, OpenWarehouseCommand })
            command?.NotifyCanExecuteChanged();
    }

    partial void OnCurrentChanged(PurchaseDetailDto? value) => Refresh();
    partial void OnIsEditingChanged(bool value) => Refresh();
    partial void OnDraftPatternRevisionIdChanged(Guid? value)
    {
        Refresh();
        ApplyPatternDefaults(value);
        OnPropertyChanged(nameof(FieldCaptions));
        RefreshNumberPreview();
    }

    private void ApplyPatternDefaults(Guid? revisionId)
    {
        if (revisionId is null || PurchasePatterns.FirstOrDefault(p => p.RevisionId == revisionId) is not { } pattern) return;

        if (Current is null)
        {
            if (pattern.DefaultWarehouseId.HasValue && DraftWarehouseId is null)
                DraftWarehouseId = pattern.DefaultWarehouseId;
            if (!string.IsNullOrWhiteSpace(pattern.DefaultCurrencyCode) && string.IsNullOrWhiteSpace(DraftCurrency))
                DraftCurrency = pattern.DefaultCurrencyCode;
            if (!string.IsNullOrWhiteSpace(pattern.DefaultPaymentMethod) && Enum.TryParse<PaymentMethod>(pattern.DefaultPaymentMethod, true, out var pm))
                DraftPaymentMethod = pm;
        }
    }
    partial void OnDraftSupplierIdChanged(Guid? value)
    {
        OnPropertyChanged(nameof(SelectedSupplier));
        OnPropertyChanged(nameof(HasSelectedSupplier));
        OnPropertyChanged(nameof(SupplierCurrentBalanceDisplay));
        OnPropertyChanged(nameof(SupplierCreditLimitDisplay));
        OnPropertyChanged(nameof(SupplierAvailableCreditDisplay));
        OnPropertyChanged(nameof(IsSupplierCreditBlocked));
        OnPropertyChanged(nameof(HasSupplierCreditLimit));
        Refresh(); _ = LoadPaymentTermsAsync(value);
        if (IsEditing && Current is null && value is { } supplierId) _ = ApplySupplierAccountCurrencyAsync(supplierId);
    }

    /// <summary>A new purchase invoice opens in the currency of the supplier's own payable account.</summary>
    private async Task ApplySupplierAccountCurrencyAsync(Guid supplierId)
    {
        try
        {
            if (Suppliers.FirstOrDefault(s => s.Id == supplierId)?.PayableAccountId is not { } accountId || accountId == Guid.Empty) return;
            var account = await accountsClient.GetAccountAsync(CompanyId, BranchId, accountId, CancellationToken.None).ConfigureAwait(true);
            if (DraftSupplierId != supplierId || Current is not null) return;
            if (account.FixedCurrencyCode is { Length: 3 } code && CurrencyOptions.Contains(code)) ApplySystemValue(() => DraftCurrency = code);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { /* the currency stays as the operator or pattern set it */ }
    }
    partial void OnDraftWarehouseIdChanged(Guid? value) => Refresh();
    partial void OnDraftPaymentMethodChanged(PaymentMethod value) => Refresh();
    partial void OnDraftCurrencyChanged(string value)
    {
        // The rate belongs to the currency, so the register is asked again; the old value was auto-filled, not typed.
        lastAutoRate = null;
        if (IsEditing && DraftRate.Length > 0) DraftRate = string.Empty;
        Refresh();
        RefreshRate();
    }
    partial void OnDraftRateChanged(string value) => Refresh();
    partial void OnDraftDateChanged(DateTime? value) { Refresh(); RefreshNumberPreview(); RefreshRate(); }
    partial void OnSelectedLineChanged(PurchaseLineItem? value) => RemoveLineCommand?.NotifyCanExecuteChanged();
    partial void OnReturnReasonChanged(string value) { PostReturnCommand?.NotifyCanExecuteChanged(); DraftFieldChanged(); }
}
