using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Accounting;
using PetraERP.Desktop.Controls;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;
using PetraERP.Desktop.Sales;

namespace PetraERP.Desktop.Parties;

public sealed record LinkedDocumentItem(string DocumentType, string DocumentNumber, DateOnly Date, string Amount, string State);

public sealed partial class PartyCardViewModel : EntityCardViewModel
{
    public SalesDocumentLinksViewModel? DocumentLinks { get; init; }
    public Relationships.RelatedDocumentsViewModel? PurchaseLinks { get; init; }
    public Relationships.RelatedDocumentsViewModel? ReceiptLinks { get; init; }
    public Relationships.RelatedDocumentsViewModel? PaymentLinks { get; init; }
    private readonly IPartiesClient partiesClient;
    private readonly IChartOfAccountsClient accountsClient;
    private readonly ShellNavigator navigator;
    private readonly IUserConfirmation confirmation;

    [ObservableProperty] private PartyDetailDto? party;
    [ObservableProperty] private Guid? partyId;
    [ObservableProperty] private PartyCreditExposureDto? exposure;

    // --- Draft fields: General ---
    [ObservableProperty] private string draftCode = string.Empty;
    [ObservableProperty] private string draftNameArabic = string.Empty;
    [ObservableProperty] private string? draftNameEnglish;
    [ObservableProperty] private PartyRole draftRole = PartyRole.Customer;
    [ObservableProperty] private bool draftIsActive = true;
    [ObservableProperty] private string? draftDefaultCurrencyCode;
    [ObservableProperty] private string? draftTaxNumber;
    [ObservableProperty] private string? draftCommercialRegistration;

    // --- Draft fields: Contacts & Address ---
    [ObservableProperty] private string? draftContactName;
    [ObservableProperty] private string? draftPhone;
    [ObservableProperty] private string? draftMobile;
    [ObservableProperty] private string? draftEmail;
    [ObservableProperty] private string? draftNotes;
    [ObservableProperty] private string? draftStreet;
    [ObservableProperty] private string? draftCity;
    [ObservableProperty] private string? draftCountry;

    // --- Draft fields: Accounting ---
    [ObservableProperty] private Guid? draftReceivableAccountId;
    [ObservableProperty] private Guid? draftPayableAccountId;

    // --- Draft fields: Credit Policy ---
    [ObservableProperty] private CreditPolicyKind draftPolicyKind = CreditPolicyKind.AllowWithWarning;
    [ObservableProperty] private string draftCreditLimit = "0.0000";
    [ObservableProperty] private bool draftIsCreditBlocked;
    [ObservableProperty] private int? draftPaymentTermDays;

    // Lookups & Collections
    public ObservableCollection<LookupItem> AccountLookups { get; } = [];
    public ObservableCollection<LinkedDocumentItem> LinkedDocuments { get; } = [];

    public PartyCardViewModel(
        IPartiesClient partiesClient,
        IChartOfAccountsClient accountsClient,
        ShellNavigator navigator,
        DesktopOperation operation,
        ILocalizationService localization,
        IUserConfirmation confirmation)
        : base(operation, localization, confirmation)
    {
        this.partiesClient = partiesClient;
        this.accountsClient = accountsClient;
        this.navigator = navigator;
        this.confirmation = confirmation;

        RefreshCommand = new AsyncRelayCommand(
            () => IsViewMode ? RunAsync(ct => LoadPartyAsync(ct)) : Task.CompletedTask,
            () => HasContext && Idle && PartyId is not null && IsViewMode);

        DeactivateCommand = new AsyncRelayCommand(DeactivateAsync, () => HasContext && Idle && CanManage && Party is { IsActive: true, IsCashCustomer: not true });
        ReactivateCommand = new AsyncRelayCommand(ReactivateAsync, () => HasContext && Idle && CanManage && Party is { IsActive: false, IsCashCustomer: not true });
    }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand DeactivateCommand { get; }
    public AsyncRelayCommand ReactivateCommand { get; }

