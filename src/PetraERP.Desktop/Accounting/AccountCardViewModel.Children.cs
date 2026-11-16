using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;

namespace PetraERP.Desktop.Accounting;

public sealed partial class AccountCardViewModel
{
    public const int ChildrenPageSize = 25;
    [ObservableProperty] private int childrenPage = 1;
    [ObservableProperty] private long childrenTotalCount;
    public AsyncRelayCommand RefreshChildrenCommand { get; private set; } = null!;
    public AsyncRelayCommand PreviousChildrenCommand { get; private set; } = null!;
    public AsyncRelayCommand NextChildrenCommand { get; private set; } = null!;
    public string ChildrenPageStatus => string.Format(CultureInfo.CurrentCulture, Localization["D_PageStatus"], ChildrenPage, Children.Count, ChildrenTotalCount);
    private bool CanReadChildren => HasContext && Idle && AccountId is not null && Can(ChartOfAccountsViewModel.ViewPermission);

    private void InitializeChildrenCommands()
    {
        RefreshChildrenCommand = new AsyncRelayCommand(() => RunAsync(token => LoadChildrenAsync(1, token)), () => CanReadChildren);
        PreviousChildrenCommand = new AsyncRelayCommand(() => RunAsync(token => LoadChildrenAsync(ChildrenPage - 1, token)), () => CanReadChildren && ChildrenPage > 1);
        NextChildrenCommand = new AsyncRelayCommand(() => RunAsync(token => LoadChildrenAsync(ChildrenPage + 1, token)),
            () => CanReadChildren && (long)ChildrenPage * ChildrenPageSize < ChildrenTotalCount);
    }

    private async Task LoadChildrenAsync(int page, CancellationToken token)
    {
        if (AccountId is not { } id) return;
        var result = await accounts.GetAccountCardContextAsync(CompanyId, BranchId, id, page, ChildrenPageSize, token).ConfigureAwait(true);
        token.ThrowIfCancellationRequested();
        if (AccountId != id) return;
        // Paging is a read-only action, including during Edit/New: retain the draft's original account/version.
        ApplyChildren(result.Children);
    }

    private void ApplyChildren(AccountPageResponse page)
    {
        Children.Clear();
        foreach (var child in page.Items) Children.Add(child);
        ChildrenPage = page.Page; ChildrenTotalCount = page.TotalCount;
        NotifyChildren();
    }

    private void ClearChildren() { Children.Clear(); ChildrenPage = 1; ChildrenTotalCount = 0; NotifyChildren(); }
    private void NotifyChildren()
    {
        OnPropertyChanged(nameof(ChildrenPageStatus)); OnPropertyChanged(nameof(HasChildAccounts));
        RefreshChildrenCommand?.NotifyCanExecuteChanged(); PreviousChildrenCommand?.NotifyCanExecuteChanged(); NextChildrenCommand?.NotifyCanExecuteChanged();
    }
}
