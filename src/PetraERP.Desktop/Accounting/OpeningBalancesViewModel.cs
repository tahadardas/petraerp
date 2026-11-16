using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Connection;
using PetraERP.Desktop.Controls;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Accounting;

public sealed partial class OpeningBalancesViewModel : PageViewModel
{
    protected override string RequiredPermission => "Ledger.Journals.View";

    private readonly DesktopSession session;
    private readonly IUserConfirmation confirmation;

    [ObservableProperty] private OpeningBalanceBatchResponse? selectedBatch;
    [ObservableProperty] private string actionReason = string.Empty;
    [ObservableProperty] private string statusMessage = string.Empty;

    public ObservableCollection<OpeningBalanceBatchResponse> Batches { get; } = [];

    public bool HasSelectedBatch => SelectedBatch is not null;
    public bool CanValidate => HasContext && Idle && SelectedBatch is not null && SelectedBatch.ApprovalState == 0;
    public bool CanApprove => HasContext && Idle && Can("Ledger.Journals.Approve") && SelectedBatch is not null && SelectedBatch.ApprovalState == 0;
    public bool CanPost => HasContext && Idle && Can("Ledger.Journals.Post") && SelectedBatch is not null && SelectedBatch.ApprovalState == 1;
    public bool CanReverse => HasContext && Idle && Can("Ledger.Journals.Reverse") && SelectedBatch is not null && SelectedBatch.ApprovalState == 2;

    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand ValidateCommand { get; }
    public IAsyncRelayCommand ApproveCommand { get; }
    public IAsyncRelayCommand PostCommand { get; }
    public IAsyncRelayCommand ReverseCommand { get; }

    public OpeningBalancesViewModel(
        DesktopSession session,
        IUserConfirmation confirmation,
        DesktopOperation operation,
        ILocalizationService localization) : base(operation, localization)
    {
        this.session = session;
        this.confirmation = confirmation;

        RefreshCommand = new AsyncRelayCommand(LoadBatchesAsync, () => HasContext && Idle);
        ValidateCommand = new AsyncRelayCommand(ValidateBatchAsync, () => CanValidate);
        ApproveCommand = new AsyncRelayCommand(ApproveBatchAsync, () => CanApprove);
        PostCommand = new AsyncRelayCommand(PostBatchAsync, () => CanPost);
        ReverseCommand = new AsyncRelayCommand(ReverseBatchAsync, () => CanReverse);
    }

    public async Task LoadBatchesAsync()
    {
        if (!HasContext) return;
        await RunAsync(async token =>
        {
            var list = await session.Api.ListOpeningBalanceBatchesAsync(CompanyId, BranchId, token).ConfigureAwait(false);
            Batches.Clear();
            foreach (var b in list) Batches.Add(b);
            SelectedBatch = Batches.FirstOrDefault();
            NotifyCommands();
        }).ConfigureAwait(false);
    }

    private async Task ValidateBatchAsync()
    {
        if (SelectedBatch is null) return;
        await RunAsync(async token =>
        {
            var updated = await session.Api.ValidateOpeningBalanceBatchAsync(CompanyId, BranchId, SelectedBatch.Id, token).ConfigureAwait(false);
            UpdateBatch(updated);
            StatusMessage = Localization["ACC_BatchValidated"];
        }).ConfigureAwait(false);
    }

    private async Task ApproveBatchAsync()
    {
        if (SelectedBatch is null) return;
        var reason = ActionReason.Trim();
        if (string.IsNullOrWhiteSpace(reason)) reason = "اعتماد الرصيد الافتتاحي";

        await RunAsync(async token =>
        {
            var updated = await session.Api.ApproveOpeningBalanceBatchAsync(CompanyId, BranchId, SelectedBatch.Id, new ApproveOpeningBalanceRequest { Reason = reason }, token).ConfigureAwait(false);
            UpdateBatch(updated);
            StatusMessage = Localization["ACC_BatchApproved"];
            ActionReason = string.Empty;
        }).ConfigureAwait(false);
    }

    private async Task PostBatchAsync()
    {
        if (SelectedBatch is null) return;
        var reason = ActionReason.Trim();
        if (string.IsNullOrWhiteSpace(reason)) reason = "ترحيل الرصيد الافتتاحي";

        await RunAsync(async token =>
        {
            var updated = await session.Api.PostOpeningBalanceBatchAsync(CompanyId, BranchId, SelectedBatch.Id, new PostOpeningBalanceRequest { Reason = reason }, token).ConfigureAwait(false);
            UpdateBatch(updated);
            StatusMessage = Localization["ACC_BatchPosted"];
            ActionReason = string.Empty;
        }).ConfigureAwait(false);
    }

    private async Task ReverseBatchAsync()
    {
        if (SelectedBatch is null) return;
        if (!await confirmation.ConfirmAsync("D_ReverseConfirmMessage").ConfigureAwait(false)) return;

        var reason = ActionReason.Trim();
        if (string.IsNullOrWhiteSpace(reason)) reason = "عكس الرصيد الافتتاحي";

        await RunAsync(async token =>
        {
            var updated = await session.Api.ReverseOpeningBalanceBatchAsync(CompanyId, BranchId, SelectedBatch.Id, new ReverseOpeningBalanceRequest { Reason = reason }, token).ConfigureAwait(false);
            UpdateBatch(updated);
            StatusMessage = Localization["ACC_BatchReversed"];
            ActionReason = string.Empty;
        }).ConfigureAwait(false);
    }

    private void UpdateBatch(OpeningBalanceBatchResponse batch)
    {
        var idx = Batches.IndexOf(Batches.FirstOrDefault(b => b.Id == batch.Id)!);
        if (idx >= 0) Batches[idx] = batch;
        SelectedBatch = batch;
        NotifyCommands();
    }

    protected override Task LoadAsync(object? parameter, CancellationToken cancellationToken) => LoadBatchesAsync();

    public void Localize() => NotifyCommands();

    protected override void ClearPage()
    {
        Batches.Clear();
        SelectedBatch = null;
        ActionReason = string.Empty;
        StatusMessage = string.Empty;
    }

    protected override void NotifyCommands()
    {
        base.NotifyCommands();
        ValidateCommand.NotifyCanExecuteChanged();
        ApproveCommand.NotifyCanExecuteChanged();
        PostCommand.NotifyCanExecuteChanged();
        ReverseCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanValidate));
        OnPropertyChanged(nameof(CanApprove));
        OnPropertyChanged(nameof(CanPost));
        OnPropertyChanged(nameof(CanReverse));
    }

    partial void OnSelectedBatchChanged(OpeningBalanceBatchResponse? value)
    {
        NotifyCommands();
    }
}
