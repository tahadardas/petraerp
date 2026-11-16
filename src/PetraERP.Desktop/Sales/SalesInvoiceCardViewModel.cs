using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Accounting;
using PetraERP.Desktop.Controls;
using PetraERP.Desktop.Currencies;
using PetraERP.Desktop.Inventory;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Parties;
using PetraERP.Desktop.Patterns;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Sales;

public sealed class PatternLookupItem
{
    public bool AllowManualNumbering { get; init; }
    /// <summary>The pattern fixes the document date to the day it is added (voucher patterns).</summary>
    public bool LockDocumentDate { get; init; }
    public Guid PatternId { get; init; }
    public Guid RevisionId { get; init; }
    /// <summary>Column headers set in the pattern's line fields (field name to caption); a missing entry means the standard caption.</summary>
    public IReadOnlyDictionary<string, string> FieldCaptions { get; init; } = new Dictionary<string, string>();
    public string Name { get; init; } = string.Empty;
    public Guid? DefaultWarehouseId { get; init; }
    public Guid? DefaultCostCenterId { get; init; }
    public string? DefaultCurrencyCode { get; init; }
    public string? DefaultPaymentMethod { get; init; }
    public BusinessOperationKind? OperationKind { get; init; }
    /// <summary>The pattern's cash account: where a cash document's total goes unless the document names another account.</summary>
    public Guid? CashAccountId { get; init; }
    /// <summary>The pattern lets a cash document name its own cash account (the Cash role is not enforced).</summary>
    public bool AllowsCashAccountOverride { get; init; }
    /// <summary>The pattern lets a credit document name its own receivable account (the Receivable role is mapped and not enforced).</summary>
    public bool AllowsReceivableAccountOverride { get; init; }
}

public sealed partial class SalesInvoiceCardViewModel : EntityCardViewModel
{
    public SalesDocumentLinksViewModel? DocumentLinks { get; init; }
    /// <summary>Company currencies and register rates; optional so older compositions still build (no currency list then).</summary>
    public ICurrenciesClient? Currencies { get; init; }
    /// <summary>Server price resolver; optional (no automatic price then, the user enters it).</summary>
    public IPricingClient? Pricing { get; init; }
    private string? lastAutoRate;
    private int rateGeneration;
    private DocumentCurrencyReference? currencyRead;
    private DocumentCurrencyReference CurrencyRead => currencyRead ??= new DocumentCurrencyReference(Currencies);
    public Relationships.RelatedDocumentsViewModel? ReceiptLinks { get; init; }
    private readonly Guid postingClientId = Guid.NewGuid();
    private Guid? requestedPatternId;
    /// <summary>A card opened from a pattern button belongs to that pattern; the picker is fixed while the invoice is new.</summary>
    public bool IsPatternLocked => requestedPatternId.HasValue && Invoice is null;
    public bool IsPatternPickerEnabled => IsEditing && !IsPatternLocked;
    private readonly ISalesInvoiceClient salesInvoiceClient;
    private readonly IPartiesClient partiesClient;
    private readonly IInventoryClient inventoryClient;
    private readonly ItemUnitCatalog unitCatalog;
    private readonly IPatternsClient patternsClient;
    private readonly DocumentNumberPeek numberPeek;
    private readonly IChartOfAccountsClient accountsClient;
    private readonly ShellNavigator navigator;
    private readonly IUserConfirmation confirmation;

    [ObservableProperty] private SalesInvoiceDetailDto? invoice;
    /// <summary>Set when the lookup lists could not be loaded completely; the card must not look ready while it is set.</summary>
    [ObservableProperty] private string? lookupsError;

    public bool HasLookupsError => !string.IsNullOrEmpty(LookupsError);
    public AsyncRelayCommand RetryLookupsCommand => retryLookups ??= new AsyncRelayCommand(() => LoadLookupsAsync(CancellationToken.None));
    private AsyncRelayCommand? retryLookups;
    partial void OnLookupsErrorChanged(string? value) => OnPropertyChanged(nameof(HasLookupsError));
    [ObservableProperty] private Guid? invoiceId;

    // Header draft fields
    [ObservableProperty] private Guid? draftPatternRevisionId;
    [ObservableProperty] private Guid? draftCustomerId;
    [ObservableProperty] private Guid? draftWarehouseId;
    [ObservableProperty] private Guid? draftCostCenterId;
    [ObservableProperty] private DateTime? draftInvoiceDate = DateTime.Today;
    [ObservableProperty] private PaymentMethod draftPaymentMethod = PaymentMethod.Cash;
    // Empty currency/rate mean "resolve on the server": customer, then pattern, then company base; register rate on the date.
    [ObservableProperty] private string draftCurrencyCode = string.Empty;
    [ObservableProperty] private string draftExchangeRate = string.Empty;
    [ObservableProperty] private string rateInfo = string.Empty;
    [ObservableProperty] private string? draftNotes;
    [ObservableProperty] private string draftSellerText = string.Empty;
    [ObservableProperty] private string draftDistributorText = string.Empty;

