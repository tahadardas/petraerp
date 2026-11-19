using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Accounting;
using PetraERP.Desktop.Controls;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Patterns;

public sealed partial class PatternCardViewModel : EntityCardViewModel
{
    private readonly IPatternsClient patternsClient;
    private readonly IChartOfAccountsClient accountsClient;
    private readonly ShellNavigator navigator;
    private readonly IUserConfirmation confirmation;

    [ObservableProperty] private DocumentPatternResponse? pattern;
    [ObservableProperty] private Guid? patternId;

    // --- Draft fields: General ---
    [ObservableProperty] private string draftCode = string.Empty;
    [ObservableProperty] private string draftShortCode = string.Empty;
    [ObservableProperty] private string draftNameArabic = string.Empty;
    [ObservableProperty] private string? draftNameEnglish;
    [ObservableProperty] private BusinessOperationKind draftOperationKind = BusinessOperationKind.ManualJournal;
    [ObservableProperty] private string? draftGroup;
    [ObservableProperty] private string? draftIcon;
    [ObservableProperty] private bool draftIsActive = true;
    [ObservableProperty] private bool draftIsDefault;
    [ObservableProperty] private ObservableCollection<Guid> draftBranchIds = [];

    // --- Draft fields: Accounting Roles ---
    [ObservableProperty] private Guid? draftSalesAccountId;
    [ObservableProperty] private bool draftSalesEnforced;
    [ObservableProperty] private Guid? draftInventoryAccountId;
    [ObservableProperty] private bool draftInventoryEnforced;
    [ObservableProperty] private Guid? draftCogsAccountId;
    [ObservableProperty] private bool draftCogsEnforced;
    [ObservableProperty] private Guid? draftCustomerAccountId;
    [ObservableProperty] private bool draftCustomerEnforced;
    [ObservableProperty] private Guid? draftSupplierAccountId;
    [ObservableProperty] private bool draftSupplierEnforced;
    [ObservableProperty] private Guid? draftCashAccountId;
    [ObservableProperty] private bool draftCashEnforced;
    [ObservableProperty] private Guid? draftDiscountAccountId;
    [ObservableProperty] private bool draftDiscountEnforced;
    [ObservableProperty] private Guid? draftTaxAccountId;
    [ObservableProperty] private bool draftTaxEnforced;
    [ObservableProperty] private Guid? draftBankAccountId;
    [ObservableProperty] private bool draftBankEnforced;
    [ObservableProperty] private Guid? draftTransportRevenueAccountId;
    [ObservableProperty] private bool draftTransportRevenueEnforced;
    [ObservableProperty] private Guid? draftOperatingAccountId;
    [ObservableProperty] private bool draftOperatingAccountEnforced;
    [ObservableProperty] private Guid? draftAssemblyExpenseAccountId;
    [ObservableProperty] private bool draftAssemblyExpenseEnforced;
    [ObservableProperty] private Guid? draftVarianceAccountId;
    [ObservableProperty] private bool draftVarianceEnforced;

    /// <summary>Server mappings for roles this card does not edit; re-sent unchanged so saving never deletes them.</summary>
    private readonly List<PatternAccountMappingDto> preservedMappings = [];

    // --- Draft fields: Defaults ---
    [ObservableProperty] private Guid? draftDefaultBranchId;
    [ObservableProperty] private string? draftDefaultWarehouse;
    [ObservableProperty] private Guid? draftDefaultCostCenterId;
    [ObservableProperty] private string? draftDefaultCurrencyCode;
    [ObservableProperty] private string? draftDefaultPaymentMethod;

    // --- Draft fields: Line Fields ---
    public ObservableCollection<PatternFieldRuleDto> DraftFieldRules { get; } = [];

    // --- Draft fields: Approvals & Numbering ---
    [ObservableProperty] private bool draftRequiresApproval;
    [ObservableProperty] private string? draftThresholdAmount;
    [ObservableProperty] private string? draftApprovalRole;
    [ObservableProperty] private PatternNumberingScope draftNumberingScope = PatternNumberingScope.Branch;
    [ObservableProperty] private string? draftPrefix;
    [ObservableProperty] private string? draftSuffix;
    [ObservableProperty] private long draftInitialSequence = 1;
    [ObservableProperty] private bool draftAllowManualNumbering;
    /// <summary>Al-Ameen "تثبيت تاريخ إضافة السند": documents of this pattern carry the day they are added and the date cannot be edited.</summary>
    [ObservableProperty] private bool draftLockDocumentDate;
    partial void OnDraftLockDocumentDateChanged(bool value) => DraftChanged();
    [ObservableProperty] private int draftMinimumDigits = 5;
    private PatternConfigurationDto? preservedConfiguration;
    public IReadOnlyList<string> NumberResetPolicies { get; } = ["Never", "Monthly"];
    partial void OnDraftInitialSequenceChanged(long value) => DraftChanged();
    partial void OnDraftAllowManualNumberingChanged(bool value) => DraftChanged();
    [ObservableProperty] private string draftResetPeriodically = "Never";

    // --- Draft fields: Print Template ---
    [ObservableProperty] private string? draftPrintTemplateId;

    // Revisions list
    public ObservableCollection<PatternRevisionResponse> Revisions { get; } = [];
    [ObservableProperty] private PatternRevisionResponse? selectedRevision;
    [ObservableProperty] private string revisionDiffText = string.Empty;

    // Lookups
    public ObservableCollection<LookupItem> AccountLookups { get; } = [];
    public ObservableCollection<LookupItem> CostCenterLookups { get; } = [];

