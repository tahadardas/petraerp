using CommunityToolkit.Mvvm.Input;

namespace PetraERP.Desktop.Inventory;

/// <summary>Al-Ameen record navigation of the group card: first / previous / next / last group by code, saved records only.</summary>
public sealed partial class ItemGroupCardViewModel
{
    private AsyncRelayCommand? firstGroupCommand;
    private AsyncRelayCommand? previousGroupCommand;
    private AsyncRelayCommand? nextGroupCommand;
    private AsyncRelayCommand? lastGroupCommand;

    public bool CanNavigate => IsViewMode && HasContext && Idle && GroupId.HasValue;

    public AsyncRelayCommand FirstGroupCommand => firstGroupCommand ??= new(() => MoveAsync(_ => 0), () => CanNavigate);
    public AsyncRelayCommand PreviousGroupCommand => previousGroupCommand ??= new(() => MoveAsync(index => index - 1), () => CanNavigate);
    public AsyncRelayCommand NextGroupCommand => nextGroupCommand ??= new(() => MoveAsync(index => index + 1), () => CanNavigate);
    public AsyncRelayCommand LastGroupCommand => lastGroupCommand ??= new(() => MoveAsync(_ => int.MaxValue), () => CanNavigate);

    public string RecordCode => DraftCode;

    private async Task MoveAsync(Func<int, int> target)
    {
        await RunAsync(async token =>
        {
            if (GroupId is not { } current) return;
            var groups = await inventoryClient.ListItemGroupsAsync(CompanyId, BranchId, token).ConfigureAwait(true);
            var ids = groups.Where(g => g.Id is not null).OrderBy(g => g.Code, StringComparer.Ordinal).Select(g => g.Id!.Value).ToList();
            if (ids.Count == 0) return;
            var next = Math.Clamp(target(Math.Max(ids.IndexOf(current), 0)), 0, ids.Count - 1);
            if (ids[next] == current) return;
            GroupId = ids[next];
            await LoadExistingGroupAsync(ids[next], token).ConfigureAwait(true);
        }).ConfigureAwait(true);
        NotifyNavigation();
    }

    private void NotifyNavigation()
    {
        OnPropertyChanged(nameof(CanNavigate));
        OnPropertyChanged(nameof(RecordCode));
        firstGroupCommand?.NotifyCanExecuteChanged();
        previousGroupCommand?.NotifyCanExecuteChanged();
        nextGroupCommand?.NotifyCanExecuteChanged();
        lastGroupCommand?.NotifyCanExecuteChanged();
    }
}
