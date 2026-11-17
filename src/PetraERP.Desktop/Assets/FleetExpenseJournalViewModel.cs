using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.Contracts.Assets;
using PetraERP.Desktop.Controls;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Assets;

/// <summary>
/// Row item for the rapid batch fleet expense & maintenance journal.
/// </summary>
public sealed partial class FleetExpenseRowItem : ObservableObject
{
    [ObservableProperty] private int lineNumber;
    [ObservableProperty] private AssetDto? asset;
    [ObservableProperty] private MaintenanceExpenseType expenseType = MaintenanceExpenseType.OilAndFilterChange;
    [ObservableProperty] private decimal amount;
    [ObservableProperty] private decimal currentMeterReading;
    [ObservableProperty] private string description = string.Empty;
    [ObservableProperty] private string paymentAccountName = string.Empty;
    [ObservableProperty] private Guid? paymentAccountId;
    [ObservableProperty] private string invoiceRef = string.Empty;
    [ObservableProperty] private bool isCapitalized;
}

/// <summary>
/// Rapid bulk journal entry screen for vehicle and machinery maintenance, oil changes, and operating expenses.
/// </summary>
public sealed partial class FleetExpenseJournalViewModel : PageViewModel
{
    private readonly IAssetsClient client;
    private readonly NotificationCenter notifications;

    [ObservableProperty] private string voucherNumber = "FLT-" + DateTime.Now.ToString("yyyyMMdd-HHmm");
    [ObservableProperty] private DateTime transactionDate = DateTime.Today;
    [ObservableProperty] private string notes = "مصاريف وصيانة أسطول الآليات والمركبات";
    [ObservableProperty] private decimal totalAmount;
    [ObservableProperty] private string totalAmountWords = string.Empty;

    // Accounts & Assets Lookups
    [ObservableProperty] private (Guid Id, string Code, string Name)? selectedDefaultAccount;
    public ObservableCollection<(Guid Id, string Code, string Name)> PaymentAccounts { get; } = [];
    public ObservableCollection<AssetDto> AvailableAssets { get; } = [];
    public ObservableCollection<FleetExpenseRowItem> Rows { get; } = [];

    public FleetExpenseJournalViewModel(IAssetsClient client, NotificationCenter notifications, DesktopOperation operation, ILocalizationService localization)
        : base(operation, localization)
    {
        this.client = client;
        this.notifications = notifications;

        AddRowCommand = new RelayCommand(AddEmptyRow);
        RemoveRowCommand = new RelayCommand<FleetExpenseRowItem>(RemoveRow);
        SaveBatchCommand = new AsyncRelayCommand(SaveBatchAsync, () => Rows.Any(r => r.Amount > 0 && r.Asset != null));
        ClearCommand = new RelayCommand(ClearForm);
    }

    protected override string RequiredPermission => "Ledger.Journals.Manage";

    public IRelayCommand AddRowCommand { get; }
    public IRelayCommand<FleetExpenseRowItem> RemoveRowCommand { get; }
    public IAsyncRelayCommand SaveBatchCommand { get; }
    public IRelayCommand ClearCommand { get; }

    protected override async Task LoadAsync(object? parameter, CancellationToken cancellationToken)
    {
        var accounts = await client.ListPaymentAccountsAsync(cancellationToken);
        PaymentAccounts.Clear();
        foreach (var acc in accounts) PaymentAccounts.Add(acc);

        SelectedDefaultAccount = PaymentAccounts.FirstOrDefault();

        var assets = await client.ListAssetsAsync(cancellationToken);
        AvailableAssets.Clear();
        foreach (var a in assets) AvailableAssets.Add(a);

        if (Rows.Count == 0)
        {
            InitializeSampleRows();
        }
        RecalculateTotals();
    }