    private static int? ParseCode(string? text) =>
        int.TryParse(text?.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : null;

    [ObservableProperty] private string actionReason = string.Empty;
    [ObservableProperty] private bool confirmCreditWarning;
    [ObservableProperty] private bool isConcreteOperation;
    [ObservableProperty] private bool overrideCreditPolicy;
    [ObservableProperty] private string creditOverrideReason = string.Empty;
    /// <summary>Required by the server only when the company policy authorizes issuing below zero stock.</summary>
    [ObservableProperty] private string negativeStockReason = string.Empty;

    /// <summary>The number this draft would most likely get, read without reserving it; empty until the server answers.</summary>
    [ObservableProperty] private string? numberPreview;

    // Totals displays
    [ObservableProperty] private string subTotalDisplay = "0.0000";
    [ObservableProperty] private string discountTotalDisplay = "0.0000";
    [ObservableProperty] private string taxTotalDisplay = "0.0000";
    [ObservableProperty] private string additionsTotalDisplay = "0.0000";
    [ObservableProperty] private string grandTotalDisplay = "0.0000";
    [ObservableProperty] private string totalQuantityDisplay = "0.0000";

    [ObservableProperty] private SalesInvoiceLineItem? selectedLine;

    // Lookups
    [ObservableProperty] private Guid? draftCustomerAccountId;
    public ObservableCollection<LookupItem> CustomerAccountLookups { get; } = [];
    public ObservableCollection<PatternLookupItem> PatternLookups { get; } = [];
    /// <summary>Line-grid column captions defined by the selected pattern (field name to caption).</summary>
    public IReadOnlyDictionary<string, string> FieldCaptions =>
        PatternLookups.FirstOrDefault(p => p.RevisionId == DraftPatternRevisionId)?.FieldCaptions ?? new Dictionary<string, string>();
    public ObservableCollection<PartyLookupDto> CustomerLookups { get; } = [];
    public ObservableCollection<WarehouseDto> WarehouseLookups { get; } = [];
    public ObservableCollection<ItemLookupDto> ItemLookups { get; } = [];
    public ObservableCollection<UnitOfMeasureDto> UnitLookups { get; } = [];
    public ObservableCollection<CostCenterResponse> CostCenterLookups { get; } = [];
    /// <summary>Enabled company currencies from the server; an empty choice lets the server resolve the currency.</summary>
    public ObservableCollection<string> CurrencyOptions { get; } = [];

    public Func<string, CancellationToken, Task<IReadOnlyList<LookupItem>>> SearchItemsAsync => async (query, ct) =>
    {
        if (CompanyId == Guid.Empty || BranchId == Guid.Empty) return [];
        var items = await inventoryClient.LookupItemsAsync(CompanyId, BranchId, 50, query, ct).ConfigureAwait(false);
        foreach (var item in items)
        {
            if (item.Id.HasValue && !ItemLookups.Any(i => i.Id == item.Id.Value))
            {
                App.Current?.Dispatcher.BeginInvoke(() =>
                {
                    if (!ItemLookups.Any(i => i.Id == item.Id.Value))
                    {
                        ItemLookups.Add(item);
                    }
                });
            }
        }
        return items.Where(i => i.Id.HasValue).Select(i => new LookupItem(i.Id!.Value, i.Code ?? string.Empty, i.NameArabic ?? string.Empty, i.NameEnglish)).ToList();
    };

    public Func<string, CancellationToken, Task<IReadOnlyList<LookupItem>>> SearchCustomersAsync => async (query, ct) =>
    {
        if (CompanyId == Guid.Empty || BranchId == Guid.Empty) return [];
        var parties = await partiesClient.LookupPartiesAsync(CompanyId, BranchId, 50, query, PartyRole.Customer, ct).ConfigureAwait(false);
        foreach (var party in parties)
        {
            if (party.Id.HasValue && !CustomerLookups.Any(c => c.Id == party.Id.Value))
            {
                App.Current?.Dispatcher.BeginInvoke(() =>
                {
                    if (!CustomerLookups.Any(c => c.Id == party.Id.Value))
                    {
                        CustomerLookups.Add(party);
                    }
                });
            }
        }
        return parties.Where(p => p.Id.HasValue).Select(p => new LookupItem(p.Id!.Value, p.Code ?? string.Empty, p.NameArabic ?? string.Empty, p.NameEnglish)).ToList();
    };

    // Document Lines
    public ObservableCollection<SalesInvoiceLineItem> Lines { get; } = [];

    public SalesInvoiceCardViewModel(
        ISalesInvoiceClient salesInvoiceClient,
        IPartiesClient partiesClient,
        IInventoryClient inventoryClient,
        IPatternsClient patternsClient,
        IChartOfAccountsClient accountsClient,
        ShellNavigator navigator,
        DesktopOperation operation,
        ILocalizationService localization,
        IUserConfirmation confirmation)
        : base(operation, localization, confirmation)
    {
        this.salesInvoiceClient = salesInvoiceClient;
        this.partiesClient = partiesClient;
        this.inventoryClient = inventoryClient;
        unitCatalog = new ItemUnitCatalog(inventoryClient);
        this.patternsClient = patternsClient;
        numberPeek = new DocumentNumberPeek(patternsClient);
        this.accountsClient = accountsClient;
        this.navigator = navigator;
        this.confirmation = confirmation;

        RefreshCommand = new AsyncRelayCommand(
            () => IsViewMode ? RunAsync(ct => LoadInvoiceAsync(ct)) : Task.CompletedTask,
            () => HasContext && Idle && InvoiceId is not null && IsViewMode);

        AddLineCommand = new RelayCommand(AddLine, () => IsEditing);
        RemoveLineCommand = new RelayCommand<SalesInvoiceLineItem>(RemoveLine, line => IsEditing && line is not null && Lines.Contains(line));
        UseListPriceCommand = new RelayCommand(() =>
        {
            if (SelectedLine is not { } line) return;
            line.HasManualPrice = false;
            _ = QuotePriceAsync(line);
        }, () => HasContext && Idle && IsEditing && SelectedLine is not null);

        ApproveCommand = new AsyncRelayCommand(ApproveInvoiceAsync, () => HasContext && Idle && CanApprove);
        PostCommand = new AsyncRelayCommand(PostInvoiceAsync, () => HasContext && Idle && CanPost);
        ReverseCommand = new AsyncRelayCommand(ReverseInvoiceAsync, () => HasContext && Idle && CanReverse);
        DeleteDraftCommand = new AsyncRelayCommand(DeleteDraftInvoiceAsync, () => HasContext && Idle && CanDeleteDraft);

        FirstCommand = new AsyncRelayCommand(() => MoveAsync("first"), () => CanMove);
        PreviousCommand = new AsyncRelayCommand(() => MoveAsync("previous"), () => CanMove);
        NextCommand = new AsyncRelayCommand(() => MoveAsync("next"), () => CanMove);
        LastCommand = new AsyncRelayCommand(() => MoveAsync("last"), () => CanMove);
        AutoGrowCommand = new RelayCommand(() => AutoGrowLines(10), () => IsEditing);
    }

    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand AddLineCommand { get; }
    public RelayCommand<SalesInvoiceLineItem> RemoveLineCommand { get; }
    public RelayCommand UseListPriceCommand { get; }
    public AsyncRelayCommand ApproveCommand { get; }
    public AsyncRelayCommand PostCommand { get; }
    public AsyncRelayCommand ReverseCommand { get; }
    public AsyncRelayCommand DeleteDraftCommand { get; }
    public bool CanMove => HasContext && Idle && InvoiceId is not null && IsViewMode && !HasUnsavedChanges;

    /// <summary>
    /// Moves to the first/previous/next/last invoice of the whole branch (fixed server order: date, created-at, id).
    /// The target is fetched in full before anything on the card changes, so a failed request leaves the current
    /// invoice's identity and details untouched and the error is shown instead of swallowed.
    /// </summary>
    private SalesInvoiceListScope? navigationScope;

    private Task MoveAsync(string direction) => !CanMove ? Task.CompletedTask : RunAsync(async cancellationToken =>
    {
        var originId = InvoiceId;
        var targetId = await salesInvoiceClient.NavigateSalesInvoiceAsync(CompanyId, BranchId, direction, originId, navigationScope, cancellationToken).ConfigureAwait(true);
        if (targetId is not { } id || id == originId) return;
        var detail = await salesInvoiceClient.GetSalesInvoiceAsync(CompanyId, BranchId, id, cancellationToken).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        if (InvoiceId != originId || !IsViewMode || HasUnsavedChanges) return; // the card changed while the target was loading
        InvoiceId = id;
        ApplyLoadedInvoice(detail);
    });

    public AsyncRelayCommand FirstCommand { get; }
    public AsyncRelayCommand PreviousCommand { get; }
    public AsyncRelayCommand NextCommand { get; }
    public AsyncRelayCommand LastCommand { get; }
    public RelayCommand AutoGrowCommand { get; }

    public bool HasInvoice => Invoice is not null;
    public bool CanManage => Can(SalesInvoicesViewModel.ManagePermission);
    public bool CanApprovePermission => Can(SalesInvoicesViewModel.ApprovePermission);
    public bool CanPostPermission => Can(SalesInvoicesViewModel.PostPermission);
    public bool CanReversePermission => Can(SalesInvoicesViewModel.ReversePermission);
    public bool CanOverrideCredit => Can("Sales.Credit.Override");
    public bool IsCreditInvoice => Invoice?.PaymentMethod == PaymentMethod.Credit;
    public string CustomerFieldLabel => Localization["D_Customer"] + (DraftPaymentMethod == PaymentMethod.Credit ? " *" : string.Empty);
    public string CreditPolicyDisplay => Invoice is null ? string.Empty :
        $"{Localization[Invoice.CreditPolicyKind switch { 1 => "CreditPolicyKind_AllowWithWarning", 2 => "CreditPolicyKind_BlockOnExceed", _ => "CreditPolicyKind_NoLimit" }]} | " +
        (Invoice.CreditBlocked == true ? Localization["AR_CreditSalesBlocked"] + " | " : string.Empty) +
        $"{Localization["AR_CreditExposureBase"]}: {Invoice.CreditExposureBase ?? "—"} | {Localization["AR_CreditLimitBase"]}: {Invoice.CreditLimitBase ?? "—"} | {Localization["AR_CreditProjectedBase"]}: {Invoice.CreditProjectedExposureBase ?? "—"}";
    public string CreditAuditDisplay => Invoice?.CreditPolicyRecordedAtUtc is { } at
        ? $"{at:g} | {Invoice.CreditPolicyActorUserId} | {Invoice.CreditOverrideReason}" : string.Empty;

    protected override string RequiredPermission => SalesInvoicesViewModel.ViewPermission;

    public bool IsInvoiceDraft => Invoice is null || Invoice.Status == SalesInvoiceStatus.Draft;
    public bool IsInvoiceApproved => Invoice?.Status == SalesInvoiceStatus.Approved;
    public bool IsInvoicePosted => Invoice?.Status == SalesInvoiceStatus.Posted;
    public bool IsInvoiceReversed => Invoice?.Status == SalesInvoiceStatus.Reversed;
    public bool IsInvoiceCancelled => Invoice?.Status == SalesInvoiceStatus.Cancelled;

    public bool CanApprove => CanApprovePermission && IsInvoiceDraft && Invoice is not null && !IsEditing;
    public bool CanPrepareCreditAction => CanPostPermission && (IsInvoiceDraft || IsInvoiceApproved) && Invoice is not null && !IsEditing;
    public bool CanPost => CanPrepareCreditAction && CreditActionReady;
    private bool CreditActionReady => Invoice is not { CreditPolicyRequiresAction: true } current ||
        (!string.IsNullOrWhiteSpace(CreditOverrideReason) && CreditOverrideReason.Trim().Length <= 500 &&
         (current.CreditBlocked == true || current.CreditPolicyKind == 2
            ? OverrideCreditPolicy && CanOverrideCredit
            : ConfirmCreditWarning || (OverrideCreditPolicy && CanOverrideCredit)));
    /// <summary>Only a saved, never-approved draft can be deleted; approved/posted invoices are reversed instead.</summary>
    public bool CanDeleteDraft => CanManage && Invoice is { Status: SalesInvoiceStatus.Draft } && InvoiceId is not null && !IsEditing;
    // Full reversal and partial returns are mutually exclusive; a return document itself is never reversed here.
    public bool CanReverse => CanReversePermission && IsInvoicePosted && Invoice?.ReversedByInvoiceId == null
        && Invoice?.ReturnOfInvoiceId == null && !HasReturns && !IsEditing;

    public string Title => IsNewMode
        ? Localization["D_NewSalesInvoice"]
        : Invoice is not null
            ? $"{RecordReference} - {Invoice.CustomerName}"
            : Localization["D_SalesInvoiceCard"];

    /// <summary>
    /// Always-visible identity of the card: the official number once allocated, otherwise a stable draft reference
    /// derived from the document id. The reference is not an accounting number and is never written as one.
    /// </summary>
    public string RecordReference => Invoice is null
        ? Localization["D_NewSalesInvoice"]
        : !string.IsNullOrWhiteSpace(Invoice.InvoiceNumber)
            ? Invoice.InvoiceNumber
            : "مسودة " + (Invoice.Id ?? Guid.Empty).ToString("N")[..8].ToUpperInvariant();

    /// <summary>Explains why the number is missing on a card that has none yet.</summary>
    public string NumberAssignmentHint => Invoice is not null && string.IsNullOrWhiteSpace(Invoice.InvoiceNumber)
        ? "الرقم الرسمي يُخصَّص عند الترحيل"
        : string.Empty;

    partial void OnInvoiceChanged(SalesInvoiceDetailDto? value)
    {
        OnPropertyChanged(nameof(IsPatternLocked)); OnPropertyChanged(nameof(IsPatternPickerEnabled));
        OnPropertyChanged(nameof(RecordReference));
        OnPropertyChanged(nameof(NumberAssignmentHint));
        OnPropertyChanged(nameof(Title));
    }

    public string StatusText => Invoice is null
        ? "مسودة جديدة"
        : Invoice.Status switch
        {
            SalesInvoiceStatus.Draft => "مسودة",
            SalesInvoiceStatus.Approved => "معتمدة",
            SalesInvoiceStatus.Posted => "مرحلة",
            SalesInvoiceStatus.Reversed => "معكوسة",
            SalesInvoiceStatus.Cancelled => "ملغاة",
            _ => "غير محدد",
        };

    public IReadOnlyList<ChoiceOption<PaymentMethod>> PaymentMethodOptions =>
        Options.For<PaymentMethod>(Localization, "PaymentMethod_");

    partial void OnDraftPatternRevisionIdChanged(Guid? value)
    {
        DraftChanged();
        ApplyPatternDefaults(value);
        UpdateCustomerAccount();
        OnPropertyChanged(nameof(FieldCaptions));
        RefreshPriceQuotes();
        RefreshNumberPreview();
    }

    private void ApplyPatternDefaults(Guid? revisionId, bool newDraft = false)
    {
        if (revisionId is null || PatternLookups.FirstOrDefault(p => p.RevisionId == revisionId) is not { } pattern) return;

        if (newDraft || IsNewMode || Invoice is null)
        {
            if (pattern.DefaultWarehouseId.HasValue && DraftWarehouseId is null)
                DraftWarehouseId = pattern.DefaultWarehouseId;
            if (pattern.DefaultCostCenterId.HasValue && DraftCostCenterId is null)
                DraftCostCenterId = pattern.DefaultCostCenterId;
            if (!string.IsNullOrWhiteSpace(pattern.DefaultCurrencyCode) && string.IsNullOrWhiteSpace(DraftCurrencyCode))
                DraftCurrencyCode = pattern.DefaultCurrencyCode;
            if (!string.IsNullOrWhiteSpace(pattern.DefaultPaymentMethod) && Enum.TryParse<PaymentMethod>(pattern.DefaultPaymentMethod, true, out var pm))
                DraftPaymentMethod = pm;
        }
    }
    partial void OnDraftCustomerIdChanged(Guid? value) { DraftChanged(); RefreshPriceQuotes(); _ = LoadPaymentTermsAsync(value); RefreshFirstPayment(); UpdateCustomerAccount(situationChanged: true); }
    partial void OnDraftWarehouseIdChanged(Guid? value) { PropagateLineDefaults(); DraftChanged(); }
    partial void OnDraftCostCenterIdChanged(Guid? value) { PropagateLineDefaults(); DraftChanged(); }

    /// <summary>
    /// Re-applies the header warehouse and cost centre to the slots the operator has not filled, so a header change made
    /// after the buffer was seeded cannot write the superseded values into the saved lines.
    /// </summary>
    private void PropagateLineDefaults()
    {
        if (!IsEditing) return;
        foreach (var line in Lines) line.ApplyDocumentDefaults(DraftWarehouseId, DraftCostCenterId);
    }

    /// <summary>
    /// Asks the server, without reserving, for the number this draft would most likely get. A draft that is never saved
    /// therefore leaves the sequence untouched, and a saved document always carries the number allocated at save.
    /// </summary>
    private void RefreshNumberPreview()
    {
        if (!IsEditing || !HasContext || !string.IsNullOrWhiteSpace(Invoice?.InvoiceNumber)
            || PatternLookups.FirstOrDefault(p => p.RevisionId == DraftPatternRevisionId) is not { } pattern)
        {
            NumberPreview = null;
            return;
        }

        _ = PeekNumberAsync(pattern);
    }

    private async Task PeekNumberAsync(PatternLookupItem pattern)
    {
        var token = Token;
        var preview = await numberPeek.ReadAsync(CompanyId, BranchId, pattern.PatternId, pattern.RevisionId,
            DraftInvoiceDate is { } date ? DateOnly.FromDateTime(date.Date) : null, token).ConfigureAwait(true);
        if (!token.IsCancellationRequested && IsEditing) NumberPreview = preview;
    }

    partial void OnNumberPreviewChanged(string? value) => OnPropertyChanged(nameof(HasNumberPreview));

    /// <summary>Drives the header chip; the estimate is shown only while it is the one the current pattern and date produced.</summary>
    public bool HasNumberPreview => !string.IsNullOrWhiteSpace(NumberPreview);
    partial void OnDraftInvoiceDateChanged(DateTime? value) { RefreshDueDate(); DraftChanged(); _ = RefreshRateAsync(); RefreshPriceQuotes(); RefreshNumberPreview(); }
    partial void OnDraftPaymentMethodChanged(PaymentMethod value) { OnPropertyChanged(nameof(CustomerFieldLabel)); RefreshDueDate(); RefreshFirstPayment(); DraftChanged(); UpdateCustomerAccount(situationChanged: true); }

    private async Task ApplyCustomerAccountCurrencyAsync(Guid accountId)
    {
        try
        {
            var account = await accountsClient.GetAccountAsync(CompanyId, BranchId, accountId, CancellationToken.None).ConfigureAwait(true);
            if (DraftCustomerAccountId != accountId || InvoiceId is not null) return;
            if (account.FixedCurrencyCode is { Length: 3 } code && CurrencyOptions.Contains(code)) DraftCurrencyCode = code;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { /* the currency stays as the operator or pattern set it */ }
    }

    private bool applyingAccountDefault;
    private bool customerAccountOverridden;

    private PatternLookupItem? DraftPattern => PatternLookups.FirstOrDefault(p => p.RevisionId == DraftPatternRevisionId);

    /// <summary>Where the invoice total goes when the invoice names no account: the pattern cash account, or the customer's receivable account.</summary>
    private Guid? DefaultCustomerAccountId()
    {
        if (DraftPaymentMethod == PaymentMethod.Cash) return DraftPattern?.CashAccountId;
        return CustomerLookups.FirstOrDefault(c => c.Id == DraftCustomerId)?.ReceivableAccountId is { } id && id != Guid.Empty ? id : null;
    }

    /// <summary>The invoice may name its own account only where its pattern permits that for the payment method (the server enforces it).</summary>
    public bool CanChangeCustomerAccount => IsEditing && DraftPattern is { } pattern
        && (DraftPaymentMethod == PaymentMethod.Cash ? pattern.AllowsCashAccountOverride : pattern.AllowsReceivableAccountOverride);

    public bool IsCustomerAccountReadOnly => !CanChangeCustomerAccount;

    public string CustomerAccountHint => CanChangeCustomerAccount
        ? "الحساب الذي يُرحَّل إليه إجمالي الفاتورة. يمكن تغييره لأن النمط يسمح بذلك."
        : DraftPaymentMethod == PaymentMethod.Cash
            ? "حساب الصندوق المحدد في النمط. لتغييره من الفاتورة ألغِ «إلزامي» عن حساب الصندوق في بطاقة النمط."
            : "حساب ذمة الزبون. لتغييره من الفاتورة عرّف حساب الزبائن في بطاقة النمط دون «إلزامي».";

    /// <summary>The account to send: the operator's choice when it differs from the default, otherwise nothing (the rule decides).</summary>
    private Guid? CustomerAccountOverride => customerAccountOverridden && DraftCustomerAccountId is { } id && id != DefaultCustomerAccountId() ? id : null;

    partial void OnDraftCustomerAccountIdChanged(Guid? value)
    {
        if (applyingAccountDefault) return;
        customerAccountOverridden = value is not null;
        DraftChanged();
        if (value is { } accountId && IsEditing && InvoiceId is null && DraftPaymentMethod != PaymentMethod.Cash) _ = ApplyCustomerAccountCurrencyAsync(accountId);
    }

    /// <summary>Shows the stored override, or the default account when the invoice names none. A system value, never an edit.</summary>
    private void ShowCustomerAccount(Guid? storedOverride)
    {
        ApplySystemValue(() =>
        {
            applyingAccountDefault = true;
            try
            {
                customerAccountOverridden = storedOverride is not null;
                DraftCustomerAccountId = storedOverride ?? DefaultCustomerAccountId();
            }
            finally { applyingAccountDefault = false; }
        });
        OnPropertyChanged(nameof(CanChangeCustomerAccount)); OnPropertyChanged(nameof(IsCustomerAccountReadOnly)); OnPropertyChanged(nameof(CustomerAccountHint));
    }

    /// <summary>
    /// The customer, the payment method or the pattern changed (or the lookups arrived): an account the operator chose is kept
    /// while it is still permitted; otherwise the field goes back to the default for the new situation.
    /// </summary>
    private void UpdateCustomerAccount(bool situationChanged = false)
    {
        var keep = customerAccountOverridden && !situationChanged ? DraftCustomerAccountId : null;
        ShowCustomerAccount(keep);
        if (IsEditing && InvoiceId is null && DraftPaymentMethod != PaymentMethod.Cash && DraftCustomerAccountId is { } accountId) _ = ApplyCustomerAccountCurrencyAsync(accountId);
    }
    partial void OnDraftCurrencyCodeChanged(string value)
    {
        // The previous rate was auto-filled for the old currency, so it must not be mistaken for one the operator typed:
        // drop it, otherwise the refresh below would keep it as a typed rate (a USD invoice stuck at the base rate 1).
        if (lastAutoRate is not null && string.Equals(DraftExchangeRate, lastAutoRate, StringComparison.Ordinal)) DraftExchangeRate = string.Empty;
        lastAutoRate = null;
        DraftChanged(); OnPropertyChanged(nameof(DisplayCurrency)); _ = RefreshRateAsync(); RefreshPriceQuotes();
    }
    partial void OnDraftExchangeRateChanged(string value) { if (!string.Equals(value, lastAutoRate, StringComparison.Ordinal)) ++rateGeneration; DraftChanged(); }
    partial void OnDraftNotesChanged(string? value) => DraftChanged();
    partial void OnConfirmCreditWarningChanged(bool value) => NotifyCreditAction();
    partial void OnOverrideCreditPolicyChanged(bool value) => NotifyCreditAction();
    partial void OnCreditOverrideReasonChanged(string value) => NotifyCreditAction();
    private void NotifyCreditAction() { OnPropertyChanged(nameof(CanPost)); PostCommand?.NotifyCanExecuteChanged(); }

    protected override bool CanEditRecord => CanManage && (Invoice is null || IsInvoiceDraft || !EntryOptionsHub.Current.PreventEditingPostedDocuments);
    protected override bool CanCreateRecord => CanManage;
    protected override void OnModeChanged(EntityCardMode mode)
    {
        OnPropertyChanged(nameof(IsPatternPickerEnabled));
        RefreshNumberPreview();
        // A brand-new draft holds no stored rate, so the register answer is the only one worth showing yet.
        if (mode == EntityCardMode.New) _ = RefreshRateAsync();
        // Editing a stored document: its lines now need their items' unit choices (one cached request per item).
        if (mode == EntityCardMode.Edit)
            foreach (var line in Lines.Where(l => l.ItemId.HasValue).ToArray()) _ = ResolveLineUnitsAsync(line, line.ItemId!.Value);
        OnPropertyChanged(nameof(CanChangeCustomerAccount)); OnPropertyChanged(nameof(IsCustomerAccountReadOnly)); OnPropertyChanged(nameof(CustomerAccountHint));
    }

    public override bool CanSaveDraft =>
        (DraftPaymentMethod == PaymentMethod.Cash || RequiredInput.Selection(DraftCustomerId)) &&
        RequiredInput.Selection(DraftWarehouseId) &&
        RequiredInput.Selection(DraftPatternRevisionId) && DraftInvoiceDate.HasValue &&
        (string.IsNullOrWhiteSpace(DraftCurrencyCode) || RequiredInput.Currency(DraftCurrencyCode)) &&
        (string.IsNullOrWhiteSpace(DraftExchangeRate) || RequiredInput.AmountText(DraftExchangeRate, 8, true)) &&
        Lines.Any(l => l.IsComplete) &&
        !Lines.Any(l => l.IsIncomplete);

    public override IReadOnlyList<string> GetValidationErrors()
    {
        var errors = new List<string>();
        if (!RequiredInput.Selection(DraftPatternRevisionId))
            errors.Add(Localization["D_DocumentPatterns"] + ": " + Localization["D_RequiredField"]);
        if (!RequiredInput.Selection(DraftWarehouseId))
            errors.Add(Localization["D_Warehouse"] + ": " + Localization["D_RequiredField"]);
        if (DraftPaymentMethod != PaymentMethod.Cash && !RequiredInput.Selection(DraftCustomerId))
            errors.Add(CustomerFieldLabel + ": " + Localization["D_RequiredField"]);
        if (!DraftInvoiceDate.HasValue)
            errors.Add(Localization["D_InvoiceDate"] + ": " + Localization["D_RequiredField"]);
        if (!string.IsNullOrWhiteSpace(DraftCurrencyCode) && !RequiredInput.Currency(DraftCurrencyCode))
            errors.Add(Localization["CUR_Currency"] + ": " + Localization["UI_InvalidCurrency"]);
        if (!string.IsNullOrWhiteSpace(DraftExchangeRate) && !RequiredInput.AmountText(DraftExchangeRate, 8, true))
            errors.Add(Localization["CUR_Rate"] + ": " + Localization["UI_InvalidNumber"]);
        if (!Lines.Any(l => l.IsComplete))
            errors.Add(Localization["D_AddLine"] + ": " + Localization["D_AtLeastOneLineRequired"]);
        DocumentEntryValidation.AddIncompleteLineErrors(errors, key => Localization[key], "D_InvoiceLines", Lines);
        return errors;
    }

    protected override string? GetValidationFocusTarget()
    {
        if (!DraftPatternRevisionId.HasValue) return "pattern";
        if (!DraftWarehouseId.HasValue) return "warehouse";
        if (DraftPaymentMethod != PaymentMethod.Cash && !DraftCustomerId.HasValue) return "customer";
        if (!DraftInvoiceDate.HasValue) return "date";
        if (!string.IsNullOrWhiteSpace(DraftCurrencyCode) && !RequiredInput.Currency(DraftCurrencyCode)) return "currency";
        if (!string.IsNullOrWhiteSpace(DraftExchangeRate) && !RequiredInput.AmountText(DraftExchangeRate, 8, true)) return "rate";
        if (!Lines.Any(line => line.IsComplete) || Lines.Any(line => line.IsIncomplete))
        {
            if (Lines.FirstOrDefault(line => line.IsIncomplete) is { } offender) SelectedLine = offender;
            return "lines";
        }
        return null;
    }

    /// <summary>By value against the snapshot taken when the draft was loaded; see the DirtyTracking part.</summary>
    protected override bool HasDraftChanges => draftBaseline is not null && !string.Equals(DraftSignature(), draftBaseline, StringComparison.Ordinal);

    protected override void LoadDraft(EntityCardMode mode)
    {
        if (mode == EntityCardMode.New)
        {
            IsConcreteOperation = false;
            // Order matters: the fields are emptied first, then the pattern fills its own defaults, then only what is still
            // empty gets a company-level default. A value is never taken just because it is the first entry of a list.
            DraftCustomerId = null;
            DraftWarehouseId = null;
            DraftCostCenterId = null;
            DraftInvoiceDate = DateTime.Today;
            DraftPaymentMethod = EntryOptionsHub.Current.SalesStartsOnCredit ? PaymentMethod.Credit : PaymentMethod.Cash;
            lastAutoRate = null;
            DraftCurrencyCode = string.Empty;
            DraftExchangeRate = string.Empty;
            DraftPatternRevisionId = (PatternLookups.FirstOrDefault(p => requestedPatternId.HasValue && p.PatternId == requestedPatternId) ?? PatternLookups.FirstOrDefault())?.RevisionId;
            ApplyPatternDefaults(DraftPatternRevisionId, newDraft: true);
            // Without a pattern currency the invoice starts in the company base currency (the field stays editable).
            if (string.IsNullOrWhiteSpace(DraftCurrencyCode) && Context?.BaseCurrencyCode is { Length: 3 } baseCurrency) DraftCurrencyCode = baseCurrency;
            // Without a pattern warehouse the field is filled only when there is exactly one warehouse to choose.
            DraftWarehouseId ??= WarehouseLookups.Count == 1 ? WarehouseLookups[0].Id : null;
            DraftNotes = null;
            DraftSellerText = string.Empty;
            DraftDistributorText = string.Empty;
            ActionReason = string.Empty;
            NegativeStockReason = string.Empty;

            Lines.Clear();
            Adjustments.Clear();
            OnPropertyChanged(nameof(HasAdjustments));
            EnsureEntryBuffer(EntryOptionsHub.Current.InvoiceRows);
        }
        else if (Invoice is not null)
        {
            DraftPatternRevisionId = Invoice.PatternRevisionId;
            DraftCustomerId = Invoice.CustomerIsCashCustomer == true ? null : Invoice.CustomerId;
            DraftWarehouseId = Invoice.WarehouseId;
            DraftCostCenterId = Invoice.CostCenterId;
            DraftInvoiceDate = Invoice.InvoiceDateUtc?.DateTime ?? DateTime.Today;
            DraftPaymentMethod = Invoice.PaymentMethod ?? PaymentMethod.Cash;
            // The stored document rate is shown as is; it is only replaced when the user changes currency or date.
            lastAutoRate = null;
            DraftCurrencyCode = Invoice.CurrencyCode ?? string.Empty;
            DraftExchangeRate = Invoice.ExchangeRate ?? string.Empty;
            DraftNotes = Invoice.Notes;
            DraftSellerText = Invoice.SellerCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
            DraftDistributorText = Invoice.DistributorCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
            ActionReason = string.Empty;
            NegativeStockReason = string.Empty;

            PopulateLinesFromInvoice(Invoice);
        }

        ShowCustomerAccount(mode == EntityCardMode.New ? null : Invoice?.CustomerAccountId);
        RecalculateTotals();
        CaptureDraftBaseline();
        DraftChanged();
    }

    private void PopulateLinesFromInvoice(SalesInvoiceDetailDto detail)
    {
        Lines.Clear();
        PopulateAdjustments(detail);
        IsConcreteOperation = detail.Lines?.Any(l => !string.IsNullOrEmpty(l.ConcreteGrade)
            || (decimal.TryParse(l.PumpCost, out var p) && p > 0)
            || (decimal.TryParse(l.TransportCost, out var t) && t > 0)) == true;
        if (detail.Lines is not null)
        {
            foreach (var l in detail.Lines)
            {
                var lineItem = new SalesInvoiceLineItem
                {
                    Id = l.Id ?? Guid.NewGuid(),
                    ItemId = l.ItemId,
                    ItemCode = l.ItemCode ?? string.Empty,
                    ItemNameArabic = l.ItemNameArabic ?? string.Empty,
                    UnitId = l.UnitId,
                    UnitCode = l.UnitCode ?? string.Empty,
                    ConcreteGrade = l.ConcreteGrade,
                    Quantity = l.Quantity ?? "0.0000",
                    UnitPrice = l.UnitPrice ?? "0.0000",
                    DiscountPercentage = l.DiscountPercentage ?? "0.0000",
                    DiscountAmount = l.DiscountAmount ?? "0.0000",
                    TaxPercentage = l.TaxPercentage ?? "0.0000",
                    TaxAmount = l.TaxAmount ?? "0.0000",
                    PumpCost = l.PumpCost ?? "0.0000",
                    TransportCost = l.TransportCost ?? "0.0000",
                    LineTotal = l.LineTotal ?? "0.0000",
                    CostCenterId = l.CostCenterId,
                    WarehouseId = l.WarehouseId,
                    Notes = l.Notes,
                    DisplayOrder = l.DisplayOrder ?? 0,
                    PriceSource = Localization["CUR_PriceStored"],
                    IsPersisted = true,
                    UnitAssignedFor = l.ItemId,
                };
                lineItem.ShowUnit(UnitOption(l.UnitId, l.UnitCode));
                lineItem.Changed = () => OnLineChanged(lineItem);
                Lines.Add(lineItem);
            }
        }
        if (IsEditing)
        {
            EnsureEntryBuffer(50);
        }
    }

    private void OnLineChanged(SalesInvoiceLineItem line)
    {
        var idx = Lines.IndexOf(line);
        if (idx >= Lines.Count - 2 && IsEditing)
        {
            AutoGrowLines(10);
        }
        AssignLineUnit(line);
        RecalculateTotals();
        DraftChanged();
        if (!line.ApplyingQuote && !line.IsPristineBlank && line.ItemId.HasValue) _ = QuotePriceAsync(line);
    }

    private LookupItem? UnitOption(Guid? unitId, string? fallbackCode = null)
    {
        if (unitId is not { } id) return null;
        if (UnitLookups.FirstOrDefault(u => u.Id == id) is { } unit) return new(id, unit.Code ?? string.Empty, unit.NameArabic ?? string.Empty, unit.NameEnglish);
        return string.IsNullOrWhiteSpace(fallbackCode) ? null : new(id, fallbackCode, fallbackCode, null);
    }

    /// <summary>
    /// The unit always belongs to the line's item. Choosing an item fills its base unit; replacing the item replaces the unit;
    /// clearing the item returns the unit. The unit column then offers only that item's units (base unit plus units with a
    /// direct conversion to it), asked from the server once per item. No unit is ever borrowed from another item or from the
    /// company unit list; an item whose base unit cannot be found leaves the line without a unit, and the row says why.
    /// </summary>
    private void AssignLineUnit(SalesInvoiceLineItem line)
    {
        if (line.ItemId is not { } itemId)
        {
            if (line.UnitAssignedFor is null) return;
            line.UnitAssignedFor = null; line.ResetUnits();
            if (!line.IsPersisted && line.UnitId is not null) { line.UnitCode = string.Empty; line.UnitId = null; }
            return;
        }

        if (line.UnitAssignedFor == itemId)
        {
            // Same item: only keep the unit code beside a unit the operator picked from the column.
            if (line.UnitId is { } chosen && UnitLookups.FirstOrDefault(u => u.Id == chosen)?.Code is { } code && line.UnitCode != code) line.UnitCode = code;
            return;
        }

        line.UnitAssignedFor = itemId; line.ResetUnits();
        var matched = ItemLookups.FirstOrDefault(i => i.Id == itemId);
        if (matched?.BaseUnitId is { } baseUnitId && baseUnitId != Guid.Empty)
        {
            line.UnitCode = matched.BaseUnitCode ?? string.Empty;
            line.UnitId = baseUnitId;
        }
        else if (line.UnitId is not null) { line.UnitCode = string.Empty; line.UnitId = null; }

        line.ShowUnit(UnitOption(line.UnitId, line.UnitCode));
        _ = ResolveLineUnitsAsync(line, itemId);
    }

    /// <summary>Asks the item's units (cached per item) and applies them to the line if it still shows that item.</summary>
    private async Task ResolveLineUnitsAsync(SalesInvoiceLineItem line, Guid itemId)
    {
        if (!HasContext) return;
        try
        {
            var known = ItemLookups.FirstOrDefault(i => i.Id == itemId)?.BaseUnitId;
            var choices = await unitCatalog.ForAsync(CompanyId, BranchId, itemId, known == Guid.Empty ? null : known, UnitLookups, Token).ConfigureAwait(true);
            if (line.ItemId != itemId || line.UnitAssignedFor != itemId || !Lines.Contains(line)) return;
            var hadUnit = line.UnitId is not null;
            line.ApplyUnits(choices, choices.UnitIds.Select(id => UnitOption(id)).OfType<LookupItem>().ToList());
            if (!hadUnit && line.UnitId is { } filled) line.UnitCode = UnitLookups.FirstOrDefault(u => u.Id == filled)?.Code ?? string.Empty;
            line.ShowUnit(UnitOption(line.UnitId, line.UnitCode));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // No unit is guessed. The row reports the missing unit; clearing and choosing the item again asks once more.
        }
    }

    public void EnsureEntryBuffer(int targetCount = 50)
    {
        if (IsViewMode && Invoice is { Status: not SalesInvoiceStatus.Draft }) return;
        while (Lines.Count < targetCount)
        {
            var blank = SalesInvoiceLineItem.CreateBlankSlot(Lines.Count + 1);
            blank.ApplyDocumentDefaults(DraftWarehouseId, DraftCostCenterId);
            blank.Changed = () => OnLineChanged(blank);
            Lines.Add(blank);
        }
    }

    public void AutoGrowLines(int additional = 10)
    {
        if (!IsEditing) return;
        EnsureEntryBuffer(Lines.Count + additional);
    }

    private void RefreshPriceQuotes()
    {
        if (IsEditing) foreach (var line in Lines) _ = QuotePriceAsync(line);
    }

    /// <summary>
    /// Asks the server price resolver for the line's item, unit, quantity, customer, pattern, currency and date. A found price
    /// fills the line only while the user has not typed a different one; nothing is filled when no price applies.
    /// </summary>
    private async Task QuotePriceAsync(SalesInvoiceLineItem line)
    {
        var generation = ++line.QuoteGeneration;
        if (line.IsPristineBlank || Pricing is null || !IsEditing || !HasContext || line.ItemId is not { } itemId || line.UnitId is not { } unitId || DraftInvoiceDate is not { } date) return;
        if (line.HasManualPrice) { line.PriceSource = Localization["CUR_PriceManual"]; return; }
        line.QuotedPrice = null;
        DraftChanged();
        var token = Token;
        try
        {
            var quote = await Pricing.QuoteAsync(CompanyId, BranchId, itemId, unitId, RequiredInput.AmountText(line.Quantity) ? line.Quantity : "0",
                DateOnly.FromDateTime(date), string.IsNullOrWhiteSpace(DraftCurrencyCode) ? null : DraftCurrencyCode, DraftCustomerId, DraftPatternRevisionId,
                token).ConfigureAwait(true);
            if (token.IsCancellationRequested || generation != line.QuoteGeneration || !IsEditing || !Lines.Contains(line) || line.HasManualPrice) return;
            if (quote.Found == true && quote.Price is { } price)
            {
                line.QuotedPrice = price;
                line.ApplyingQuote = true;
                try { line.UnitPrice = price; }
                finally { line.ApplyingQuote = false; }
                line.PriceSource = string.Format(CultureInfo.CurrentCulture, Localization["CUR_PriceFromList"], quote.PriceListCode, quote.CurrencyCode);
            }
            else
            {
                line.ApplyingQuote = true;
                try { line.UnitPrice = "0.0000"; line.QuotedPrice = null; }
                finally { line.ApplyingQuote = false; }
                line.PriceSource = string.Format(CultureInfo.CurrentCulture, Localization["CUR_PriceNone"], quote.CurrencyCode);
            }
            DraftChanged();
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A quote is a convenience: the user can still enter the price; the server validates on save.
            if (!token.IsCancellationRequested && generation == line.QuoteGeneration) line.PriceSource = Localization["CUR_PriceUnavailable"];
        }
    }

    /// <summary>
    /// Fills the rate from the server register for the chosen currency and date. A rate the user typed is kept; the base
    /// currency is always 1; when no register rate exists the field stays empty and the server rejects a save without one.
    /// </summary>
    private async Task RefreshRateAsync()
    {
        if (!IsEditing || !HasContext) return;
        var generation = ++rateGeneration;
        var currency = DraftCurrencyCode?.Trim().ToUpperInvariant() ?? string.Empty;
        var userTyped = DraftExchangeRate is { Length: > 0 } current && !string.Equals(current, lastAutoRate, StringComparison.Ordinal);
        if (currency.Length == 0)
        {
            if (!userTyped) SetAutoRate(string.Empty);
            RateInfo = Localization["CUR_AutoCurrency"];
            return;
        }
        if (!CurrencyRead.IsAvailable || DraftInvoiceDate is not { } date || !RequiredInput.Currency(currency)) { RateInfo = string.Empty; return; }
        var token = Token;
        var reading = await CurrencyRead.ReadRateAsync(CompanyId, BranchId, currency, DateOnly.FromDateTime(date), token).ConfigureAwait(true);
        if (token.IsCancellationRequested || generation != rateGeneration || !IsEditing) return;
        if (reading is null)
        {
            RateInfo = Localization["CUR_RateUnavailable"];
            return;
        }
        var isBase = string.Equals(currency, reading.BaseCurrencyCode, StringComparison.Ordinal);
        if (reading.Found)
        {
            // The base currency is one by definition, so even a typed value is corrected to what the server states.
            if (!userTyped || isBase) SetAutoRate(reading.Rate ?? string.Empty);
            RateInfo = isBase
                ? Localization["CUR_BaseRate"]
                : userTyped
                    ? string.Format(CultureInfo.CurrentCulture, Localization["CUR_ExplicitRate"], reading.Rate)
                    : string.Format(CultureInfo.CurrentCulture, Localization["CUR_RateFromRegister"], reading.EffectiveDate, reading.Source);
            return;
        }
        RateInfo = Localization["CUR_RateMissing"];
        if (!userTyped) SetAutoRate(string.Empty);
    }

    private void SetAutoRate(string rate)
    {
        // The register rate is the system's value, not an edit: a clean draft stays clean.
        ApplySystemValue(() =>
        {
            lastAutoRate = rate;
            DraftExchangeRate = rate;
        });
    }

    /// <summary>The currency the totals are in: the draft choice, else the stored document currency.</summary>
    public string DisplayCurrency => !string.IsNullOrWhiteSpace(DraftCurrencyCode) ? DraftCurrencyCode : Invoice?.CurrencyCode ?? string.Empty;

    /// <summary>Settlement of a posted credit invoice from the server (receipts, returns, remaining), in the invoice currency.</summary>
    public bool HasSettlement => Invoice?.OutstandingAmount is not null;
    public string SettledDisplay => Invoice?.SettledAmount ?? string.Empty;
    public string ReturnedDisplay => Invoice?.ReturnedAmount ?? string.Empty;
    public string OutstandingDisplay => Invoice?.OutstandingAmount ?? string.Empty;

    public void RecalculateTotals()
    {
        decimal subTotal = 0m;
        decimal discountTotal = 0m;
        decimal taxTotal = 0m;
        decimal additionsTotal = 0m;
        decimal grandTotal = 0m;
        decimal totalQty = 0m;
        RebaseAdjustments();

        foreach (var line in Lines.Where(l => !l.IsPristineBlank))
        {
            var qty = ParseDecimal(line.Quantity);
            var price = ParseDecimal(line.UnitPrice);
            var disc = ParseDecimal(line.DiscountAmount);
            var tax = ParseDecimal(line.TaxAmount);
            var pump = ParseDecimal(line.PumpCost);
            var transp = ParseDecimal(line.TransportCost);
            var lineTot = ParseDecimal(line.LineTotal);

            totalQty += qty;
            subTotal += Math.Round(qty * price, 4, MidpointRounding.AwayFromZero);
            discountTotal += disc;
            taxTotal += tax;
            additionsTotal += (pump + transp);
            grandTotal += lineTot;
        }

        // Document-level rows (the discounts-and-additions table) join the discount and additions totals, as the server stores them.
        var adjustmentDiscounts = Adjustments.Where(a => a is { IsComplete: true, CountsInTotals: true, Kind: SalesInvoiceAdjustmentKind.Discount }).Sum(a => a.AmountValue);
        var adjustmentAdditions = Adjustments.Where(a => a is { IsComplete: true, CountsInTotals: true, Kind: SalesInvoiceAdjustmentKind.Addition }).Sum(a => a.AmountValue);
        discountTotal += adjustmentDiscounts;
        additionsTotal += adjustmentAdditions;
        grandTotal += adjustmentAdditions - adjustmentDiscounts;

        SubTotalDisplay = subTotal.ToString("0.0000", CultureInfo.InvariantCulture);
        DiscountTotalDisplay = discountTotal.ToString("0.0000", CultureInfo.InvariantCulture);
        TaxTotalDisplay = taxTotal.ToString("0.0000", CultureInfo.InvariantCulture);
        AdditionsTotalDisplay = additionsTotal.ToString("0.0000", CultureInfo.InvariantCulture);
        GrandTotalDisplay = grandTotal.ToString("0.0000", CultureInfo.InvariantCulture);
        TotalQuantityDisplay = totalQty.ToString("0.0000", CultureInfo.InvariantCulture);
        OnPropertyChanged(nameof(FirstPaymentRemainingDisplay));
    }

    private static decimal ParseDecimal(string text) =>
        decimal.TryParse(text, NumberStyles.Number | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0m;

    public void AddLine()
    {
        var target = Lines.FirstOrDefault(l => l.IsPristineBlank);
        if (target is null)
        {
            target = SalesInvoiceLineItem.CreateBlankSlot(Lines.Count + 1);
            target.ApplyDocumentDefaults(DraftWarehouseId, DraftCostCenterId);
            target.Changed = () => OnLineChanged(target);
            Lines.Add(target);
        }

        SelectedLine = target;
        RecalculateTotals();
        DraftChanged();
    }

    public void RemoveLine(SalesInvoiceLineItem? line)
    {
        if (!IsEditing || line is null || !Lines.Contains(line)) return;
        Lines.Remove(line);
        if (ReferenceEquals(SelectedLine, line)) SelectedLine = Lines.LastOrDefault();
        RecalculateTotals();
        DraftChanged();
    }

    public override bool SupportsSaveAsDraft => true;

    /// <summary>Stable until a save completes, so an uncertain retry replays the server posting instead of posting twice.</summary>
    private string? pendingSaveKey;
    private string? pendingSaveFingerprint;

    [ObservableProperty] private string saveOutcomeMessage = string.Empty;
    [ObservableProperty] private bool saveOutcomeIsWarning;

    /// <summary>The key is stable for an unchanged payload (an ambiguous retry resolves to the same saved document on the server)
    /// and is renewed when the user changes the document, so a corrected retry never collides with the earlier payload.</summary>
    private string SaveKeyFor(object payload, bool saveAsDraft)
    {
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { payload, saveAsDraft, DraftCustomerId, DraftWarehouseId, DraftInvoiceDate, DraftNotes, DraftSellerText, DraftDistributorText, DraftPaymentMethod, DraftCurrencyCode, DraftExchangeRate })));
        if (pendingSaveKey is null || !string.Equals(fingerprint, pendingSaveFingerprint, StringComparison.Ordinal))
        {
            pendingSaveKey = $"sales-save:{Guid.NewGuid():N}";
            pendingSaveFingerprint = fingerprint;
        }
        return pendingSaveKey;
    }

