using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Parties;

public sealed partial class PartySummaryItem : ObservableObject
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string NameArabic { get; init; } = string.Empty;
    public string? NameEnglish { get; init; }
    public PartyRole Role { get; init; }
    public bool IsActive { get; set; }
    public bool IsCashCustomer { get; init; }
    public bool IsCreditBlocked { get; init; }
    public string ReceivableBalance { get; init; } = "0.0000";
    public string PayableBalance { get; init; } = "0.0000";
    public string? TaxNumber { get; init; }
    public string DefaultCurrencyCode { get; init; } = string.Empty;
    public string CreditLimit { get; init; } = "0.0000";
    public int? PaymentTermDays { get; init; }
    public CreditPolicyKind PolicyKind { get; init; }
    public string CurrentBalance { get; set; } = "0.0000";
    public string AvailableCredit { get; set; } = "0.0000";

    public string RoleDisplay => Role switch
    {
        PartyRole.Customer => "زبون",
        PartyRole.Supplier => "مورد",
        PartyRole.Both => "زبون ومورد",
        _ => Role.ToString(),
    };

    public string PolicyDisplay => PolicyKind switch
    {
        CreditPolicyKind.AllowWithWarning => "سماح مع تحذير",
        CreditPolicyKind.BlockOnExceed => "حظر عند التجاوز",
        CreditPolicyKind.NoLimit => "بدون سقف",
        _ => PolicyKind.ToString(),
    };
}

public sealed partial class PartiesViewModel : PageViewModel, IShellPage
{
    public const string ViewPermission = "Parties.Directory.View";
    public const string ManagePermission = "Parties.Directory.Manage";

    private readonly IPartiesClient partiesClient;
    private readonly ShellNavigator navigator;
    private readonly IUserConfirmation confirmation;

    [ObservableProperty] private string search = string.Empty;
    [ObservableProperty] private string filterRole = "all";
    [ObservableProperty] private bool activeOnly;
    [ObservableProperty] private PartySummaryItem? selectedParty;
    [ObservableProperty] private int totalCount;

    public PartiesViewModel(
        IPartiesClient partiesClient,
        ShellNavigator navigator,
        DesktopOperation operation,
        ILocalizationService localization,
        IUserConfirmation confirmation)
        : base(operation, localization)
    {
        this.partiesClient = partiesClient;
        this.navigator = navigator;
        this.confirmation = confirmation;

        RefreshCommand = new AsyncRelayCommand(LoadPartiesAsync, () => HasContext && Idle);
        OpenCardCommand = new AsyncRelayCommand(OpenSelectedCardAsync, () => SelectedParty is not null && Idle);
        NewPartyCommand = new AsyncRelayCommand(NewPartyAsync, () => HasContext && Idle && CanManage);
        ToggleActiveCommand = new AsyncRelayCommand(ToggleSelectedActiveAsync, () => SelectedParty is { IsCashCustomer: false } && Idle && CanManage);
    }

    protected override string RequiredPermission => ViewPermission;
    public bool CanManage => Can(ManagePermission);

    public ObservableCollection<PartySummaryItem> Parties { get; } = [];

    public IReadOnlyList<PartySummaryItem> VisibleParties => Parties
        .Where(MatchesFilter)
        .ToList();

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand OpenCardCommand { get; }
    public AsyncRelayCommand NewPartyCommand { get; }
    public AsyncRelayCommand ToggleActiveCommand { get; }

    protected override Task LoadAsync(object? parameter, CancellationToken cancellationToken)
    {
        NotifyPermissions();
        return LoadPartiesCoreAsync(cancellationToken);
    }

    protected override void ClearPage()
    {
        Parties.Clear();
        SelectedParty = null;
        Search = string.Empty;
        FilterRole = "all";
        ActiveOnly = false;
        TotalCount = 0;
        OnPropertyChanged(nameof(VisibleParties));
    }

    public void Localize()
    {
        OnPropertyChanged(nameof(VisibleParties));
        NotifyCommands();
    }

    partial void OnSearchChanged(string value) => RunSearch();
    partial void OnFilterRoleChanged(string value) => RunSearch();
    partial void OnActiveOnlyChanged(bool value) => RunSearch();
    partial void OnSelectedPartyChanged(PartySummaryItem? value) => NotifyCommands();

    private void RunSearch()
    {
        if (HasContext)
        {
            _ = LoadPartiesAsync();
        }
        else
        {
            OnPropertyChanged(nameof(VisibleParties));
        }
    }