    public PatternCardViewModel(
        IPatternsClient patternsClient,
        IChartOfAccountsClient accountsClient,
        ShellNavigator navigator,
        DesktopOperation operation,
        ILocalizationService localization,
        IUserConfirmation confirmation)
        : base(operation, localization, confirmation)
    {
        this.patternsClient = patternsClient;
        this.accountsClient = accountsClient;
        this.navigator = navigator;
        this.confirmation = confirmation;

        RefreshCommand = new AsyncRelayCommand(
            () => IsViewMode ? RunAsync(LoadPatternAsync) : Task.CompletedTask,
            () => HasContext && Idle && PatternId is not null && IsViewMode);

        PublishRevisionCommand = new AsyncRelayCommand(PublishActiveRevisionAsync, () => HasContext && Idle && CanPublish && HasActiveRevision);
        RetireCommand = new AsyncRelayCommand(RetirePatternAsync, () => HasContext && Idle && CanPublish && HasPattern && (Pattern?.IsActive == true));
        CreateDraftRevisionCommand = new AsyncRelayCommand(CreateNewRevisionAsync, () => HasContext && Idle && CanManage && HasPattern && RequiredAccountsComplete);

        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName?.StartsWith("Draft", StringComparison.Ordinal) == true) CreateDraftRevisionCommand.NotifyCanExecuteChanged();
            if (args.PropertyName == nameof(Mode) && IsViewMode && Pattern is not null)
            {
                LoadDraft(EntityCardMode.View);
            }
        };
    }

    protected override string RequiredPermission => DocumentPatternsViewModel.ViewPermission;
    public bool CanManage => Can(DocumentPatternsViewModel.ManagePermission);
    public bool CanPublish => Can(DocumentPatternsViewModel.PublishPermission);

    public bool HasPattern => Pattern is not null;
    public bool HasActiveRevision => Pattern?.ActiveRevision is not null;
    public string StatusText => Pattern is null ? string.Empty : ((Pattern.IsActive ?? false) ? Localization["UX_Active"] : Localization["UX_Inactive"]);
    public BadgeKind StatusKind => Pattern is null ? BadgeKind.Neutral : ((Pattern.IsActive ?? false) ? BadgeKind.Success : BadgeKind.Neutral);
    public string ModeLabel => Localization["D_CardMode_" + Mode];
    public string Title => Pattern is null ? Localization["D_NewPattern"] : $"{Localization["D_Pattern"]}: {Pattern.NameArabic} ({Pattern.Code})";

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand PublishRevisionCommand { get; }
    public AsyncRelayCommand RetireCommand { get; }
    public AsyncRelayCommand CreateDraftRevisionCommand { get; }

    public IReadOnlyList<ChoiceOption<BusinessOperationKind>> OperationKinds =>
        Options.For<BusinessOperationKind>(Localization, "BusinessOperationKind_");

    public IReadOnlyList<ChoiceOption<PatternNumberingScope>> NumberingScopes =>
        Options.For<PatternNumberingScope>(Localization, "PatternNumberingScope_");

    public bool IsCodeReadOnly => !IsNewMode;

    partial void OnDraftCodeChanged(string value) => DraftChanged();
    partial void OnDraftShortCodeChanged(string value) => DraftChanged();
    partial void OnDraftNameArabicChanged(string value) => DraftChanged();
    partial void OnDraftNameEnglishChanged(string? value) => DraftChanged();
    partial void OnDraftOperationKindChanged(BusinessOperationKind value)
    {
        DraftChanged();
        NotifyVoucherKind();
        if (IsNewMode && Pattern is null)
        {
            ApplyDefaultPatternSettingsForKind(value);
        }
    }
    partial void OnDraftGroupChanged(string? value) => DraftChanged();
    partial void OnDraftIconChanged(string? value) => DraftChanged();
    partial void OnDraftIsActiveChanged(bool value) => DraftChanged();
    partial void OnDraftIsDefaultChanged(bool value) => DraftChanged();
    partial void OnDraftSalesAccountIdChanged(Guid? value) => DraftChanged();
    partial void OnDraftSalesEnforcedChanged(bool value) => DraftChanged();
    partial void OnDraftInventoryAccountIdChanged(Guid? value) => DraftChanged();
    partial void OnDraftInventoryEnforcedChanged(bool value) => DraftChanged();
    partial void OnDraftCogsAccountIdChanged(Guid? value) => DraftChanged();
    partial void OnDraftCogsEnforcedChanged(bool value) => DraftChanged();
    partial void OnDraftCustomerAccountIdChanged(Guid? value) => DraftChanged();
    partial void OnDraftCustomerEnforcedChanged(bool value) => DraftChanged();
    partial void OnDraftSupplierAccountIdChanged(Guid? value) => DraftChanged();
    partial void OnDraftSupplierEnforcedChanged(bool value) => DraftChanged();
    partial void OnDraftCashAccountIdChanged(Guid? value) => DraftChanged();
    partial void OnDraftCashEnforcedChanged(bool value) => DraftChanged();
    partial void OnDraftDiscountAccountIdChanged(Guid? value) => DraftChanged();
    partial void OnDraftDiscountEnforcedChanged(bool value) => DraftChanged();
    partial void OnDraftTaxAccountIdChanged(Guid? value) => DraftChanged();
    partial void OnDraftTaxEnforcedChanged(bool value) => DraftChanged();
    partial void OnDraftBankAccountIdChanged(Guid? value) => DraftChanged();
    partial void OnDraftBankEnforcedChanged(bool value) => DraftChanged();
    partial void OnDraftTransportRevenueAccountIdChanged(Guid? value) => DraftChanged();
    partial void OnDraftTransportRevenueEnforcedChanged(bool value) => DraftChanged();
    partial void OnDraftOperatingAccountIdChanged(Guid? value) => DraftChanged();
    partial void OnDraftOperatingAccountEnforcedChanged(bool value) => DraftChanged();
    partial void OnDraftAssemblyExpenseAccountIdChanged(Guid? value) => DraftChanged();
    partial void OnDraftAssemblyExpenseEnforcedChanged(bool value) => DraftChanged();
    partial void OnDraftVarianceAccountIdChanged(Guid? value) => DraftChanged();
    partial void OnDraftVarianceEnforcedChanged(bool value) => DraftChanged();
    partial void OnDraftDefaultCostCenterIdChanged(Guid? value) => DraftChanged();
    partial void OnDraftDefaultWarehouseChanged(string? value) => DraftChanged();
    partial void OnDraftDefaultCurrencyCodeChanged(string? value) => DraftChanged();
    partial void OnDraftDefaultPaymentMethodChanged(string? value) => DraftChanged();
    partial void OnDraftRequiresApprovalChanged(bool value) => DraftChanged();
    partial void OnDraftThresholdAmountChanged(string? value) => DraftChanged();
    partial void OnDraftApprovalRoleChanged(string? value) => DraftChanged();
    partial void OnDraftNumberingScopeChanged(PatternNumberingScope value) => DraftChanged();
    partial void OnDraftPrefixChanged(string? value) => DraftChanged();
    partial void OnDraftSuffixChanged(string? value) => DraftChanged();
    partial void OnDraftMinimumDigitsChanged(int value) => DraftChanged();
    partial void OnDraftResetPeriodicallyChanged(string value) => DraftChanged();
    partial void OnDraftPrintTemplateIdChanged(string? value) => DraftChanged();

    protected override bool CanEditRecord => CanManage && Pattern is not null;
    protected override bool CanCreateRecord => CanManage;
    public override bool CanSaveDraft =>
        !string.IsNullOrWhiteSpace(DraftCode)
        && !string.IsNullOrWhiteSpace(DraftShortCode)
        && !string.IsNullOrWhiteSpace(DraftNameArabic) && RequiredAccountsComplete;

    public override IReadOnlyList<string> GetValidationErrors()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(DraftCode))
            errors.Add(Localization["D_Code"] + ": " + Localization["D_RequiredField"]);
        if (string.IsNullOrWhiteSpace(DraftShortCode))
            errors.Add(Localization["D_ShortCode"] + ": " + Localization["D_RequiredField"]);
        if (string.IsNullOrWhiteSpace(DraftNameArabic))
            errors.Add(Localization["D_NameArabic"] + ": " + Localization["D_RequiredField"]);
        if (!RequiredAccountsComplete)
            errors.Add(Localization["PAT_AccountMappings"] + ": " + Localization["D_RequiredFieldsIncomplete"]);
        return errors;
    }

    protected override string? GetValidationFocusTarget() => string.IsNullOrWhiteSpace(DraftCode)
        ? "code"
        : string.IsNullOrWhiteSpace(DraftShortCode) ? "short-code"
        : string.IsNullOrWhiteSpace(DraftNameArabic) ? "name-ar"
        : !RequiredAccountsComplete ? "account-mappings" : null;

    private bool RequiredAccountsComplete => (!DraftSalesEnforced || RequiredInput.Selection(DraftSalesAccountId))
        && (!DraftInventoryEnforced || RequiredInput.Selection(DraftInventoryAccountId))
        && (!DraftCogsEnforced || RequiredInput.Selection(DraftCogsAccountId))
        && (!DraftCustomerEnforced || RequiredInput.Selection(DraftCustomerAccountId))
        && (!DraftSupplierEnforced || RequiredInput.Selection(DraftSupplierAccountId))
        && (!DraftCashEnforced || RequiredInput.Selection(DraftCashAccountId))
        && (!DraftDiscountEnforced || RequiredInput.Selection(DraftDiscountAccountId))
        && (!DraftTaxEnforced || RequiredInput.Selection(DraftTaxAccountId))
        && (!DraftBankEnforced || RequiredInput.Selection(DraftBankAccountId))
        && (!DraftTransportRevenueEnforced || RequiredInput.Selection(DraftTransportRevenueAccountId))
        && (!DraftOperatingAccountEnforced || RequiredInput.Selection(DraftOperatingAccountId))
        && (!DraftAssemblyExpenseEnforced || RequiredInput.Selection(DraftAssemblyExpenseAccountId))
        && (!DraftVarianceEnforced || RequiredInput.Selection(DraftVarianceAccountId))
        && (!DraftFxGainEnforced || RequiredInput.Selection(DraftFxGainAccountId))
        && (!DraftFxLossEnforced || RequiredInput.Selection(DraftFxLossAccountId));

    protected override bool HasDraftChanges => IsNewMode
        ? DraftCode.Length > 0 || DraftNameArabic.Length > 0 || DraftShortCode.Length > 0 || FxMappingsChanged
        : Pattern is { } current && (
            DraftCode != current.Code
            || DraftShortCode != current.ShortCode
            || DraftNameArabic != current.NameArabic
            || (string.IsNullOrWhiteSpace(DraftNameEnglish) ? null : DraftNameEnglish.Trim()) != current.NameEnglish
            || (string.IsNullOrWhiteSpace(DraftGroup) ? null : DraftGroup.Trim()) != current.Group
            || (string.IsNullOrWhiteSpace(DraftIcon) ? null : DraftIcon.Trim()) != current.Icon
            || DraftIsActive != current.IsActive
            || DraftIsDefault != current.IsDefault || FxMappingsChanged);

    protected override async Task LoadAsync(object? parameter, CancellationToken cancellationToken)
    {
        NotifyPermissions();

        if (parameter is OpenPatternCardRequest open)
        {
            PatternId = open.PatternId;
            await LoadLookupsAsync(cancellationToken).ConfigureAwait(true);
            await LoadPatternAsync(cancellationToken).ConfigureAwait(true);
            // Opening a record in view mode: the draft fields (shown read-only) must hold what was just read, not the constructor defaults.
            // Later reloads (after creating a revision) deliberately keep what the user typed, as before.
            LoadDraft(EntityCardMode.View);
        }
        else if (parameter is NewPatternCardRequest newReq)
        {
            PatternId = null;
            await LoadLookupsAsync(cancellationToken).ConfigureAwait(true);
            if (newReq.BasedOnPatternId is { } baseId)
            {
                var source = await patternsClient.GetPatternAsync(CompanyId, BranchId, baseId, cancellationToken).ConfigureAwait(true);
                LoadFromSource(source);
            }
            else
            {
                BeginNewCommand.Execute(null);
                if (newReq.InitialOperationKind is { } initKind)
                {
                    DraftOperationKind = initKind;
                    ApplyDefaultPatternSettingsForKind(initKind);
                }
            }
        }
    }

    protected override void ClearPage()
    {
        Pattern = null;
        PatternId = null;
        Revisions.Clear();
        AccountLookups.Clear();
        CostCenterLookups.Clear();
        DraftFieldRules.Clear();
        SelectedRevision = null;
        RevisionDiffText = string.Empty;
        ResetMode();
    }

    public void Localize()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(ModeLabel));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(OperationKinds));
        OnPropertyChanged(nameof(NumberingScopes));
        NotifyCommands();
    }

    protected override void NotifyCommands()
    {
        PublishRevisionCommand?.NotifyCanExecuteChanged();
        RetireCommand?.NotifyCanExecuteChanged();
        CreateDraftRevisionCommand?.NotifyCanExecuteChanged();
        RefreshCommand?.NotifyCanExecuteChanged();
        NotifyCardCommands();
    }

    private void NotifyPermissions()
    {
        OnPropertyChanged(nameof(CanManage));
        OnPropertyChanged(nameof(CanPublish));
        NotifyCardCommands();
    }

    private async Task LoadLookupsAsync(CancellationToken ct)
    {
        var accounts = await accountsClient.GetAccountTreeAsync(CompanyId, BranchId, ct).ConfigureAwait(true);
        AccountLookups.Clear();
        foreach (var acc in accounts.Where(a => a.PostingAllowed && a.IsActive))
        {
            AccountLookups.Add(new LookupItem(acc.Id, acc.Code, acc.NameArabic, acc.NameEnglish, acc.IsActive));
        }

        var costCenters = await accountsClient.SearchCostCentersAsync(CompanyId, BranchId, null, true, 1, 200, ct).ConfigureAwait(true);
        CostCenterLookups.Clear();
        foreach (var cc in costCenters.Items.Where(c => c.IsActive))
        {
            CostCenterLookups.Add(new LookupItem(cc.Id, cc.Code, cc.NameArabic, cc.NameEnglish, cc.IsActive));
        }
    }

    private async Task LoadPatternAsync(CancellationToken ct = default)
    {
        if (PatternId is null) return;

        var details = await patternsClient.GetPatternAsync(CompanyId, BranchId, PatternId.Value, ct).ConfigureAwait(true);
        Pattern = details;

        Revisions.Clear();
        if (details.Revisions is { Count: > 0 })
        {
            foreach (var r in details.Revisions) Revisions.Add(r);
        }
        else if (details.ActiveRevision is not null)
        {
            Revisions.Add(details.ActiveRevision);
        }
        SelectedRevision = details.ActiveRevision ?? Revisions.FirstOrDefault();

        ResetMode();
        NotifyProperties();
    }

    private void LoadFromSource(DocumentPatternResponse source)
    {
        BeginNewCommand.Execute(null);

        DraftCode = $"{source.Code}-COPY";
        DraftShortCode = source.ShortCode ?? string.Empty;
        DraftNameArabic = $"{source.NameArabic} (نسخة)";
        DraftNameEnglish = source.NameEnglish is null ? null : $"{source.NameEnglish} (Copy)";
        DraftOperationKind = source.OperationKind ?? BusinessOperationKind.ManualJournal;
        DraftGroup = source.Group;
        DraftIcon = source.Icon;
        DraftIsActive = true;
        DraftIsDefault = false;
        DraftBranchIds = new ObservableCollection<Guid>(source.BranchIds ?? []);

        var config = source.ActiveRevision?.Configuration;
        if (config is not null)
        {
            ApplyConfigurationToDraft(config);
        }

        DraftChanged();
    }

    private void ApplyConfigurationToDraft(PatternConfigurationDto config)
    {
        preservedConfiguration = config;
        // The configuration is the whole truth: clear every role first so a role removed on the server cannot linger in the draft.
        preservedMappings.Clear();
        DraftFxGainAccountId = DraftFxLossAccountId = null;
        DraftFxGainEnforced = DraftFxLossEnforced = false;
        DraftSalesAccountId = DraftInventoryAccountId = DraftCogsAccountId = DraftCustomerAccountId = DraftSupplierAccountId = null;
        DraftCashAccountId = DraftBankAccountId = DraftDiscountAccountId = DraftTaxAccountId = DraftTransportRevenueAccountId = null;
        DraftOperatingAccountId = DraftAssemblyExpenseAccountId = DraftVarianceAccountId = null;
        DraftSalesEnforced = DraftInventoryEnforced = DraftCogsEnforced = DraftCustomerEnforced = DraftSupplierEnforced = false;
        DraftCashEnforced = DraftBankEnforced = DraftDiscountEnforced = DraftTaxEnforced = DraftTransportRevenueEnforced = false;
        DraftOperatingAccountEnforced = DraftAssemblyExpenseEnforced = DraftVarianceEnforced = false;
        if (config.AccountMappings is not null)
        {
            foreach (var mapping in config.AccountMappings)
            {
                var accId = mapping.AccountId;
                var enforced = mapping.IsRequired ?? false;
                // Both the canonical server names and the legacy aliases are accepted when loading.
                switch (mapping.Role?.Trim().ToUpperInvariant())
                {
                    case "REVENUE" or "SALES": DraftSalesAccountId = accId; DraftSalesEnforced = enforced; break;
                    case "INVENTORY": DraftInventoryAccountId = accId; DraftInventoryEnforced = enforced; break;
                    case "COGS" or "COSTOFGOODS": DraftCogsAccountId = accId; DraftCogsEnforced = enforced; break;
                    case "RECEIVABLE" or "CUSTOMER": DraftCustomerAccountId = accId; DraftCustomerEnforced = enforced; break;
                    case "PAYABLE" or "SUPPLIER": DraftSupplierAccountId = accId; DraftSupplierEnforced = enforced; break;
                    case "CASH": DraftCashAccountId = accId; DraftCashEnforced = enforced; break;
                    case "BANK": DraftBankAccountId = accId; DraftBankEnforced = enforced; break;
                    case "DISCOUNT": DraftDiscountAccountId = accId; DraftDiscountEnforced = enforced; break;
                    case "TAX": DraftTaxAccountId = accId; DraftTaxEnforced = enforced; break;
                    case "TRANSPORTREVENUE": DraftTransportRevenueAccountId = accId; DraftTransportRevenueEnforced = enforced; break;
                    case "FXGAIN": DraftFxGainAccountId = accId; DraftFxGainEnforced = enforced; break;
                    case "FXLOSS": DraftFxLossAccountId = accId; DraftFxLossEnforced = enforced; break;
                    case "OPERATINGACCOUNT" or "OPERATING" or "WORKINPROGRESS": DraftOperatingAccountId = accId; DraftOperatingAccountEnforced = enforced; break;
                    case "ASSEMBLYEXPENSE" or "MANUFACTURINGEXPENSE": DraftAssemblyExpenseAccountId = accId; DraftAssemblyExpenseEnforced = enforced; break;
                    case "VARIANCE" or "SETTLEMENTVARIANCE": DraftVarianceAccountId = accId; DraftVarianceEnforced = enforced; break;
                    default: preservedMappings.Add(mapping); break;
                }
            }
        }

        DraftDefaultCostCenterId = config.DefaultCostCenterId;
        DraftDefaultCurrencyCode = config.DefaultCurrencyCode;
        DraftDefaultPaymentMethod = config.AllowedPaymentMethods is { Count: > 0 } methods ? methods[0] : null;

        DraftFieldRules.Clear();
        if (config.LineFields is not null)
        {
            foreach (var rule in config.LineFields)
            {
                DraftFieldRules.Add(rule);
            }
        }

        if (config.ApprovalPolicy is not null)
        {
            DraftRequiresApproval = config.ApprovalPolicy.RequiresApproval ?? false;
            DraftThresholdAmount = config.ApprovalPolicy.ThresholdAmount;
        }

        DraftLockDocumentDate = config.EntryPolicy?.LockDocumentDate ?? false;
        ApplyVoucherToDraft(config);
        if (config.NumberingPolicy is not null)
        {
            DraftInitialSequence = config.NumberingPolicy.InitialSequence ?? 1;
            DraftResetPeriodically = config.NumberingPolicy.ResetPolicy?.ToString() ?? "Never";
            DraftAllowManualNumbering = config.NumberingPolicy.AllowManualNumbering ?? false;
            DraftNumberingScope = config.NumberingPolicy.Scope ?? PatternNumberingScope.Branch;
            DraftPrefix = config.NumberingPolicy.Prefix;
            DraftSuffix = config.NumberingPolicy.Suffix;
            DraftMinimumDigits = config.NumberingPolicy.MinimumDigits ?? 5;
        }
    }

    private PatternConfigurationDto BuildConfigurationFromDraft()
    {
        // Canonical role names are the ones the posting engines resolve (the server also normalizes legacy aliases).
        var mappings = new List<PatternAccountMappingDto>();
        void Map(string role, Guid? account, bool enforced)
        {
            if (account is not null)
                mappings.Add(new PatternAccountMappingDto { Role = role, AccountId = account, IsRequired = enforced, AllowOverride = !enforced });
        }
        Map("Revenue", DraftSalesAccountId, DraftSalesEnforced);
        Map("Inventory", DraftInventoryAccountId, DraftInventoryEnforced);
        Map("COGS", DraftCogsAccountId, DraftCogsEnforced);
        Map("Receivable", DraftCustomerAccountId, DraftCustomerEnforced);
        Map("Payable", DraftSupplierAccountId, DraftSupplierEnforced);
        Map("Cash", DraftCashAccountId, DraftCashEnforced);
        Map("Bank", DraftBankAccountId, DraftBankEnforced);
        Map("Discount", DraftDiscountAccountId, DraftDiscountEnforced);
        Map("Tax", DraftTaxAccountId, DraftTaxEnforced);
        Map("TransportRevenue", DraftTransportRevenueAccountId, DraftTransportRevenueEnforced);
        Map("FxGain", DraftFxGainAccountId, DraftFxGainEnforced);
        Map("FxLoss", DraftFxLossAccountId, DraftFxLossEnforced);
        Map("OperatingAccount", DraftOperatingAccountId, DraftOperatingAccountEnforced);
        Map("AssemblyExpense", DraftAssemblyExpenseAccountId, DraftAssemblyExpenseEnforced);
        Map("Variance", DraftVarianceAccountId, DraftVarianceEnforced);
        mappings.AddRange(preservedMappings);

        return new PatternConfigurationDto
        {
            SchemaVersion = 2,
            Voucher = BuildVoucherFromDraft(),
            DefaultWarehouseId = Guid.TryParse(DraftDefaultWarehouse, out var warehouse) ? warehouse : preservedConfiguration?.DefaultWarehouseId,
            AllowedWarehouseIds = preservedConfiguration?.AllowedWarehouseIds,
            OptionOverridesWhitelist = preservedConfiguration?.OptionOverridesWhitelist,
            PricingPolicy = preservedConfiguration?.PricingPolicy,
            EntryPolicy = new PatternEntryPolicyDto { LockDocumentDate = DraftLockDocumentDate },
            DefaultCostCenterId = DraftDefaultCostCenterId,
            DefaultCurrencyCode = DraftDefaultCurrencyCode,
            AllowedPaymentMethods = string.IsNullOrWhiteSpace(DraftDefaultPaymentMethod) ? preservedConfiguration?.AllowedPaymentMethods
                : new[] { DraftDefaultPaymentMethod }.Concat(preservedConfiguration?.AllowedPaymentMethods ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            AccountMappings = mappings,
            LineFields = DraftFieldRules.ToList(),
            ApprovalPolicy = new PatternApprovalPolicyDto
            {
                RequiresApproval = DraftRequiresApproval,
                ThresholdAmount = string.IsNullOrWhiteSpace(DraftThresholdAmount) ? null : DraftThresholdAmount.Trim(),
            },
            NumberingPolicy = new PatternNumberingPolicyDto
            {
                InitialSequence = DraftInitialSequence,
                ResetPolicy = Enum.TryParse<PatternNumberResetPolicy>(DraftResetPeriodically,out var reset) ? reset : PatternNumberResetPolicy.Never,
                AllowManualNumbering = DraftAllowManualNumbering,
                Scope = DraftNumberingScope,
                Prefix = DraftPrefix,
                Suffix = DraftSuffix,
                MinimumDigits = DraftMinimumDigits,
            },
        };
    }

    protected override void LoadDraft(EntityCardMode mode)
    {
        if (mode == EntityCardMode.New || Pattern is null)
        {
            DraftFxGainAccountId = DraftFxLossAccountId = null;
            DraftFxGainEnforced = DraftFxLossEnforced = false;
            DraftCode = string.Empty;
            DraftShortCode = string.Empty;
            DraftNameArabic = string.Empty;
            DraftNameEnglish = null;
            DraftOperationKind = BusinessOperationKind.ManualJournal;
            DraftGroup = null;
            DraftIcon = null;
            DraftIsActive = true;
            DraftIsDefault = false;
            DraftBranchIds = [];

            // Default standard field rules
            DraftFieldRules.Clear();
            DraftFieldRules.Add(new PatternFieldRuleDto { FieldName = "Account", IsVisible = true, IsRequired = true, DisplayOrder = 1 });
            DraftFieldRules.Add(new PatternFieldRuleDto { FieldName = "Debit", IsVisible = true, IsRequired = false, DisplayOrder = 2 });
            DraftFieldRules.Add(new PatternFieldRuleDto { FieldName = "Credit", IsVisible = true, IsRequired = false, DisplayOrder = 3 });
            DraftFieldRules.Add(new PatternFieldRuleDto { FieldName = "CostCenter", IsVisible = true, IsRequired = false, DisplayOrder = 4 });
            DraftFieldRules.Add(new PatternFieldRuleDto { FieldName = "Notes", IsVisible = true, IsRequired = false, DisplayOrder = 5 });

            preservedConfiguration = null;
            ResetVoucherDraft();
            DraftInitialSequence = 1; DraftAllowManualNumbering = false; DraftLockDocumentDate = false;
            DraftRequiresApproval = false;
            DraftThresholdAmount = null;
            DraftApprovalRole = null;
            DraftNumberingScope = PatternNumberingScope.Branch;
            DraftPrefix = "MJ-";
            DraftSuffix = null;
            DraftMinimumDigits = 5;
            DraftResetPeriodically = "Never";
        }
        else
        {
            DraftCode = Pattern.Code ?? string.Empty;
            DraftShortCode = Pattern.ShortCode ?? string.Empty;
            DraftNameArabic = Pattern.NameArabic ?? string.Empty;
            DraftNameEnglish = Pattern.NameEnglish;
            DraftOperationKind = Pattern.OperationKind ?? BusinessOperationKind.ManualJournal;
            DraftGroup = Pattern.Group;
            DraftIcon = Pattern.Icon;
            DraftIsActive = Pattern.IsActive ?? false;
            DraftIsDefault = Pattern.IsDefault ?? false;
            DraftBranchIds = new ObservableCollection<Guid>(Pattern.BranchIds ?? []);

            if (Pattern.ActiveRevision?.Configuration is not null)
            {
                ApplyConfigurationToDraft(Pattern.ActiveRevision.Configuration);
            }
        }
    }

    protected override async Task<bool> SaveDraftAsync(EntityCardMode mode)
    {
        var config = BuildConfigurationFromDraft();

        if (mode == EntityCardMode.New)
        {
            var createRequest = new CreateDocumentPatternRequest
            {
                OperationKind = DraftOperationKind,
                Code = DraftCode.Trim(),
                ShortCode = DraftShortCode.Trim().ToUpperInvariant(),
                NameArabic = DraftNameArabic.Trim(),
                NameEnglish = string.IsNullOrWhiteSpace(DraftNameEnglish) ? null : DraftNameEnglish.Trim(),
                Icon = string.IsNullOrWhiteSpace(DraftIcon) ? null : DraftIcon.Trim(),
                Group = string.IsNullOrWhiteSpace(DraftGroup) ? null : DraftGroup.Trim(),
                IsDefault = DraftIsDefault,
                BranchIds = DraftBranchIds.ToList(),
                InitialConfiguration = config,
            };

            var created = await patternsClient.CreatePatternAsync(CompanyId, BranchId, createRequest, Token).ConfigureAwait(true);
            PatternId = created.Id;
            Pattern = created;
            ResetMode();
            NotifyProperties();
            return true;
        }

        if (Pattern is null) return false;

        var updateRequest = new UpdateDocumentPatternRequest
        {
            ExpectedVersion = Pattern.Version ?? string.Empty,
            NameArabic = DraftNameArabic.Trim(),
            NameEnglish = string.IsNullOrWhiteSpace(DraftNameEnglish) ? null : DraftNameEnglish.Trim(),
            ShortCode = DraftShortCode.Trim().ToUpperInvariant(),
            Icon = string.IsNullOrWhiteSpace(DraftIcon) ? null : DraftIcon.Trim(),
            Group = string.IsNullOrWhiteSpace(DraftGroup) ? null : DraftGroup.Trim(),
            IsActive = DraftIsActive,
            IsDefault = DraftIsDefault,
            BranchIds = DraftBranchIds.ToList(),
        };

        var updated = await patternsClient.UpdatePatternAsync(CompanyId, BranchId, Pattern.Id!.Value, Pattern.Version ?? string.Empty, updateRequest, Token).ConfigureAwait(true);
        Pattern = updated;

        // Create new draft revision with the updated configuration
        var revRequest = new CreatePatternRevisionRequest
        {
            ExpectedVersion = updated.Version ?? string.Empty,
            Configuration = config,
            Reason = "تعديل إعدادات النمط",
        };

        await patternsClient.CreateRevisionAsync(CompanyId, BranchId, Pattern.Id!.Value, revRequest, Token).ConfigureAwait(true);

        // Reload pattern to reflect latest state
        await LoadPatternAsync().ConfigureAwait(true);
        ResetMode();
        NotifyProperties();
        return true;
    }

    private async Task PublishActiveRevisionAsync()
    {
        if (Pattern?.ActiveRevision is null || !CanPublish) return;

        var confirmed = await confirmation.ConfirmAsync("D_ConfirmPublishPattern").ConfigureAwait(true);
        if (!confirmed) return;

        await RunAsync(async ct =>
        {
            var request = new PublishPatternRevisionRequest
            {
                ExpectedVersion = Pattern.Version ?? string.Empty,
                RevisionId = Pattern.ActiveRevision.Id!.Value,
                Reason = "نشر الإصدار من بطاقة النمط",
            };

            var result = await patternsClient.PublishRevisionAsync(CompanyId, BranchId, Pattern.Id!.Value, request, ct).ConfigureAwait(true);
            Pattern = result;
            NotifyProperties();
        }).ConfigureAwait(true);
    }

    private async Task RetirePatternAsync()
    {
        if (Pattern is null || !CanPublish) return;

        var confirmed = await confirmation.ConfirmAsync("D_ConfirmRetirePattern").ConfigureAwait(true);
        if (!confirmed) return;

        await RunAsync(async ct =>
        {
            var request = new RetirePatternRequest
            {
                ExpectedVersion = Pattern.Version ?? string.Empty,
                Reason = "إيقاف النمط من بطاقة النمط",
            };

            var result = await patternsClient.RetirePatternAsync(CompanyId, BranchId, Pattern.Id!.Value, request, ct).ConfigureAwait(true);
            Pattern = result;
            NotifyProperties();
        }).ConfigureAwait(true);
    }

    private async Task CreateNewRevisionAsync()
    {
        if (Pattern is null || !CanManage || !RequiredAccountsComplete) return;

        await RunAsync(async ct =>
        {
            var request = new CreatePatternRevisionRequest
            {
                ExpectedVersion = Pattern.Version ?? string.Empty,
                Configuration = BuildConfigurationFromDraft(),
                Reason = "إنشاء مسودة إصدار جديدة",
            };

            await patternsClient.CreateRevisionAsync(CompanyId, BranchId, Pattern.Id!.Value, request, ct).ConfigureAwait(true);
            await LoadPatternAsync(ct).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }


    partial void OnSelectedRevisionChanged(PatternRevisionResponse? value) => ComputeRevisionDiff();

    private void ComputeRevisionDiff()
    {
        if (SelectedRevision is null || Pattern is null)
        {
            RevisionDiffText = string.Empty;
            return;
        }

        var selected = SelectedRevision;
        var active = Pattern.ActiveRevision;
        if (active is null || selected.Id == active.Id)
        {
            RevisionDiffText = "الإصدار #" + selected.RevisionNumber + " (الحالة: " + selected.State + ") هو الإصدار الفعال المعتمد حالياً للنمط.";
            return;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("مقارنة الإصدار #" + selected.RevisionNumber + " مع الإصدار الفعال #" + active.RevisionNumber + ":");

        var selCfg = selected.Configuration;
        var actCfg = active.Configuration;

        var selRoles = selCfg?.AccountMappings?.Where(m => !string.IsNullOrEmpty(m.Role)).ToDictionary(m => m.Role!, m => m.AccountId) ?? [];
        var actRoles = actCfg?.AccountMappings?.Where(m => !string.IsNullOrEmpty(m.Role)).ToDictionary(m => m.Role!, m => m.AccountId) ?? [];

        foreach (var (role, acc) in selRoles)
        {
            if (!actRoles.TryGetValue(role, out var actAcc))
                sb.AppendLine("- دور الحساب '" + role + "' موجود في هذا الإصدار وغير موجود في الفعال.");
            else if (acc != actAcc)
                sb.AppendLine("- دور الحساب '" + role + "' تم تغيير حسابه بين الإصدارين.");
        }
        foreach (var role in actRoles.Keys.Where(k => !selRoles.ContainsKey(k)))
        {
            sb.AppendLine("- دور الحساب '" + role + "' مضاف في الإصدار الفعال.");
        }

        if (selCfg?.DefaultWarehouseId != actCfg?.DefaultWarehouseId)
            sb.AppendLine("- المستودع الافتراضي يختلف بين الإصدارين.");
        if (!string.Equals(selCfg?.DefaultCurrencyCode, actCfg?.DefaultCurrencyCode, StringComparison.OrdinalIgnoreCase))
            sb.AppendLine("- العملة الافتراضية تختلف (" + (selCfg?.DefaultCurrencyCode ?? "افتراضي") + " مقابل " + (actCfg?.DefaultCurrencyCode ?? "افتراضي") + ").");

        if (selCfg?.NumberingPolicy?.Scope != actCfg?.NumberingPolicy?.Scope)
            sb.AppendLine("- نطاق الترقيم يختلف (" + selCfg?.NumberingPolicy?.Scope + " مقابل " + actCfg?.NumberingPolicy?.Scope + ").");
        if (selCfg?.NumberingPolicy?.Prefix != actCfg?.NumberingPolicy?.Prefix)
            sb.AppendLine("- بادئة الترقيم تختلف ('" + selCfg?.NumberingPolicy?.Prefix + "' مقابل '" + actCfg?.NumberingPolicy?.Prefix + "').");

        if (selCfg?.ApprovalPolicy?.RequiresApproval != actCfg?.ApprovalPolicy?.RequiresApproval)
            sb.AppendLine("- سياسة الموافقة تختلف (مطلوبة: " + selCfg?.ApprovalPolicy?.RequiresApproval + " مقابل " + actCfg?.ApprovalPolicy?.RequiresApproval + ").");

        RevisionDiffText = sb.ToString();
    }

    private void NotifyProperties()
    {
        OnPropertyChanged(nameof(Pattern));
        OnPropertyChanged(nameof(HasPattern));
        OnPropertyChanged(nameof(HasActiveRevision));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusKind));
        PublishRevisionCommand.NotifyCanExecuteChanged();
        RetireCommand.NotifyCanExecuteChanged();
        CreateDraftRevisionCommand.NotifyCanExecuteChanged();
        NotifyCardCommands();
    }

    private void ApplyDefaultPatternSettingsForKind(BusinessOperationKind kind)
    {
        switch (kind)
        {
            case BusinessOperationKind.SalesInvoice:
                DraftPrefix = "SI-";
                PopulateStandardInvoiceFieldRules();
                break;
            case BusinessOperationKind.PurchaseInvoice:
                DraftPrefix = "PI-";
                PopulateStandardInvoiceFieldRules();
                break;
            case BusinessOperationKind.SalesReturn:
                DraftPrefix = "SR-";
                PopulateStandardInvoiceFieldRules();
                break;
            case BusinessOperationKind.PurchaseReturn:
                DraftPrefix = "PR-";
                PopulateStandardInvoiceFieldRules();
                break;
            case BusinessOperationKind.ManualJournal:
                DraftPrefix = "MJ-";
                PopulateStandardJournalFieldRules();
                break;
            case BusinessOperationKind.CashReceipt:
                DraftPrefix = "CR-";
                PopulateStandardVoucherFieldRules(payment: false);
                break;
            case BusinessOperationKind.CashPayment:
                DraftPrefix = "CP-";
                PopulateStandardVoucherFieldRules(payment: true);
                break;
            case BusinessOperationKind.CashJournal:
                DraftPrefix = "CJ-";
                PopulateStandardJournalFieldRules();
                break;
            case BusinessOperationKind.InventoryReceipt:
                DraftPrefix = "IR-";
                PopulateStandardInventoryFieldRules();
                break;
            case BusinessOperationKind.InventoryIssue:
                DraftPrefix = "II-";
                PopulateStandardInventoryFieldRules();
                break;
        }
    }

    /// <summary>Voucher grid columns with their editable headers; the voucher screen reads these captions from the active revision.</summary>
    private void PopulateStandardVoucherFieldRules(bool payment)
    {
        DraftFieldRules.Clear();
        DraftFieldRules.Add(new PatternFieldRuleDto { FieldName = "Party", CaptionArabic = payment ? "المورد" : "الزبون", DisplayOrder = 1, IsVisible = true });
        DraftFieldRules.Add(new PatternFieldRuleDto { FieldName = "Account", CaptionArabic = payment ? "حساب المصروف" : "الحساب", DisplayOrder = 2, IsVisible = true });
        DraftFieldRules.Add(new PatternFieldRuleDto { FieldName = "CostCenter", CaptionArabic = "مركز الكلفة", DisplayOrder = 3, IsVisible = true });
        DraftFieldRules.Add(new PatternFieldRuleDto { FieldName = "Amount", CaptionArabic = "المبلغ", DisplayOrder = 4, IsVisible = true, IsRequired = true });
        DraftFieldRules.Add(new PatternFieldRuleDto { FieldName = "Description", CaptionArabic = "البيان", DisplayOrder = 5, IsVisible = true });
    }

    private void PopulateStandardInvoiceFieldRules()
    {
        DraftFieldRules.Clear();
        DraftFieldRules.Add(new PatternFieldRuleDto { FieldName = "Item", DisplayOrder = 1, IsVisible = true, IsRequired = true });
        DraftFieldRules.Add(new PatternFieldRuleDto { FieldName = "Unit", DisplayOrder = 2, IsVisible = true, IsRequired = true });
        DraftFieldRules.Add(new PatternFieldRuleDto { FieldName = "Quantity", DisplayOrder = 3, IsVisible = true, IsRequired = true });
        DraftFieldRules.Add(new PatternFieldRuleDto { FieldName = "UnitPrice", DisplayOrder = 4, IsVisible = true, IsRequired = true });
        DraftFieldRules.Add(new PatternFieldRuleDto { FieldName = "Notes", DisplayOrder = 5, IsVisible = true, IsRequired = false });
        DraftFieldRules.Add(new PatternFieldRuleDto { FieldName = "CostCenter", DisplayOrder = 6, IsVisible = true, IsRequired = false });
        DraftFieldRules.Add(new PatternFieldRuleDto { FieldName = "Warehouse", DisplayOrder = 7, IsVisible = true, IsRequired = false });
        DraftFieldRules.Add(new PatternFieldRuleDto { FieldName = "DiscountAmount", DisplayOrder = 8, IsVisible = true, IsRequired = false });
        DraftFieldRules.Add(new PatternFieldRuleDto { FieldName = "TaxAmount", DisplayOrder = 9, IsVisible = true, IsRequired = false });
        DraftFieldRules.Add(new PatternFieldRuleDto { FieldName = "NetTotal", DisplayOrder = 10, IsVisible = true, IsRequired = false });
    }

    private void PopulateStandardJournalFieldRules()
    {
        DraftFieldRules.Clear();
        DraftFieldRules.Add(new PatternFieldRuleDto { FieldName = "Account", DisplayOrder = 1, IsVisible = true, IsRequired = true });
        DraftFieldRules.Add(new PatternFieldRuleDto { FieldName = "Debit", DisplayOrder = 2, IsVisible = true, IsRequired = false });
        DraftFieldRules.Add(new PatternFieldRuleDto { FieldName = "Credit", DisplayOrder = 3, IsVisible = true, IsRequired = false });
        DraftFieldRules.Add(new PatternFieldRuleDto { FieldName = "CostCenter", DisplayOrder = 4, IsVisible = true, IsRequired = false });
        DraftFieldRules.Add(new PatternFieldRuleDto { FieldName = "Notes", DisplayOrder = 5, IsVisible = true, IsRequired = false });
    }

    private void PopulateStandardInventoryFieldRules()
    {
        DraftFieldRules.Clear();
        DraftFieldRules.Add(new PatternFieldRuleDto { FieldName = "Item", DisplayOrder = 1, IsVisible = true, IsRequired = true });
        DraftFieldRules.Add(new PatternFieldRuleDto { FieldName = "Unit", DisplayOrder = 2, IsVisible = true, IsRequired = true });
        DraftFieldRules.Add(new PatternFieldRuleDto { FieldName = "Quantity", DisplayOrder = 3, IsVisible = true, IsRequired = true });
        DraftFieldRules.Add(new PatternFieldRuleDto { FieldName = "UnitCost", DisplayOrder = 4, IsVisible = true, IsRequired = false });
        DraftFieldRules.Add(new PatternFieldRuleDto { FieldName = "Notes", DisplayOrder = 5, IsVisible = true, IsRequired = false });
    }
}

public sealed class PatternCardFactory(
    IPatternsClient patternsClient,
    IChartOfAccountsClient accountsClient,
    ShellNavigator navigator,
    DesktopOperation operation,
    ILocalizationService localization,
    IUserConfirmation confirmation)
{
    public PatternCardViewModel Create() => new(patternsClient, accountsClient, navigator, operation, localization, confirmation);
}