    private void InitializeSampleRows()
    {
        Rows.Clear();
        var loader = AvailableAssets.FirstOrDefault(a => a.AssetCode.Contains("MC"));
        var truck = AvailableAssets.FirstOrDefault(a => a.AssetCode.Contains("TR"));
        var excavator = AvailableAssets.FirstOrDefault(a => a.AssetCode.Contains("EX"));

        if (loader != null)
        {
            Rows.Add(new FleetExpenseRowItem
            {
                LineNumber = 1,
                Asset = loader,
                ExpenseType = MaintenanceExpenseType.OilAndFilterChange,
                Amount = 650m,
                CurrentMeterReading = (loader.MachineryDetails?.CurrentMeterReading ?? 3400m) + 20m,
                Description = "تغيير زيت المحرك وفلتر أصلي",
                PaymentAccountId = SelectedDefaultAccount?.Id,
                PaymentAccountName = SelectedDefaultAccount?.Name ?? string.Empty
            });
        }

        if (truck != null)
        {
            Rows.Add(new FleetExpenseRowItem
            {
                LineNumber = 2,
                Asset = truck,
                ExpenseType = MaintenanceExpenseType.TiresAndBatteries,
                Amount = 1200m,
                CurrentMeterReading = (truck.MachineryDetails?.CurrentMeterReading ?? 142000m) + 500m,
                Description = "شراء إطار خلفي مقاس 24 مع التركيب",
                PaymentAccountId = SelectedDefaultAccount?.Id,
                PaymentAccountName = SelectedDefaultAccount?.Name ?? string.Empty,
                InvoiceRef = "INV-TYRE-992"
            });
        }

        if (excavator != null)
        {
            Rows.Add(new FleetExpenseRowItem
            {
                LineNumber = 3,
                Asset = excavator,
                ExpenseType = MaintenanceExpenseType.HydraulicRepair,
                Amount = 3400m,
                CurrentMeterReading = (excavator.MachineryDetails?.CurrentMeterReading ?? 8100m) + 10m,
                Description = "تبديل بستم هيدروليك مع خراطيم ضغط عالي",
                PaymentAccountId = PaymentAccounts.Count > 4 ? PaymentAccounts[4].Id : SelectedDefaultAccount?.Id, // بنك أو صندوق آخر
                PaymentAccountName = PaymentAccounts.Count > 4 ? PaymentAccounts[4].Name : (SelectedDefaultAccount?.Name ?? string.Empty)
            });
        }
    }

    public void AddEmptyRow()
    {
        var nextNum = Rows.Count + 1;
        var newRow = new FleetExpenseRowItem
        {
            LineNumber = nextNum,
            Asset = AvailableAssets.FirstOrDefault(),
            ExpenseType = MaintenanceExpenseType.OilAndFilterChange,
            Amount = 0m,
            CurrentMeterReading = 0m,
            Description = string.Empty,
            PaymentAccountId = SelectedDefaultAccount?.Id,
            PaymentAccountName = SelectedDefaultAccount?.Name ?? string.Empty
        };
        newRow.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FleetExpenseRowItem.Amount)) RecalculateTotals();
        };
        Rows.Add(newRow);
        RecalculateTotals();
    }

    private void RemoveRow(FleetExpenseRowItem? row)
    {
        if (row != null)
        {
            Rows.Remove(row);
            ReindexRows();
            RecalculateTotals();
        }
    }

    private void ReindexRows()
    {
        for (int i = 0; i < Rows.Count; i++)
        {
            Rows[i].LineNumber = i + 1;
        }
    }

    public void RecalculateTotals()
    {
        TotalAmount = Rows.Sum(r => r.Amount);
        TotalAmountWords = ArabicTafqeet.ToWordsOrEmpty(TotalAmount, "SYP");
        SaveBatchCommand.NotifyCanExecuteChanged();
    }

    private async Task SaveBatchAsync()
    {
        var validRows = Rows.Where(r => r.Asset != null && r.Amount > 0).ToList();
        if (validRows.Count == 0) return;

        var batch = new AssetMaintenanceExpenseBatchDto
        {
            VoucherNumber = VoucherNumber,
            TransactionDate = TransactionDate,
            DefaultPaymentAccountId = SelectedDefaultAccount?.Id ?? Guid.Empty,
            DefaultPaymentAccountName = SelectedDefaultAccount?.Name ?? string.Empty,
            Notes = Notes,
            Lines = validRows.Select(r => new AssetMaintenanceExpenseLineDto
            {
                AssetId = r.Asset!.Id,
                AssetCode = r.Asset.AssetCode,
                AssetName = r.Asset.NameArabic,
                ExpenseType = r.ExpenseType,
                Amount = r.Amount,
                CurrentMeterReading = r.CurrentMeterReading,
                Description = r.Description,
                PaymentAccountId = r.PaymentAccountId ?? SelectedDefaultAccount?.Id,
                PaymentAccountName = string.IsNullOrWhiteSpace(r.PaymentAccountName) ? (SelectedDefaultAccount?.Name ?? string.Empty) : r.PaymentAccountName,
                InvoiceReference = r.InvoiceRef,
                IsCapitalizedRepair = r.IsCapitalized
            }).ToList()
        };

        await client.SaveMaintenanceBatchAsync(batch);
        notifications.Text(NotificationKind.Success, "نجاح الترحيل", $"تم حفظ وترحيل قيد مصاريف وصيانة الآليات برقم {VoucherNumber} وإجمالي {TotalAmount:N2}");
        ClearForm();
    }

    private void ClearForm()
    {
        VoucherNumber = "FLT-" + DateTime.Now.ToString("yyyyMMdd-HHmm");
        TransactionDate = DateTime.Today;
        Rows.Clear();
        AddEmptyRow();
        RecalculateTotals();
    }
}