    /// <summary>Never shows an incomplete workflow (missing grant, failed approve/post) as plain success: states the saved status and next step.</summary>
    private void ApplyOutcome(DocumentProcessingResultDto outcome)
    {
        var incomplete = outcome.Outcome is "ApprovalPermissionMissing" or "PostingPermissionMissing" or "PostingFailed" or "ApprovalFailed";
        SaveOutcomeIsWarning = incomplete;
        SaveOutcomeMessage = !incomplete ? outcome.Message ?? string.Empty
            : $"⚠ {outcome.Outcome}{(string.IsNullOrEmpty(outcome.FailureCode) ? string.Empty : $" [{outcome.FailureCode}]")} — المستند محفوظ بحالة «{outcome.Status}» {outcome.DocumentNumber}. {outcome.Message} الخطوة التالية: عالج السبب أو اطلب الصلاحية ثم أكمل الاعتماد/الترحيل يدوياً من بطاقة المستند.";
    }

    public bool HasSaveOutcome => !string.IsNullOrWhiteSpace(SaveOutcomeMessage);

    partial void OnSaveOutcomeMessageChanged(string value) => OnPropertyChanged(nameof(HasSaveOutcome));

    protected override Task<bool> SaveDraftAsync(EntityCardMode mode) => PersistAsync(mode, saveAsDraft: false);