    public bool HasParty => Party is not null;
    public bool CanManage => Can(PartiesViewModel.ManagePermission) && Party?.IsCashCustomer != true;
    public bool IsCashCustomer => Party?.IsCashCustomer == true;
    public string CreditLimitCaption => $"{Localization["AR_CreditLimitBase"]} ({BaseCurrencyCode})";
    public IEnumerable<PartyPolicyHistoryItem> PolicyHistory => Party?.PolicyHistory?.Select(a => new PartyPolicyHistoryItem(
        a.DateUtc, a.ActorId, a.BeforeCreditLimit ?? "0.0000", a.AfterCreditLimit ?? "0.0000",
        PolicyCaption(a.BeforePolicy), PolicyCaption(a.AfterPolicy), Localization[a.BeforeBlocked == true ? "D_Yes" : "D_No"],
        Localization[a.AfterBlocked == true ? "D_Yes" : "D_No"])) ?? [];
    private string PolicyCaption(int? policy) => Localization[policy switch
    { 1 => "CreditPolicyKind_AllowWithWarning", 2 => "CreditPolicyKind_BlockOnExceed", _ => "CreditPolicyKind_NoLimit" }];
    protected override string RequiredPermission => PartiesViewModel.ViewPermission;
    public bool IsCodeReadOnly => !IsNewMode;

    public string Title => IsNewMode
        ? Localization["D_NewParty"]
        : Party is not null
            ? $"{Party.Code} - {Party.NameArabic}"
            : Localization["D_PartyCard"];

    public string StatusText => Party is null
        ? string.Empty
        : Party.IsActive == true ? "نشط" : "معطل";

    public IReadOnlyList<ChoiceOption<PartyRole>> RoleOptions =>
        Options.For<PartyRole>(Localization, "PartyRole_");

    public IReadOnlyList<ChoiceOption<CreditPolicyKind>> PolicyOptions =>
        Options.For<CreditPolicyKind>(Localization, "CreditPolicyKind_");

    // Change notifications for draft tracking
    partial void OnDraftCodeChanged(string value) => DraftChanged();
    partial void OnDraftNameArabicChanged(string value) => DraftChanged();
    partial void OnDraftNameEnglishChanged(string? value) => DraftChanged();
    partial void OnDraftRoleChanged(PartyRole value)
    {
        OnPropertyChanged(nameof(RequiresReceivableAccount)); OnPropertyChanged(nameof(RequiresPayableAccount)); DraftChanged();
        NotifyAccountPlan();
    }

    protected override void OnModeChanged(EntityCardMode mode) => NotifyAccountPlan();
    partial void OnDraftIsActiveChanged(bool value) => DraftChanged();
    partial void OnDraftDefaultCurrencyCodeChanged(string? value) => DraftChanged();
    partial void OnDraftTaxNumberChanged(string? value) => DraftChanged();
    partial void OnDraftCommercialRegistrationChanged(string? value) => DraftChanged();
    partial void OnDraftContactNameChanged(string? value) => DraftChanged();
    partial void OnDraftPhoneChanged(string? value) => DraftChanged();
    partial void OnDraftMobileChanged(string? value) => DraftChanged();
    partial void OnDraftEmailChanged(string? value) => DraftChanged();
    partial void OnDraftNotesChanged(string? value) => DraftChanged();
    partial void OnDraftStreetChanged(string? value) => DraftChanged();
    partial void OnDraftCityChanged(string? value) => DraftChanged();
    partial void OnDraftCountryChanged(string? value) => DraftChanged();
    partial void OnDraftReceivableAccountIdChanged(Guid? value) { DraftChanged(); NotifyAccountPlan(); }
    partial void OnDraftPayableAccountIdChanged(Guid? value) { DraftChanged(); NotifyAccountPlan(); }
    partial void OnDraftPolicyKindChanged(CreditPolicyKind value) => DraftChanged();
    partial void OnDraftCreditLimitChanged(string value) => DraftChanged();
    partial void OnDraftIsCreditBlockedChanged(bool value) => DraftChanged();
    partial void OnDraftPaymentTermDaysChanged(int? value) => DraftChanged();

