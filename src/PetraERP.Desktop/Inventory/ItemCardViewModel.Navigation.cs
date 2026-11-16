using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;

namespace PetraERP.Desktop.Inventory;

/// <summary>Al-Ameen record navigation (first / previous / next / last by item code) and the conditional delete of the item card.</summary>
public sealed partial class ItemCardViewModel
{
    private const int NavigationPageSize = 200;
    private List<Guid>? navigationIds;
    private AsyncRelayCommand? firstItemCommand;
    private AsyncRelayCommand? previousItemCommand;
    private AsyncRelayCommand? nextItemCommand;
    private AsyncRelayCommand? lastItemCommand;
    private AsyncRelayCommand? deleteItemCommand;

    /// <summary>Navigation moves between saved records only: never while a draft is open, so no unsaved edit can be lost.</summary>
    public bool CanNavigate => IsViewMode && HasContext && Idle && ItemId is not null;

    public AsyncRelayCommand FirstItemCommand => firstItemCommand ??= new(() => MoveAsync(_ => 0), () => CanNavigate);
    public AsyncRelayCommand PreviousItemCommand => previousItemCommand ??= new(() => MoveAsync(index => index - 1), () => CanNavigate);
    public AsyncRelayCommand NextItemCommand => nextItemCommand ??= new(() => MoveAsync(index => index + 1), () => CanNavigate);
    public AsyncRelayCommand LastItemCommand => lastItemCommand ??= new(() => MoveAsync(_ => int.MaxValue), () => CanNavigate);

    /// <summary>Delete is offered only for a saved item with no stock movement; the server repeats the check and also refuses an item used by a document.</summary>
    public bool CanDeleteItem => CanManage && IsViewMode && Item is not null && Idle && Movements.Count == 0;

    public AsyncRelayCommand DeleteItemCommand => deleteItemCommand ??= new(DeleteCurrentItemAsync, () => CanDeleteItem);

    /// <summary>"رقم السجل" of the navigator: the item code.</summary>
    public string RecordCode => Item?.Code ?? DraftCode;

    private async Task<List<Guid>> EnsureNavigationIdsAsync(CancellationToken token)
    {
        if (navigationIds is not null) return navigationIds;
        var ids = new List<Guid>();
        for (var page = 1; ; page++)
        {
            var response = await inventoryClient.ListItemsAsync(CompanyId, BranchId, page, NavigationPageSize, null, null, null, null, token).ConfigureAwait(true);
            var items = response.Items ?? [];
            ids.AddRange(items.Where(i => i.Id is not null).Select(i => i.Id!.Value));
            if (items.Count == 0 || ids.Count >= (response.TotalCount ?? 0)) break;
        }

        return navigationIds = ids;
    }

    private async Task MoveAsync(Func<int, int> target)
    {
        await RunAsync(async token =>
        {
            var ids = await EnsureNavigationIdsAsync(token).ConfigureAwait(true);
            if (ids.Count == 0 || ItemId is not { } current) return;
            var index = ids.IndexOf(current);
            var next = Math.Clamp(target(Math.Max(index, 0)), 0, ids.Count - 1);
            if (ids[next] == current) return;
            ItemId = ids[next];
            linkedMovementId = null;
            await LoadItemAsync(token).ConfigureAwait(true);
        }).ConfigureAwait(true);
        NotifyNavigation();
    }

    private async Task DeleteCurrentItemAsync()
    {
        if (ItemId is not { } id || !HasContext || !CanDeleteItem) return;
        if (!await confirmation.ConfirmAsync("D_DeleteItemPrompt").ConfigureAwait(true)) return;
        var deleted = await RunAsync(async token =>
        {
            await inventoryClient.DeleteItemAsync(CompanyId, BranchId, id, token).ConfigureAwait(true);
            var ids = await EnsureNavigationIdsAsync(token).ConfigureAwait(true);
            var index = ids.IndexOf(id);
            ids.Remove(id);
            if (ids.Count == 0)
            {
                await navigator.NavigateAsync("items").ConfigureAwait(true);
                return;
            }

            // Stay in the card: show the record that followed the deleted one (or the last one).
            ItemId = ids[Math.Clamp(index, 0, ids.Count - 1)];
            linkedMovementId = null;
            await LoadItemAsync(token).ConfigureAwait(true);
        }).ConfigureAwait(true);
        if (deleted) NotifyNavigation();
    }

    /// <summary>A saved item changes the ordered list, so the cached ids are rebuilt on the next move.</summary>
    private void ForgetNavigation() => navigationIds = null;

    private void NotifyNavigation()
    {
        OnPropertyChanged(nameof(CanNavigate));
        OnPropertyChanged(nameof(CanDeleteItem));
        OnPropertyChanged(nameof(RecordCode));
        firstItemCommand?.NotifyCanExecuteChanged();
        previousItemCommand?.NotifyCanExecuteChanged();
        nextItemCommand?.NotifyCanExecuteChanged();
        lastItemCommand?.NotifyCanExecuteChanged();
        deleteItemCommand?.NotifyCanExecuteChanged();
    }
}
