using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;

namespace PetraERP.Desktop.Accounting;

public sealed partial class AccountCardViewModel
{
    public const int AuditPageSize = 25;
    [ObservableProperty] private int auditPage = 1;
    [ObservableProperty] private long auditTotalCount;
    [ObservableProperty] private AccountHistoryResponse? selectedAudit;
    public ObservableCollection<AccountHistoryResponse> AuditEvents { get; } = [];
    public ObservableCollection<AccountAuditField> AuditFields { get; } = [];
    public AsyncRelayCommand RefreshAuditCommand { get; private set; } = null!;
    public AsyncRelayCommand PreviousAuditCommand { get; private set; } = null!;
    public AsyncRelayCommand NextAuditCommand { get; private set; } = null!;
    public string AuditPageStatus => string.Format(CultureInfo.CurrentCulture, Localization["D_PageStatus"], AuditPage, AuditEvents.Count, AuditTotalCount);
    public bool HasLegacyAudit => SelectedAudit is { Before: null, After: null };
    public string AuditActor => SelectedAudit is not { } item ? string.Empty
        : (Localization.IsRightToLeft ? item.ActorNameArabic : item.ActorNameEnglish ?? item.ActorNameArabic) ?? item.ActorUserId.ToString("D");

    private bool CanReadAudit => HasContext && Idle && AccountId is not null && Can(ViewPermissionForAudit);
    private const string ViewPermissionForAudit = ChartOfAccountsViewModel.ViewPermission;

    private void InitializeAuditCommands()
    {
        RefreshAuditCommand = new AsyncRelayCommand(() => RunAsync(token => LoadAuditAsync(1, token)), () => CanReadAudit);
        PreviousAuditCommand = new AsyncRelayCommand(() => RunAsync(token => LoadAuditAsync(AuditPage - 1, token)), () => CanReadAudit && AuditPage > 1);
        NextAuditCommand = new AsyncRelayCommand(() => RunAsync(token => LoadAuditAsync(AuditPage + 1, token)), () => CanReadAudit && (long)AuditPage * AuditPageSize < AuditTotalCount);
    }

    private async Task LoadAuditAsync(int page, CancellationToken token)
    {
        if (AccountId is not { } id) return;
        var result = await accounts.GetAccountHistoryAsync(CompanyId, BranchId, id, page, AuditPageSize, token).ConfigureAwait(true);
        token.ThrowIfCancellationRequested();
        if (AccountId != id) return;
        SelectedAudit = null; AuditEvents.Clear();
        foreach (var item in result.Items) AuditEvents.Add(item);
        AuditPage = result.Page; AuditTotalCount = result.TotalCount;
        SelectedAudit = AuditEvents.FirstOrDefault();
        NotifyAudit();
    }

    private void ClearAudit()
    {
        SelectedAudit = null; AuditEvents.Clear(); AuditFields.Clear(); AuditPage = 1; AuditTotalCount = 0; NotifyAudit();
    }

    partial void OnSelectedAuditChanged(AccountHistoryResponse? value)
    {
        AuditFields.Clear();
        if (value is not null && !HasLegacyAudit)
        {
            Add("D_Code", state => state.Code);
            Add("D_ArabicName", state => state.NameArabic);
            Add("D_EnglishName", state => state.NameEnglish);
            Add("D_AccountType", state => Localization["D_AccountType_" + state.AccountType]);
            Add("D_PostingAllowed", state => Localization["D_Posting_" + state.PostingAllowed]);
            Add("D_CostCenterPolicy", state => Localization["D_CostPolicy_" + state.CostCenterPolicy]);
            Add("D_CurrencyPolicy", state => Localization["D_CurrencyMode_" + state.CurrencyMode]);
            Add("D_FixedCurrency", state => state.FixedCurrencyCode);
            Add("D_ParentAccount", state => state.ParentId?.ToString("D"));
            Add("D_AuditActive", state => Localization[state.IsActive ? "D_Active" : "D_Inactive"]);
        }
        OnPropertyChanged(nameof(HasLegacyAudit)); OnPropertyChanged(nameof(AuditActor));

        void Add(string key, Func<AccountStateResponse, string?> read)
        {
            var before = value?.Before is { } old ? read(old) : null;
            var after = value?.After is { } current ? read(current) : null;
            if (value is { Before: not null, After: not null } && before == after) return;
            AuditFields.Add(new(Localization[key], Format(value?.Before, before), Format(value?.After, after)));
        }
        string Format(AccountStateResponse? state, string? field) => state is null ? Localization["D_AuditNoState"]
            : field is null ? Localization["D_AuditNull"] : field.Length == 0 ? Localization["D_AuditEmpty"] : field;
    }

    private void NotifyAudit()
    {
        OnPropertyChanged(nameof(AuditPageStatus));
        RefreshAuditCommand?.NotifyCanExecuteChanged(); PreviousAuditCommand?.NotifyCanExecuteChanged(); NextAuditCommand?.NotifyCanExecuteChanged();
    }
}

public sealed record AccountAuditField(string Field, string Before, string After);