    protected override bool CanEditRecord => CanManage && Party is not null;
    protected override bool CanCreateRecord => CanManage;

    public override bool CanSaveDraft
    {
        get
        {
            if (string.IsNullOrWhiteSpace(DraftCode) || string.IsNullOrWhiteSpace(DraftNameArabic))
                return false;

            // An empty account is a choice, not an omission: the server then creates the party's own account under the
            // customers / suppliers heading (company policy, else the chart convention). The card shows that plan beforehand.
            return RequiredInput.AmountText(DraftCreditLimit) && DraftPaymentTermDays is >= 0;
        }
    }

    public override IReadOnlyList<string> GetValidationErrors()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(DraftCode))
            errors.Add(Localization["D_Code"] + ": " + Localization["D_RequiredField"]);
        if (string.IsNullOrWhiteSpace(DraftNameArabic))
            errors.Add(Localization["D_NameArabic"] + ": " + Localization["D_RequiredField"]);
        if (!RequiredInput.AmountText(DraftCreditLimit))
            errors.Add(Localization["AR_CreditLimit"] + ": " + Localization["UI_InvalidNumber"]);
        if (DraftPaymentTermDays < 0)
            errors.Add(Localization["AR_PaymentTerms"] + ": " + Localization["UI_InvalidNumber"]);
        errors.AddRange(ProfileValidationErrors());
        return errors;
    }

    protected override string? GetValidationFocusTarget()
    {
        if (string.IsNullOrWhiteSpace(DraftCode)) return "code";
        if (string.IsNullOrWhiteSpace(DraftNameArabic)) return "name-ar";
        if (!RequiredInput.AmountText(DraftCreditLimit)) return "credit-limit";
        return null;
    }

    public bool RequiresReceivableAccount => DraftRole is PartyRole.Customer or PartyRole.Both;
    public bool RequiresPayableAccount => DraftRole is PartyRole.Supplier or PartyRole.Both;

    private PartyAccountPlanDto? accountPlan;

    /// <summary>What saving with an empty receivable account will do; empty when an account is chosen or the role needs none.</summary>
    public string ReceivableAccountPlanText => IsEditing && RequiresReceivableAccount && DraftReceivableAccountId is null ? PlanText(accountPlan?.Receivable) : string.Empty;

    /// <summary>What saving with an empty payable account will do; empty when an account is chosen or the role needs none.</summary>
    public string PayableAccountPlanText => IsEditing && RequiresPayableAccount && DraftPayableAccountId is null ? PlanText(accountPlan?.Payable) : string.Empty;

    private static string PlanText(PartyAccountPlanSideDto? side)
    {
        if (side is null) return "عند الحفظ يُنشأ لهذا الطرف حساب خاص به في دليل الحسابات.";
        var source = side.FromCompanyPolicy == true ? "حسب خيارات الشركة" : "حسب عرف دليل الحسابات";
        return side.ParentWillBeCreated == true
            ? $"عند الحفظ يُنشأ الحساب الرئيسي {side.ParentCode} - {side.ParentNameArabic} لأنه غير موجود، ثم حساب الطرف {side.NextAccountCode} تحته. يُفضّل تحديد الحساب الرئيسي من خيارات الشركة أولًا."
            : $"عند الحفظ يُنشأ حساب باسم الطرف برمز {side.NextAccountCode} تحت {side.ParentCode} - {side.ParentNameArabic} ({source}).";
    }

    private void NotifyAccountPlan()
    {
        OnPropertyChanged(nameof(ReceivableAccountPlanText)); OnPropertyChanged(nameof(PayableAccountPlanText));
    }

    private async Task LoadAccountPlanAsync(CancellationToken cancellationToken)
    {
        try { accountPlan = await partiesClient.GetAccountPlanAsync(CompanyId, BranchId, cancellationToken).ConfigureAwait(true); }
        catch (Exception exception) when (exception is System.Net.Http.HttpRequestException or PetraApiException or NotSupportedException)
        {
            accountPlan = null; // the general sentence is shown; the server still decides on save
        }
        NotifyAccountPlan();
    }

    protected override bool HasDraftChanges =>
        IsNewMode || ProfileHasChanges() ||
        (Party is not null &&
            (DraftNameArabic != (Party.NameArabic ?? string.Empty) ||
             DraftNameEnglish != Party.NameEnglish ||
             DraftRole != (Party.Role ?? PartyRole.Customer) ||
             (DraftDefaultCurrencyCode ?? string.Empty) != (Party.DefaultCurrencyCode ?? string.Empty) ||
             DraftTaxNumber != Party.TaxNumber ||
             DraftCommercialRegistration != Party.CommercialRegistration ||
             DraftReceivableAccountId != Party.ReceivableAccountId ||
             DraftPayableAccountId != Party.PayableAccountId ||
             DraftPolicyKind != (Party.PolicyKind ?? CreditPolicyKind.AllowWithWarning) ||
             DraftCreditLimit != (Party.CreditLimit ?? "0.0000") ||
             DraftIsCreditBlocked != (Party.IsCreditBlocked == true) ||
             DraftPaymentTermDays != Party.PaymentTermDays));

    protected override async Task LoadAsync(object? parameter, CancellationToken cancellationToken)
    {
        NotifyPermissions();
        if (parameter is OpenPartyCardRequest open)
        {
            PartyId = open.PartyId;
            await LoadLookupsAsync(cancellationToken).ConfigureAwait(true);
            await LoadPartyAsync(cancellationToken).ConfigureAwait(true);
        }
        else if (parameter is NewPartyCardRequest)
        {
            PartyId = null;
            Party = null;
            await LoadLookupsAsync(cancellationToken).ConfigureAwait(true);
            BeginNewCommand.Execute(null);
        }
    }

    protected override void ClearPage()
    {
        Party = null;
        PartyId = null;
        Exposure = null;
        AccountLookups.Clear();
        LinkedDocuments.Clear();
        DocumentLinks?.SetSource(null, SalesLinkSource.Customer, null);
        RefreshRelatedSources();
        ResetMode();
    }

    public void Localize()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(RoleOptions));
        OnPropertyChanged(nameof(PolicyOptions));
        OnPropertyChanged(nameof(CreditLimitCaption));
        OnPropertyChanged(nameof(PolicyHistory));
        NotifyCommands();
    }

    protected override void NotifyCommands()
    {
        NotifyNavigation();
        DocumentLinks?.SetSource(Context, SalesLinkSource.Customer, PartyId);
        RefreshRelatedSources();
        RefreshCommand?.NotifyCanExecuteChanged();
        DeactivateCommand?.NotifyCanExecuteChanged();
        ReactivateCommand?.NotifyCanExecuteChanged();
        NotifyTreasuryCommands();
        NotifyCardCommands();
    }

    private void NotifyPermissions()
    {
        OnPropertyChanged(nameof(CanManage));
        NotifyCommands();
    }

    private async Task LoadLookupsAsync(CancellationToken ct)
    {
        AccountLookups.Clear();
        if (!Can("Ledger.Accounts.View")) return;
        var accounts = await accountsClient.GetAccountTreeAsync(CompanyId, BranchId, ct).ConfigureAwait(true);
        foreach (var acc in accounts.Where(a => a.PostingAllowed && a.IsActive))
        {
            AccountLookups.Add(new LookupItem(acc.Id, acc.Code, acc.NameArabic, acc.NameEnglish, acc.IsActive));
        }
        // No account is pre-selected for a new party: picking "the first account under 121" tied new customers to another
        // customer's account. Empty means the party gets its own account; the plan below says which.
        await LoadAccountPlanAsync(ct).ConfigureAwait(true);
    }

    private async Task LoadPartyAsync(CancellationToken ct = default)
    {
        if (PartyId is null) return;

        var details = await partiesClient.GetPartyAsync(CompanyId, BranchId, PartyId.Value, ct).ConfigureAwait(true);
        ct.ThrowIfCancellationRequested();
        Party = details;
        DocumentLinks?.SetSource(Context, SalesLinkSource.Customer, PartyId);

        try
        {
            Exposure = await partiesClient.GetPartyCreditExposureAsync(CompanyId, BranchId, PartyId.Value, ct).ConfigureAwait(true);
        }
        catch
        {
            Exposure = null;
        }

        ResetMode();
        NotifyProperties();
    }

    /// <summary>Shown as a hint only: an empty party currency means the pattern, then the company base, applies on documents.</summary>
    public string BaseCurrencyCode => Context?.BaseCurrencyCode ?? string.Empty;

    protected override void LoadDraft(EntityCardMode mode)
    {
        if (mode == EntityCardMode.New)
        {
            DraftCode = string.Empty;
            DraftNameArabic = string.Empty;
            DraftNameEnglish = null;
            DraftRole = PartyRole.Customer;
            DraftIsActive = true;
            DraftDefaultCurrencyCode = string.Empty;
            DraftTaxNumber = null;
            DraftCommercialRegistration = null;
            DraftContactName = null;
            DraftPhone = null;
            DraftMobile = null;
            DraftEmail = null;
            DraftNotes = null;
            DraftStreet = null;
            DraftCity = null;
            DraftCountry = null;
            DraftReceivableAccountId = null;
            DraftPayableAccountId = null;
            DraftPolicyKind = CreditPolicyKind.AllowWithWarning;
            DraftCreditLimit = "0.0000";
            DraftIsCreditBlocked = false;
            DraftPaymentTermDays = 30;
            LoadProfileDraft(null);
        }
        else if (Party is not null)
        {
            DraftCode = Party.Code ?? string.Empty;
            DraftNameArabic = Party.NameArabic ?? string.Empty;
            DraftNameEnglish = Party.NameEnglish;
            DraftRole = Party.Role ?? PartyRole.Customer;
            DraftIsActive = Party.IsActive ?? true;
            DraftDefaultCurrencyCode = Party.DefaultCurrencyCode ?? string.Empty;
            DraftTaxNumber = Party.TaxNumber;
            DraftCommercialRegistration = Party.CommercialRegistration;

            var firstContact = Party.Contacts is { Count: > 0 } ? Party.Contacts[0] : null;
            DraftContactName = firstContact?.Name;
            DraftPhone = firstContact?.Phone;
            DraftMobile = firstContact?.Mobile;
            DraftEmail = firstContact?.Email;
            DraftNotes = null;

            var firstAddr = Party.Addresses is { Count: > 0 } ? Party.Addresses[0] : null;
            DraftStreet = firstAddr?.Street;
            DraftCity = firstAddr?.City;
            DraftCountry = null;

            DraftReceivableAccountId = Party.ReceivableAccountId;
            DraftPayableAccountId = Party.PayableAccountId;
            DraftPolicyKind = Party.PolicyKind ?? CreditPolicyKind.AllowWithWarning;
            DraftCreditLimit = Party.CreditLimit ?? "0.0000";
            DraftIsCreditBlocked = Party.IsCreditBlocked == true;
            DraftPaymentTermDays = Party.PaymentTermDays ?? 30;
            LoadProfileDraft(Party.Profile);
        }

        DraftChanged();
    }

    protected override async Task<bool> SaveDraftAsync(EntityCardMode mode)
    {
        if (!CanSaveDraft) return false;

        List<PartyContactDto>? contacts = null;
        if (!string.IsNullOrWhiteSpace(DraftContactName) || !string.IsNullOrWhiteSpace(DraftPhone) || !string.IsNullOrWhiteSpace(DraftEmail))
        {
            contacts =
            [
                new PartyContactDto
                {
                    Id = mode == EntityCardMode.Edit && Party?.Contacts is { Count: > 0 } ? Party.Contacts[0].Id : Guid.NewGuid(),
                    Name = DraftContactName ?? DraftNameArabic,
                    Role = mode == EntityCardMode.Edit && Party?.Contacts is { Count: > 0 } ? Party.Contacts[0].Role : null,
                    Phone = DraftPhone,
                    Mobile = DraftMobile,
                    Email = DraftEmail,
                }
            ];
        }

        List<PartyAddressDto>? addresses = null;
        if (!string.IsNullOrWhiteSpace(DraftCity) || !string.IsNullOrWhiteSpace(DraftStreet))
        {
            addresses =
            [
                new PartyAddressDto
                {
                    Id = mode == EntityCardMode.Edit && Party?.Addresses is { Count: > 0 } ? Party.Addresses[0].Id : Guid.NewGuid(),
                    Label = mode == EntityCardMode.Edit && Party?.Addresses is { Count: > 0 } ? Party.Addresses[0].Label : "الرئيسي",
                    PostalCode = mode == EntityCardMode.Edit && Party?.Addresses is { Count: > 0 } ? Party.Addresses[0].PostalCode : null,
                    Notes = mode == EntityCardMode.Edit && Party?.Addresses is { Count: > 0 } ? Party.Addresses[0].Notes : null,
                    City = DraftCity,
                    Street = DraftStreet,
                }
            ];
        }

        if (mode == EntityCardMode.Edit && Party is not null)
        {
            // The compact editor edits the first contact/address and preserves every additional existing entry.
            contacts ??= [];
            contacts.AddRange((Party.Contacts ?? []).Skip(1));
            addresses ??= [];
            addresses.AddRange((Party.Addresses ?? []).Skip(1));
        }

        if (mode == EntityCardMode.New)
        {
            var createRequest = new CreatePartyRequest
            {
                Code = DraftCode.Trim(),
                NameArabic = DraftNameArabic.Trim(),
                NameEnglish = string.IsNullOrWhiteSpace(DraftNameEnglish) ? null : DraftNameEnglish.Trim(),
                Role = DraftRole,
                TaxNumber = string.IsNullOrWhiteSpace(DraftTaxNumber) ? null : DraftTaxNumber.Trim(),
                CommercialRegistration = string.IsNullOrWhiteSpace(DraftCommercialRegistration) ? null : DraftCommercialRegistration.Trim(),
                DefaultCurrencyCode = string.IsNullOrWhiteSpace(DraftDefaultCurrencyCode) ? null : DraftDefaultCurrencyCode.Trim().ToUpperInvariant(),
                ReceivableAccountId = DraftReceivableAccountId,
                PayableAccountId = DraftPayableAccountId,
                PolicyKind = DraftPolicyKind,
                IsCreditBlocked = DraftIsCreditBlocked,
                CreditLimit = string.IsNullOrWhiteSpace(DraftCreditLimit) ? "0.0000" : DraftCreditLimit.Trim(),
                PaymentTermDays = DraftPaymentTermDays,
                Contacts = contacts,
            Addresses = addresses,
            Profile = BuildProfile(),
        };

            var created = await partiesClient.CreatePartyAsync(CompanyId, BranchId, createRequest, Token).ConfigureAwait(true);
            PartyId = created.Id;
            Party = created;
            ResetMode();
            await LoadLookupsAsync(Token).ConfigureAwait(true); // shows the account the server created and refreshes the plan
            NotifyProperties();
            return true;
        }

        if (Party is null || PartyId is null) return false;

        var updateRequest = new UpdatePartyRequest
        {
            ExpectedVersion = Party.VersionToken,
            NameArabic = DraftNameArabic.Trim(),
            NameEnglish = string.IsNullOrWhiteSpace(DraftNameEnglish) ? null : DraftNameEnglish.Trim(),
            Role = DraftRole,
            TaxNumber = string.IsNullOrWhiteSpace(DraftTaxNumber) ? null : DraftTaxNumber.Trim(),
            CommercialRegistration = string.IsNullOrWhiteSpace(DraftCommercialRegistration) ? null : DraftCommercialRegistration.Trim(),
            DefaultCurrencyCode = string.IsNullOrWhiteSpace(DraftDefaultCurrencyCode) ? null : DraftDefaultCurrencyCode.Trim().ToUpperInvariant(),
            ReceivableAccountId = DraftReceivableAccountId,
            PayableAccountId = DraftPayableAccountId,
            PolicyKind = DraftPolicyKind,
            IsCreditBlocked = DraftIsCreditBlocked,
            CreditLimit = string.IsNullOrWhiteSpace(DraftCreditLimit) ? "0.0000" : DraftCreditLimit.Trim(),
            PaymentTermDays = DraftPaymentTermDays,
            Contacts = contacts,
            Addresses = addresses,
            Profile = BuildProfile(),
        };

        var updated = await partiesClient.UpdatePartyAsync(CompanyId, BranchId, PartyId.Value, updateRequest, Token).ConfigureAwait(true);
        Party = updated;
        ResetMode();
        await LoadLookupsAsync(Token).ConfigureAwait(true);
        NotifyProperties();
        return true;
    }

    private async Task DeactivateAsync()
    {
        if (Party is null || PartyId is null || !CanManage) return;

        if (!await confirmation.ConfirmAsync("D_ConfirmDeactivateParty").ConfigureAwait(true)) return;

        await RunAsync(async ct =>
        {
            await partiesClient.DeactivatePartyAsync(CompanyId, BranchId, PartyId.Value, ct).ConfigureAwait(true);
            await LoadPartyAsync(ct).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    private async Task ReactivateAsync()
    {
        if (Party is null || PartyId is null || !CanManage) return;

        if (!await confirmation.ConfirmAsync("D_ConfirmReactivateParty").ConfigureAwait(true)) return;

        await RunAsync(async ct =>
        {
            await partiesClient.ReactivatePartyAsync(CompanyId, BranchId, PartyId.Value, ct).ConfigureAwait(true);
            await LoadPartyAsync(ct).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    private void NotifyProperties()
    {
        RefreshRelatedSources();
        OnPropertyChanged(nameof(Party));
        OnPropertyChanged(nameof(HasParty));
        OnPropertyChanged(nameof(IsCashCustomer));
        OnPropertyChanged(nameof(CanManage));
        OnPropertyChanged(nameof(CreditLimitCaption));
        OnPropertyChanged(nameof(PolicyHistory));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(Exposure));
        DeactivateCommand.NotifyCanExecuteChanged();
        ReactivateCommand.NotifyCanExecuteChanged();
        RefreshCommand.NotifyCanExecuteChanged();
        NotifyCardCommands();
    }
}

public sealed class PartyCardFactory(
    IPartiesClient partiesClient,
    IChartOfAccountsClient accountsClient,
    ShellNavigator navigator,
    DesktopOperation operation,
    ILocalizationService localization,
    IUserConfirmation confirmation,
    SalesDocumentLinksFactory links,
    Relationships.RelatedDocumentsFactory relations)
{
    public PartyCardViewModel Create() => new(partiesClient, accountsClient, navigator, operation, localization, confirmation)
    {
        DocumentLinks = links.Create(), PurchaseLinks = relations.Create(), ReceiptLinks = relations.Create(), PaymentLinks = relations.Create(),
    };
}

public sealed record PartyPolicyHistoryItem(DateTimeOffset? DateUtc, Guid? ActorId, string BeforeCreditLimit,
    string AfterCreditLimit, string BeforePolicy, string AfterPolicy, string BeforeBlocked, string AfterBlocked);
