using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.Desktop.Controls;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Assets;

/// <summary>
/// Workstation for calculating and posting periodic depreciation runs.
/// </summary>
public sealed partial class AssetDepreciationRunViewModel : PageViewModel
{
    private readonly IAssetsClient client;
    private readonly NotificationCenter notifications;

    [ObservableProperty] private int fiscalYear = DateTime.Today.Year;
    [ObservableProperty] private int periodNumber = DateTime.Today.Month;
    [ObservableProperty] private string voucherNumber = $"DEP-{DateTime.Today.Year}-{DateTime.Today.Month:D2}";
    [ObservableProperty] private string description = $"إهلاك الأصول الدوري لشهر {DateTime.Today.Month} لسنة {DateTime.Today.Year}";
    [ObservableProperty] private decimal totalDepreciationAmount;
    [ObservableProperty] private string totalDepreciationWords = string.Empty;
    [ObservableProperty] private bool isPosted;

    public ObservableCollection<AssetDepreciationRunLineDto> Lines { get; } = [];

    public AssetDepreciationRunViewModel(IAssetsClient client, NotificationCenter notifications, DesktopOperation operation, ILocalizationService localization)
        : base(operation, localization)
    {
        this.client = client;
        this.notifications = notifications;

        CalculateCommand = new AsyncRelayCommand(CalculateAsync);
        PostJournalCommand = new AsyncRelayCommand(PostJournalAsync, () => Lines.Count > 0 && !IsPosted);
    }

    protected override string RequiredPermission => "Ledger.Journals.Manage";

    public IAsyncRelayCommand CalculateCommand { get; }
    public IAsyncRelayCommand PostJournalCommand { get; }

    protected override async Task LoadAsync(object? parameter, CancellationToken cancellationToken)
    {
        await CalculateAsync();
    }

    private async Task CalculateAsync()
    {
        var run = await client.CalculateDepreciationRunAsync(FiscalYear, PeriodNumber);
        Lines.Clear();
        foreach (var line in run.Lines) Lines.Add(line);

        TotalDepreciationAmount = run.TotalDepreciationAmount;
        TotalDepreciationWords = ArabicTafqeet.ToWordsOrEmpty(TotalDepreciationAmount, "SYP");
        VoucherNumber = run.VoucherNumber;
        Description = run.Description;
        IsPosted = false;

        PostJournalCommand.NotifyCanExecuteChanged();
    }

    private async Task PostJournalAsync()
    {
        if (Lines.Count == 0 || IsPosted) return;

        await client.PostDepreciationRunAsync(Guid.NewGuid());
        IsPosted = true;
        notifications.Text(NotificationKind.Success, "تم ترحيل الاهتلاك", $"تم توليد وترحيل سند قيد إهلاك الأصول برقم {VoucherNumber} ومبلغ {TotalDepreciationAmount:N2}");
        PostJournalCommand.NotifyCanExecuteChanged();
    }

    protected override void ClearPage()
    {
        Lines.Clear();
        IsPosted = false;
    }
}