    protected override Task<bool> SaveDraftOnlyAsync(EntityCardMode mode) => PersistAsync(mode, saveAsDraft: true);

    private async Task<bool> PersistAsync(EntityCardMode mode, bool saveAsDraft)
    {
        if (!HasContext || !CanSaveDraft || !DraftWarehouseId.HasValue) return false;
        if (!await DocumentDateGuard.AllowsAsync(DraftInvoiceDate, Context, Localization, confirmation, message => ValidationMessage = message).ConfigureAwait(true)) return false;
        if (!await EntrySaveGuard.AllowsAsync(Lines.Where(l => l.IsComplete).Select(l => l.ItemId), confirmation, isNew: mode == EntityCardMode.New).ConfigureAwait(true)) return false;
        var hasPriceChange = Lines.Where(l => l.IsComplete).Any(l => l.HasManualPrice && !string.IsNullOrWhiteSpace(l.QuotedPrice) && l.UnitPrice != l.QuotedPrice);
        if (!await EntrySaveGuard.AllowsPriceAsync(isBelowCost: false, isChangedFromQuote: hasPriceChange, confirmation).ConfigureAwait(true)) return false;
        if (HasIncompleteAdjustment) { ValidationMessage = Localization["D_AdjustmentIncomplete"]; return false; }
        var adjustmentRequests = BuildAdjustmentRequests();
        if (FirstPaymentExceedsTotal) { ValidationMessage = Localization["D_FirstPaymentExceeds"]; return false; }
        var firstPayment = mode == EntityCardMode.New ? CaptureFirstPayment() : null;

        var lineRequests = Lines
                .Where(l => l.IsComplete)
                .Select(l => new CreateSalesInvoiceLineRequest
                {
                    ItemId = l.ItemId!.Value,
                    UnitId = l.UnitId!.Value,
                    Quantity = l.Quantity,
                    UnitPrice = l.UnitPrice,
                    DiscountPercentage = l.EffectiveDiscountPercentageText,
                    DiscountAmount = l.DiscountAmount,
                    TaxPercentage = l.TaxPercentage,
                    TaxAmount = l.TaxAmount,
                    PumpCost = l.PumpCost,
                    TransportCost = l.TransportCost,
                    CostCenterId = l.CostCenterId ?? DraftCostCenterId,
                    WarehouseId = l.WarehouseId ?? DraftWarehouseId,
                    Notes = !string.IsNullOrWhiteSpace(l.Notes) ? l.Notes : (l.DiscountPercentage.Contains('+') ? $"خصم متسلسل: {l.DiscountPercentage}" : null),
                    ConcreteGrade = l.ConcreteGrade,
                }).ToList();

        if (mode == EntityCardMode.New)
        {
            if (!DraftPatternRevisionId.HasValue) return false;

                var createReq = new CreateSalesInvoiceRequest
                {
                    PatternRevisionId = DraftPatternRevisionId.Value,
                    CustomerId = DraftCustomerId,
                    WarehouseId = DraftWarehouseId.Value,
                    CostCenterId = DraftCostCenterId,
                    CustomerAccountId = CustomerAccountOverride,
                    InvoiceDateUtc = new DateTimeOffset(DateTime.SpecifyKind(DraftInvoiceDate!.Value.Date, DateTimeKind.Unspecified), TimeSpan.Zero),
                    PaymentMethod = DraftPaymentMethod,
                    CurrencyCode = string.IsNullOrWhiteSpace(DraftCurrencyCode) ? null : DraftCurrencyCode.Trim().ToUpperInvariant(),
                    ExchangeRate = string.IsNullOrWhiteSpace(DraftExchangeRate) ? null : DraftExchangeRate.Trim(),
                    Notes = DraftNotes,
                    SellerCode = ParseCode(DraftSellerText),
                    DistributorCode = ParseCode(DraftDistributorText),
                    Lines = lineRequests,
                    Adjustments = adjustmentRequests,
                };

                var outcome = await salesInvoiceClient.SaveAndProcessSalesInvoiceAsync(CompanyId, BranchId,
                    new SaveAndProcessSalesInvoiceRequest
                    {
                        Document = createReq,
                        SaveAsDraft = saveAsDraft,
                        IdempotencyKey = SaveKeyFor(new { lineRequests, adjustmentRequests }, saveAsDraft),
                        ManualNumber = string.IsNullOrWhiteSpace(ManualDocumentNumber) ? null : ManualDocumentNumber.Trim(),
                        ConfirmCreditWarning = ConfirmCreditWarning,
                        OverrideCreditPolicy = OverrideCreditPolicy,
                        CreditOverrideReason = string.IsNullOrWhiteSpace(CreditOverrideReason) ? null : CreditOverrideReason.Trim(),
                        NegativeStockReason = string.IsNullOrWhiteSpace(NegativeStockReason) ? null : NegativeStockReason.Trim(),
                    }, Token).ConfigureAwait(true);
                pendingSaveKey = null;
                ApplyOutcome(outcome);
                InvoiceId = outcome.DocumentId;
                ResetFirstPayment();
                ResetMode();
                await LoadInvoiceAsync(Token).ConfigureAwait(true);
                await RegisterFirstPaymentAsync(outcome, firstPayment).ConfigureAwait(true);
                return true;
        }

        if (mode == EntityCardMode.Edit && InvoiceId is { } existingId)
        {
                var updateReq = new UpdateSalesInvoiceRequest
                {
                    // The version read with the draft; a newer server change returns 409 and keeps this draft.
                    ExpectedVersion = Invoice?.Version ?? string.Empty,
                    CustomerId = DraftCustomerId,
                    WarehouseId = DraftWarehouseId.Value,
                    CostCenterId = DraftCostCenterId,
                    CustomerAccountId = CustomerAccountOverride,
                    InvoiceDateUtc = new DateTimeOffset(DateTime.SpecifyKind(DraftInvoiceDate!.Value.Date, DateTimeKind.Unspecified), TimeSpan.Zero),
                    PaymentMethod = DraftPaymentMethod,
                    CurrencyCode = string.IsNullOrWhiteSpace(DraftCurrencyCode) ? null : DraftCurrencyCode.Trim().ToUpperInvariant(),
                    ExchangeRate = string.IsNullOrWhiteSpace(DraftExchangeRate) ? null : DraftExchangeRate.Trim(),
                    Notes = DraftNotes,
                    SellerCode = ParseCode(DraftSellerText),
                    DistributorCode = ParseCode(DraftDistributorText),
                    Lines = lineRequests,
                    Adjustments = adjustmentRequests,
                };

                var outcome = await salesInvoiceClient.SaveAndProcessUpdateSalesInvoiceAsync(CompanyId, BranchId, existingId,
                    new SaveAndProcessSalesInvoiceRequest
                    {
                        Document = new CreateSalesInvoiceRequest
                        {
                            CustomerId = updateReq.CustomerId,
                            WarehouseId = updateReq.WarehouseId,
                            InvoiceDateUtc = updateReq.InvoiceDateUtc,
                            PaymentMethod = updateReq.PaymentMethod,
                            CurrencyCode = updateReq.CurrencyCode,
                            ExchangeRate = updateReq.ExchangeRate,
                            CostCenterId = updateReq.CostCenterId,
                            CustomerAccountId = updateReq.CustomerAccountId,
                            Notes = updateReq.Notes,
                            SellerCode = updateReq.SellerCode,
                            DistributorCode = updateReq.DistributorCode,
                            Lines = updateReq.Lines,
                            Adjustments = updateReq.Adjustments,
                        },
                        InvoiceId = existingId,
                        ExpectedVersion = updateReq.ExpectedVersion,
                        SaveAsDraft = saveAsDraft,
                        IdempotencyKey = SaveKeyFor(new { lineRequests, adjustmentRequests }, saveAsDraft),
                        ManualNumber = string.IsNullOrWhiteSpace(ManualDocumentNumber) ? null : ManualDocumentNumber.Trim(),
                        ConfirmCreditWarning = ConfirmCreditWarning,
                        OverrideCreditPolicy = OverrideCreditPolicy,
                        CreditOverrideReason = string.IsNullOrWhiteSpace(CreditOverrideReason) ? null : CreditOverrideReason.Trim(),
                        NegativeStockReason = string.IsNullOrWhiteSpace(NegativeStockReason) ? null : NegativeStockReason.Trim(),
                    }, Token).ConfigureAwait(true);
                pendingSaveKey = null;
                ApplyOutcome(outcome);
                ResetMode();
                await LoadInvoiceAsync(Token).ConfigureAwait(true);
                return true;
        }

        return false;
    }