    protected override void NotifyCommands()
    {
        OpenCardCommand?.NotifyCanExecuteChanged();
        NewPartyCommand?.NotifyCanExecuteChanged();
        ToggleActiveCommand?.NotifyCanExecuteChanged();
    }

    private void NotifyPermissions()
    {
        OnPropertyChanged(nameof(CanManage));
        NotifyCommands();
    }

    private bool MatchesFilter(PartySummaryItem item)
    {
        if (ActiveOnly && !item.IsActive) return false;

        if (FilterRole == "customer" && item.Role != PartyRole.Customer) return false;
        if (FilterRole == "supplier" && item.Role != PartyRole.Supplier) return false;
        if (FilterRole == "both" && item.Role != PartyRole.Both) return false;

        return true;
    }

    private Task<bool> LoadPartiesAsync() => RunAsync(LoadPartiesCoreAsync);

    private async Task LoadPartiesCoreAsync(CancellationToken ct)
    {
        if (!HasContext) return;

        PartyRole? roleFilter = FilterRole switch
        {
            "customer" => PartyRole.Customer,
            "supplier" => PartyRole.Supplier,
            "both" => PartyRole.Both,
            _ => null,
        };

        var response = await partiesClient.ListPartiesAsync(
            CompanyId,
            BranchId,
            page: 1,
            pageSize: 200,
            search: string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
            role: roleFilter,
            isActive: ActiveOnly ? true : null,
            ct).ConfigureAwait(true);

        Parties.Clear();
        if (response.Items is not null)
        {
            foreach (var item in response.Items)
            {
                Parties.Add(new PartySummaryItem
                {
                    Id = item.Id ?? Guid.Empty,
                    Code = item.Code ?? string.Empty,
                    NameArabic = item.NameArabic ?? string.Empty,
                    NameEnglish = item.NameEnglish,
                    Role = item.Role ?? PartyRole.Customer,
                    IsActive = item.IsActive ?? false,
                    IsCashCustomer = item.IsCashCustomer == true,
                    IsCreditBlocked = item.IsCreditBlocked == true,
                    ReceivableBalance = item.ReceivableBalance ?? "0.0000",
                    PayableBalance = item.PayableBalance ?? "0.0000",
                    TaxNumber = item.TaxNumber,
                    DefaultCurrencyCode = item.DefaultCurrencyCode ?? string.Empty,
                    CreditLimit = item.CreditLimit ?? "0.0000",
                    PaymentTermDays = item.PaymentTermDays,
                    PolicyKind = item.PolicyKind ?? CreditPolicyKind.AllowWithWarning,
                    CurrentBalance = item.CurrentBalance ?? "0.0000",
                    AvailableCredit = item.AvailableCredit ?? "0.0000",
                });
            }
        }

        TotalCount = (int)(response.TotalCount ?? 0);
        OnPropertyChanged(nameof(VisibleParties));
        NotifyCommands();
    }

    private async Task OpenSelectedCardAsync()
    {
        if (SelectedParty is null) return;
        await navigator.NavigateAsync("party-card", new OpenPartyCardRequest(SelectedParty.Id)).ConfigureAwait(true);
    }

    private async Task NewPartyAsync()
    {
        if (!CanManage) return;
        await navigator.NavigateAsync("party-card", new NewPartyCardRequest()).ConfigureAwait(true);
    }

    private async Task ToggleSelectedActiveAsync()
    {
        if (SelectedParty is null || SelectedParty.IsCashCustomer || !CanManage) return;

        var party = SelectedParty;
        if (party.IsActive)
        {
            if (!await confirmation.ConfirmAsync("D_ConfirmDeactivateParty").ConfigureAwait(true)) return;
            await Operation.RunAsync(async () =>
            {
                await partiesClient.DeactivatePartyAsync(CompanyId, BranchId, party.Id, CancellationToken.None).ConfigureAwait(true);
                party.IsActive = false;
                OnPropertyChanged(nameof(VisibleParties));
                NotifyCommands();
            }).ConfigureAwait(true);
        }
        else
        {
            if (!await confirmation.ConfirmAsync("D_ConfirmReactivateParty").ConfigureAwait(true)) return;
            await Operation.RunAsync(async () =>
            {
                await partiesClient.ReactivatePartyAsync(CompanyId, BranchId, party.Id, CancellationToken.None).ConfigureAwait(true);
                party.IsActive = true;
                OnPropertyChanged(nameof(VisibleParties));
                NotifyCommands();
            }).ConfigureAwait(true);
        }
    }
}
