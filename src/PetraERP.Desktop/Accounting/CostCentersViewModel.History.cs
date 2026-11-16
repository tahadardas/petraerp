using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;

namespace PetraERP.Desktop.Accounting;

public sealed partial class CostCentersViewModel
{
    public const int HistoryPageSize = 25;
    [ObservableProperty] private int historyPage = 1;
    [ObservableProperty] private long historyTotalCount;
    [ObservableProperty] private CostCenterHistoryResponse? selectedHistory;
    public ObservableCollection<CostCenterHistoryResponse> History { get; } = [];
    public ObservableCollection<CostCenterAuditField> HistoryFields { get; } = [];
    public AsyncRelayCommand RefreshHistoryCommand { get; private set; } = null!;
    public AsyncRelayCommand PreviousHistoryCommand { get; private set; } = null!;
    public AsyncRelayCommand NextHistoryCommand { get; private set; } = null!;
    public string HistoryPageStatus => string.Format(Localization.Culture, Localization["D_PageStatus"], HistoryPage, History.Count, HistoryTotalCount);
    public string HistoryActor => SelectedHistory is not { } item ? string.Empty
        : (Localization.IsRightToLeft ? item.ActorNameArabic : item.ActorNameEnglish ?? item.ActorNameArabic) ?? item.ActorUserId.ToString("D");
    private bool CanReadHistory => HasContext && Idle && editing is not null && !IsNew && Can(RequiredPermission);

    private void InitializeHistoryCommands()
    {
        RefreshHistoryCommand = new AsyncRelayCommand(() => RunAsync(token => LoadHistoryAsync(1, token)), () => CanReadHistory);
        PreviousHistoryCommand = new AsyncRelayCommand(() => RunAsync(token => LoadHistoryAsync(HistoryPage - 1, token)), () => CanReadHistory && HistoryPage > 1);
        NextHistoryCommand = new AsyncRelayCommand(() => RunAsync(token => LoadHistoryAsync(HistoryPage + 1, token)),
            () => CanReadHistory && (long)HistoryPage * HistoryPageSize < HistoryTotalCount);
    }

    private async Task LoadHistoryAsync(int page, CancellationToken token)
    {
        if (editing is not { } target) return;
        var result = await client.GetCostCenterHistoryAsync(CompanyId, BranchId, target.Id, page, HistoryPageSize, token).ConfigureAwait(true);
        token.ThrowIfCancellationRequested();
        if (editing?.Id != target.Id || IsNew) return;
        SelectedHistory = null; History.Clear();
        foreach (var item in result.Items) History.Add(item);
        HistoryPage = result.Page; HistoryTotalCount = result.TotalCount; SelectedHistory = History.FirstOrDefault();
        // Read-only history does not replace editing or any draft field/version.
        NotifyHistory();
    }

    private void ClearHistory()
    {
        SelectedHistory = null; History.Clear(); HistoryFields.Clear(); HistoryPage = 1; HistoryTotalCount = 0; NotifyHistory();
    }

    partial void OnSelectedHistoryChanged(CostCenterHistoryResponse? value)
    {
        HistoryFields.Clear();
        if (value is not null)
        {
            Add("D_Code", state => state.Code);
            Add("D_ArabicName", state => state.NameArabic);
            Add("D_EnglishName", state => state.NameEnglish);
            Add("D_AuditActive", state => Localization[state.IsActive ? "D_Active" : "D_Inactive"]);
        }
        OnPropertyChanged(nameof(HistoryActor));
        void Add(string key, Func<CostCenterStateResponse, string?> read)
        {
            var before = value?.Before is { } old ? read(old) : null;
            var after = value?.After is { } current ? read(current) : null;
            if (value?.Before is not null && before == after) return;
            HistoryFields.Add(new(Localization[key], Format(value?.Before, before), Format(value?.After, after)));
        }
        string Format(CostCenterStateResponse? state, string? field) => state is null ? Localization["D_AuditNoState"]
            : field is null ? Localization["D_AuditNull"] : field.Length == 0 ? Localization["D_AuditEmpty"] : field;
    }

    private void NotifyHistory()
    {
        OnPropertyChanged(nameof(HistoryPageStatus));
        RefreshHistoryCommand?.NotifyCanExecuteChanged(); PreviousHistoryCommand?.NotifyCanExecuteChanged(); NextHistoryCommand?.NotifyCanExecuteChanged();
    }
}

public sealed record CostCenterAuditField(string Field, string Before, string After);