    protected override async Task LoadAsync(object? parameter, CancellationToken cancellationToken)
    {
        NotifyCardCommands();

        if (parameter is OpenSalesInvoiceCardRequest open)
        {
            navigationScope = open.Scope;
            InvoiceId = open.InvoiceId;
            ResetMode();
            await LoadLookupsAsync(cancellationToken).ConfigureAwait(true);
            await LoadInvoiceAsync(cancellationToken).ConfigureAwait(true);
        }
        else if (parameter is NewSalesInvoiceCardRequest newRequest)
        {
            requestedPatternId = newRequest.PatternId;
            navigationScope = null;
            InvoiceId = null;
            Invoice = null;
            await LoadLookupsAsync(cancellationToken).ConfigureAwait(true);
            EnterNew();
        }
    }

    private void EnterNew()
    {
        ConfirmCreditWarning = false;
        OverrideCreditPolicy = false;
        CreditOverrideReason = string.Empty;
        NegativeStockReason = string.Empty;
        LoadDraft(EntityCardMode.New);
        var prop = typeof(EntityCardViewModel).GetProperty(nameof(Mode));
        prop?.SetValue(this, EntityCardMode.New);
        OnPropertyChanged(nameof(IsPatternLocked)); OnPropertyChanged(nameof(IsPatternPickerEnabled));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(Lifecycle)); OnPropertyChanged(nameof(HasDocumentStatus)); OnPropertyChanged(nameof(DocumentStatusKind));
        NotifyCardCommands();
    }

    public async Task LoadLookupsAsync(CancellationToken cancellationToken)
    {
        if (!HasContext) return;

        LookupsError = null;
        try
        {
            // These are independent reads. Awaiting them one after another is what made the card refill itself line by
            // line; starting them together leaves the screen waiting on the slowest call only.
            var patternsTask = PatternShortcutLoader.LoadAsync(patternsClient, CompanyId, BranchId,
                k => k == BusinessOperationKind.SalesInvoice || k == BusinessOperationKind.SalesReturn, cancellationToken);
            var customersTask = partiesClient.LookupPartiesAsync(CompanyId, BranchId, 200, null, PartyRole.Customer, cancellationToken);
            var warehousesTask = inventoryClient.ListWarehousesAsync(CompanyId, BranchId, null, cancellationToken);
            var itemsTask = inventoryClient.LookupItemsAsync(CompanyId, BranchId, 500, null, cancellationToken);
            var unitsTask = inventoryClient.ListUnitsAsync(CompanyId, BranchId, cancellationToken);
            var settingsTask = Currencies?.GetSettingsAsync(CompanyId, BranchId, cancellationToken);
            var costCentersTask = accountsClient.SearchCostCentersAsync(CompanyId, BranchId, null, true, 1, 100, cancellationToken);

            var patterns = await patternsTask.ConfigureAwait(true);
            PatternLookups.Clear();
            foreach (var pattern in patterns)
                if (pattern is not null) PatternLookups.Add(pattern);
                OnPropertyChanged(nameof(FieldCaptions));

            var customers = await customersTask.ConfigureAwait(true);
            CustomerLookups.Clear();
            foreach (var c in customers) CustomerLookups.Add(c);

            var warehouses = await warehousesTask.ConfigureAwait(true);
            WarehouseLookups.Clear();
            foreach (var w in warehouses) WarehouseLookups.Add(w);

            var items = await itemsTask.ConfigureAwait(true);
            ItemLookups.Clear();
            foreach (var item in items) ItemLookups.Add(item);

            var units = await unitsTask.ConfigureAwait(true);
            UnitLookups.Clear(); unitCatalog.Clear();
            foreach (var u in units) UnitLookups.Add(u);

            CurrencyOptions.Clear();
            CurrencyOptions.Add(string.Empty);
            if (settingsTask is not null)
            {
                var settings = await settingsTask.ConfigureAwait(true);
                foreach (var currency in settings.Currencies ?? [])
                    if (currency.Code is { Length: 3 } code && currency.IsActive == true) CurrencyOptions.Add(code);
            }

            await LoadAdjustmentAccountsAsync(cancellationToken).ConfigureAwait(true);
            var costCentersResponse = await costCentersTask.ConfigureAwait(true);
            CostCenterLookups.Clear();
            if (costCentersResponse.Items is not null)
            {
                foreach (var cc in costCentersResponse.Items) CostCenterLookups.Add(cc);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Lists may be partly filled. Say so, with the server's correlation id when there is one, instead of showing a card that
            // looks complete; LoadLookupsAsync is the retry.
            LookupsError = exception is PetraERP.ApiClient.PetraApiException api
                ? (PetraERP.Desktop.Connection.DesktopErrors.Diagnostics(api) ?? api.Message)
                : exception.Message;
        }
    }

    public async Task LoadInvoiceAsync(CancellationToken cancellationToken)
    {
        if (!HasContext || InvoiceId is null) return;

        var detail = await salesInvoiceClient.GetSalesInvoiceAsync(CompanyId, BranchId, InvoiceId.Value, cancellationToken).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        ApplyLoadedInvoice(detail);
    }

    private void ApplyLoadedInvoice(SalesInvoiceDetailDto detail)
    {
        Invoice = detail;
        ManualDocumentNumber = string.Empty; OnPropertyChanged(nameof(CanUseManualNumber));
        NumberPreview = null;
        ConfirmCreditWarning = false;
        OverrideCreditPolicy = false;
        CreditOverrideReason = string.Empty;
        DocumentLinks?.SetSource(Context, SalesLinkSource.Invoice, InvoiceId);
        ReceiptLinks?.SetSource(IsViewMode ? Context : null, Relationships.RelatedDocumentSource.InvoiceReceipts, detail.Id);

        SubTotalDisplay = detail.SubTotal ?? "0.0000";
        DiscountTotalDisplay = detail.DiscountTotal ?? "0.0000";
        TaxTotalDisplay = detail.TaxTotal ?? "0.0000";
        AdditionsTotalDisplay = detail.AdditionsTotal ?? "0.0000";
        GrandTotalDisplay = detail.GrandTotal ?? "0.0000";

        PopulateLinesFromInvoice(detail);
        RecalculateTotals();
        PrepareReturnLines(detail);
        RateInfo = string.Empty;
        OnPropertyChanged(nameof(DisplayCurrency));
        OnPropertyChanged(nameof(HasSettlement));
        OnPropertyChanged(nameof(SettledDisplay));
        OnPropertyChanged(nameof(ReturnedDisplay));
        OnPropertyChanged(nameof(OutstandingDisplay));

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(Lifecycle)); OnPropertyChanged(nameof(HasDocumentStatus)); OnPropertyChanged(nameof(DocumentStatusKind));
        OnPropertyChanged(nameof(HasInvoice));
        OnPropertyChanged(nameof(IsInvoiceDraft));
        OnPropertyChanged(nameof(IsInvoiceApproved));
        OnPropertyChanged(nameof(IsInvoicePosted));
        OnPropertyChanged(nameof(IsInvoiceReversed));
        OnPropertyChanged(nameof(IsInvoiceCancelled));
        OnPropertyChanged(nameof(IsCreditInvoice));
        OnPropertyChanged(nameof(CreditPolicyDisplay));
        OnPropertyChanged(nameof(CreditAuditDisplay));
        OnPropertyChanged(nameof(CanOverrideCredit));
        OnPropertyChanged(nameof(CanPrepareCreditAction));
        OnPropertyChanged(nameof(CanPost));
        OnPropertyChanged(nameof(CustomerFieldLabel));
        NotifyCardCommands();
        NotifyLinkCommands();
    }

    private async Task ApproveInvoiceAsync()
    {
        if (InvoiceId is null || Invoice?.Version is not { Length: > 0 } version || !HasContext || !CanApprove) return;

        if (!await confirmation.ConfirmAsync("D_ConfirmApproveInvoice").ConfigureAwait(true)) return;

        // One submit at a time, localized errors (never raw server text), and "outcome unknown" rather than "failed" when
        // the server could not confirm the result. The card stays open so a retry reuses the same request key.
        var targetInvoiceId = InvoiceId.Value;
        await RunDocumentAsync(async () =>
        {
            var approved = await salesInvoiceClient.ApproveSalesInvoiceAsync(CompanyId, BranchId, targetInvoiceId, version, CancellationToken.None).ConfigureAwait(true);
            Invoice = approved;
            ResetMode();
            await LoadInvoiceAsync(CancellationToken.None).ConfigureAwait(true);
        }, OperationKind.Save).ConfigureAwait(true);
    }

    private async Task PostInvoiceAsync()
    {
        if (InvoiceId is null || !HasContext || !CanPost) return;

        if (!await confirmation.ConfirmAsync("D_ConfirmPostInvoice").ConfigureAwait(true)) return;

        // One submit at a time, localized errors (never raw server text), and "outcome unknown" rather than "failed" when
        // the server could not confirm the result. The card stays open so a retry reuses the same request key.
        var targetInvoiceId = InvoiceId.Value;
        await RunDocumentAsync(async () =>
        {
            var postReq = new PostSalesInvoiceRequest
            {
                IdempotencyKey = $"sales-post:{targetInvoiceId:N}:{Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { Invoice, Reason = ActionReason?.Trim(), ConfirmCreditWarning, OverrideCreditPolicy, CreditReason = CreditOverrideReason?.Trim(), NegativeReason = NegativeStockReason?.Trim(), ManualNumber = ManualDocumentNumber?.Trim(), Client = postingClientId })))[..32]}",
                ManualNumber = string.IsNullOrWhiteSpace(ManualDocumentNumber) ? null : ManualDocumentNumber.Trim(),
                Notes = string.IsNullOrWhiteSpace(ActionReason) ? null : ActionReason,
                ConfirmCreditWarning = ConfirmCreditWarning,
                OverrideCreditPolicy = OverrideCreditPolicy,
                CreditOverrideReason = string.IsNullOrWhiteSpace(CreditOverrideReason) ? null : CreditOverrideReason.Trim(),
                NegativeStockReason = string.IsNullOrWhiteSpace(NegativeStockReason) ? null : NegativeStockReason.Trim(),
            };
            var posted = await salesInvoiceClient.PostSalesInvoiceAsync(CompanyId, BranchId, targetInvoiceId, postReq, CancellationToken.None).ConfigureAwait(true);
            Invoice = posted;
            ResetMode();
            await LoadInvoiceAsync(CancellationToken.None).ConfigureAwait(true);
        }, OperationKind.Post).ConfigureAwait(true);
    }

    private async Task ReverseInvoiceAsync()
    {
        if (InvoiceId is null || !HasContext || !CanReverse) return;

        if (!await confirmation.ConfirmAsync("D_ConfirmReverseInvoice").ConfigureAwait(true)) return;

        // One submit at a time, localized errors (never raw server text), and "outcome unknown" rather than "failed" when
        // the server could not confirm the result. The card stays open so a retry reuses the same request key.
        var targetInvoiceId = InvoiceId.Value;
        await RunDocumentAsync(async () =>
        {
            var revReq = new ReverseSalesInvoiceRequest
            {
                Reason = string.IsNullOrWhiteSpace(ActionReason) ? "عكس فاتورة مبيعات" : ActionReason,
            };
            var reversed = await salesInvoiceClient.ReverseSalesInvoiceAsync(CompanyId, BranchId, targetInvoiceId, revReq, CancellationToken.None).ConfigureAwait(true);
            Invoice = reversed;
            ResetMode();
            await LoadInvoiceAsync(CancellationToken.None).ConfigureAwait(true);
        }, OperationKind.Post).ConfigureAwait(true);
    }

    private async Task DeleteDraftInvoiceAsync()
    {
        if (InvoiceId is null || Invoice?.Version is not { Length: > 0 } version || !HasContext || !CanDeleteDraft) return;

        if (!await confirmation.ConfirmAsync("D_ConfirmDeleteDraft").ConfigureAwait(true)) return;

        var targetInvoiceId = InvoiceId.Value;
        await RunDocumentAsync(async () =>
        {
            await salesInvoiceClient.DeleteSalesInvoiceAsync(CompanyId, BranchId, targetInvoiceId, version, CancellationToken.None).ConfigureAwait(true);
            ClearPage();
            if (BeginNewCommand.CanExecute(null)) BeginNewCommand.Execute(null);
        }, OperationKind.Save).ConfigureAwait(true);
    }

    protected override void ClearPage()
    {
        Invoice = null;
        InvoiceId = null;
        ConfirmCreditWarning = false;
        OverrideCreditPolicy = false;
        CreditOverrideReason = string.Empty;
        NegativeStockReason = string.Empty;
        DocumentLinks?.SetSource(null, SalesLinkSource.Invoice, null);
        ReceiptLinks?.SetSource(null, Relationships.RelatedDocumentSource.InvoiceReceipts, null);
        Lines.Clear();
        Adjustments.Clear();
        AdjustmentAccounts.Clear();
        ReturnLines.Clear();
        PatternLookups.Clear();
        CustomerLookups.Clear();
        WarehouseLookups.Clear();
        ItemLookups.Clear();
        UnitLookups.Clear(); unitCatalog.Clear();
        CostCenterLookups.Clear();
        CurrencyOptions.Clear();
        RateInfo = string.Empty;
        lastAutoRate = null;
        ResetMode();
    }

    protected override void NotifyCommands()
    {
        OnPropertyChanged(nameof(CanUseManualNumber));
        DocumentLinks?.SetSource(Context, SalesLinkSource.Invoice, InvoiceId);
        ReceiptLinks?.SetSource(IsViewMode ? Context : null, Relationships.RelatedDocumentSource.InvoiceReceipts, Invoice?.Id);
        NotifyLinkCommands();
        base.NotifyCommands();
        RefreshCommand.NotifyCanExecuteChanged();
        AddLineCommand.NotifyCanExecuteChanged();
        RemoveLineCommand.NotifyCanExecuteChanged();
        UseListPriceCommand.NotifyCanExecuteChanged();
        ApproveCommand.NotifyCanExecuteChanged();
        PostCommand.NotifyCanExecuteChanged();
        ReverseCommand.NotifyCanExecuteChanged();
        DeleteDraftCommand.NotifyCanExecuteChanged();
        postReturnCommand?.NotifyCanExecuteChanged();
        FirstCommand?.NotifyCanExecuteChanged();
        PreviousCommand?.NotifyCanExecuteChanged();
        NextCommand?.NotifyCanExecuteChanged();
        LastCommand?.NotifyCanExecuteChanged();
        AutoGrowCommand?.NotifyCanExecuteChanged();
        NotifyCardCommands();
    }
}

public sealed class SalesInvoiceCardFactory(
    ISalesInvoiceClient salesInvoiceClient,
    IPartiesClient partiesClient,
    IInventoryClient inventoryClient,
    IPatternsClient patternsClient,
    IChartOfAccountsClient accountsClient,
    ShellNavigator navigator,
    DesktopOperation operation,
    ILocalizationService localization,
    IUserConfirmation confirmation,
    SalesDocumentLinksFactory links,
    Relationships.RelatedDocumentsFactory relations,
    ICurrenciesClient? currencies = null,
    IPricingClient? pricing = null,
    FirstPaymentRegistrar? firstPayment = null)
{
    public SalesInvoiceCardViewModel Create() => new(
        salesInvoiceClient,
        partiesClient,
        inventoryClient,
        patternsClient,
        accountsClient,
        navigator,
        operation,
        localization,
        confirmation) { DocumentLinks = links.Create(), ReceiptLinks = relations.Create(), Currencies = currencies, Pricing = pricing, FirstPayment = firstPayment };
}
