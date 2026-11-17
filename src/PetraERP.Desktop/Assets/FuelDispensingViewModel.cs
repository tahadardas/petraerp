using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.Desktop.Controls;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Assets;

/// <summary>
/// Row item for fuel dispensing sheet.
/// </summary>
public sealed partial class FuelDispensingRowItem : ObservableObject
{
    [ObservableProperty] private int lineNumber;
    [ObservableProperty] private AssetDto? asset;
    [ObservableProperty] private string driverOrOperator = string.Empty;
    [ObservableProperty] private decimal currentMeterReading;
    [ObservableProperty] private decimal previousMeterReading;
    [ObservableProperty] private decimal litersIssued;
    [ObservableProperty] private decimal unitCost; // Read-only from warehouse moving average cost
    [ObservableProperty] private decimal totalCost;
    [ObservableProperty] private decimal consumptionRate;
    [ObservableProperty] private bool hasExcessAlert;
    [ObservableProperty] private string notes = string.Empty;
}

/// <summary>
/// Fuel and diesel distribution voucher deducting stock from warehouse at automatic weighted average cost.
/// </summary>
public sealed partial class FuelDispensingViewModel : PageViewModel
{
    private readonly IAssetsClient client;
    private readonly NotificationCenter notifications;

    [ObservableProperty] private string voucherNumber = "FUEL-" + DateTime.Now.ToString("yyyyMMdd-HHmm");
    [ObservableProperty] private DateTime transactionDate = DateTime.Today;
    [ObservableProperty] private string dispenserName = "عبد الله النجار (أمين الخزان)";
    [ObservableProperty] private string notes = "صرف وتوزيع مازوت يومي للآليات العاملة بالموقع";

    // Selected Fuel Warehouse / Tank
    [ObservableProperty] private (Guid Id, string Name, decimal MovingAverageCost, decimal CurrentStockLiters)? selectedWarehouse;
    [ObservableProperty] private decimal currentWarehouseStockLiters;
    [ObservableProperty] private decimal movingAverageCost;

    [ObservableProperty] private decimal totalLitersIssued;
    [ObservableProperty] private decimal grandTotalCost;
    [ObservableProperty] private string grandTotalCostWords = string.Empty;

    public ObservableCollection<(Guid Id, string Name, decimal MovingAverageCost, decimal CurrentStockLiters)> FuelWarehouses { get; } = [];
    public ObservableCollection<AssetDto> MachineryAssets { get; } = [];
    public ObservableCollection<FuelDispensingRowItem> Rows { get; } = [];

    public FuelDispensingViewModel(IAssetsClient client, NotificationCenter notifications, DesktopOperation operation, ILocalizationService localization)
        : base(operation, localization)
    {
        this.client = client;
        this.notifications = notifications;

        AddRowCommand = new RelayCommand(AddEmptyRow);
        RemoveRowCommand = new RelayCommand<FuelDispensingRowItem>(RemoveRow);
        SaveVoucherCommand = new AsyncRelayCommand(SaveVoucherAsync, () => Rows.Any(r => r.LitersIssued > 0 && r.Asset != null));
        ClearCommand = new RelayCommand(ClearForm);
    }

    protected override string RequiredPermission => "Inventory.Movements.View";

    public IRelayCommand AddRowCommand { get; }
    public IRelayCommand<FuelDispensingRowItem> RemoveRowCommand { get; }
    public IAsyncRelayCommand SaveVoucherCommand { get; }
    public IRelayCommand ClearCommand { get; }

    protected override async Task LoadAsync(object? parameter, CancellationToken cancellationToken)
    {
        var warehouses = await client.ListFuelWarehousesAsync(cancellationToken);
        FuelWarehouses.Clear();
        foreach (var w in warehouses) FuelWarehouses.Add(w);

        SelectedWarehouse = FuelWarehouses.FirstOrDefault();

        var assets = await client.ListAssetsAsync(cancellationToken);
        MachineryAssets.Clear();
        foreach (var a in assets.Where(a => a.AssetType == AssetType.MachineryVehicle))
        {
            MachineryAssets.Add(a);
        }

        if (Rows.Count == 0)
        {
            InitializeSampleRows();
        }
        RecalculateTotals();
    }

    partial void OnSelectedWarehouseChanged((Guid Id, string Name, decimal MovingAverageCost, decimal CurrentStockLiters)? value)
    {
        if (value.HasValue)
        {
            CurrentWarehouseStockLiters = value.Value.CurrentStockLiters;
            MovingAverageCost = value.Value.MovingAverageCost;

            // Update unit cost across all rows
            foreach (var row in Rows)
            {
                row.UnitCost = MovingAverageCost;
                row.TotalCost = Math.Round(row.LitersIssued * row.UnitCost, 2);
            }
            RecalculateTotals();
        }
    }

    private void InitializeSampleRows()
    {
        Rows.Clear();
        var generator = MachineryAssets.FirstOrDefault(a => a.AssetCode.Contains("GN"));
        var loader = MachineryAssets.FirstOrDefault(a => a.AssetCode.Contains("MC"));
        var truck = MachineryAssets.FirstOrDefault(a => a.AssetCode.Contains("TR"));

        var avgCost = MovingAverageCost > 0 ? MovingAverageCost : 11200m;

        if (generator != null)
        {
            var prev = generator.MachineryDetails?.CurrentMeterReading ?? 4100m;
            var curr = prev + 10m;
            var liters = 180m;
            var isHours = generator.MachineryDetails?.MeterType == MeterType.OperatingHours;
            var rate = AssetCalculationEngine.CalculateFuelConsumptionRate(liters, curr, prev, isHours);
            var isExcess = AssetCalculationEngine.IsFuelConsumptionExcessive(rate, generator.MachineryDetails?.StandardFuelConsumptionRate ?? 45m);

            Rows.Add(new FuelDispensingRowItem
            {
                LineNumber = 1,
                Asset = generator,
                DriverOrOperator = generator.MachineryDetails?.CurrentDriverOrOperator ?? "فني الموقع",
                PreviousMeterReading = prev,
                CurrentMeterReading = curr,
                LitersIssued = liters,
                UnitCost = avgCost,
                TotalCost = Math.Round(liters * avgCost, 2),
                ConsumptionRate = rate,
                HasExcessAlert = isExcess,
                Notes = "تشغيل وردية ليلية كاملة"
            });
        }

        if (loader != null)
        {
            var prev = loader.MachineryDetails?.CurrentMeterReading ?? 3410m;
            var curr = prev + 8m;
            var liters = 120m;
            var isHours = loader.MachineryDetails?.MeterType == MeterType.OperatingHours;
            var rate = AssetCalculationEngine.CalculateFuelConsumptionRate(liters, curr, prev, isHours);
            var isExcess = AssetCalculationEngine.IsFuelConsumptionExcessive(rate, loader.MachineryDetails?.StandardFuelConsumptionRate ?? 18m);

            Rows.Add(new FuelDispensingRowItem
            {
                LineNumber = 2,
                Asset = loader,
                DriverOrOperator = loader.MachineryDetails?.CurrentDriverOrOperator ?? "عثمان",
                PreviousMeterReading = prev,
                CurrentMeterReading = curr,
                LitersIssued = liters,
                UnitCost = avgCost,
                TotalCost = Math.Round(liters * avgCost, 2),
                ConsumptionRate = rate,
                HasExcessAlert = isExcess,
                Notes = "عمل مستمر في تكسير ونقل ركام"
            });
        }

        if (truck != null)
        {
            var prev = truck.MachineryDetails?.CurrentMeterReading ?? 142000m;
            var curr = prev + 600m;
            var liters = 250m;
            var isHours = truck.MachineryDetails?.MeterType == MeterType.OperatingHours;
            var rate = AssetCalculationEngine.CalculateFuelConsumptionRate(liters, curr, prev, isHours);
            var isExcess = AssetCalculationEngine.IsFuelConsumptionExcessive(rate, truck.MachineryDetails?.StandardFuelConsumptionRate ?? 32m);

            Rows.Add(new FuelDispensingRowItem
            {
                LineNumber = 3,
                Asset = truck,
                DriverOrOperator = truck.MachineryDetails?.CurrentDriverOrOperator ?? "محمد حسن",
                PreviousMeterReading = prev,
                CurrentMeterReading = curr,
                LitersIssued = liters,
                UnitCost = avgCost,
                TotalCost = Math.Round(liters * avgCost, 2),
                ConsumptionRate = rate,
                HasExcessAlert = isExcess,
                Notes = "رحلة نقل مواد إلى المشروع الجنوبي"
            });
        }
    }

    public void AddEmptyRow()
    {
        var newRow = new FuelDispensingRowItem
        {
            LineNumber = Rows.Count + 1,
            Asset = MachineryAssets.FirstOrDefault(),
            DriverOrOperator = string.Empty,
            PreviousMeterReading = 0m,
            CurrentMeterReading = 0m,
            LitersIssued = 0m,
            UnitCost = MovingAverageCost,
            TotalCost = 0m,
            ConsumptionRate = 0m,
            HasExcessAlert = false,
            Notes = string.Empty
        };

        newRow.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName is nameof(FuelDispensingRowItem.LitersIssued) or
                                  nameof(FuelDispensingRowItem.CurrentMeterReading) or
                                  nameof(FuelDispensingRowItem.Asset))
            {
                UpdateRowCalculations(newRow);
                RecalculateTotals();
            }
        };

        Rows.Add(newRow);
        RecalculateTotals();
    }

    private void UpdateRowCalculations(FuelDispensingRowItem row)
    {
        row.UnitCost = MovingAverageCost;
        row.TotalCost = Math.Round(row.LitersIssued * row.UnitCost, 2);

        if (row.Asset?.MachineryDetails != null)
        {
            if (row.PreviousMeterReading == 0)
                row.PreviousMeterReading = row.Asset.MachineryDetails.CurrentMeterReading;

            var isHours = row.Asset.MachineryDetails.MeterType == MeterType.OperatingHours;
            row.ConsumptionRate = AssetCalculationEngine.CalculateFuelConsumptionRate(row.LitersIssued, row.CurrentMeterReading, row.PreviousMeterReading, isHours);
            row.HasExcessAlert = AssetCalculationEngine.IsFuelConsumptionExcessive(row.ConsumptionRate, row.Asset.MachineryDetails.StandardFuelConsumptionRate);
        }
    }

    private void RemoveRow(FuelDispensingRowItem? row)
    {
        if (row != null)
        {
            Rows.Remove(row);
            for (int i = 0; i < Rows.Count; i++) Rows[i].LineNumber = i + 1;
            RecalculateTotals();
        }
    }

    public void RecalculateTotals()
    {
        TotalLitersIssued = Rows.Sum(r => r.LitersIssued);
        GrandTotalCost = Rows.Sum(r => r.TotalCost);
        GrandTotalCostWords = ArabicTafqeet.ToWordsOrEmpty(GrandTotalCost, "SYP");
        SaveVoucherCommand.NotifyCanExecuteChanged();
    }

    private async Task SaveVoucherAsync()
    {
        var validRows = Rows.Where(r => r.Asset != null && r.LitersIssued > 0).ToList();
        if (validRows.Count == 0) return;

        if (TotalLitersIssued > CurrentWarehouseStockLiters)
        {
            notifications.Text(NotificationKind.Warning, "تحذير المخزون", $"الكمية المصروفة ({TotalLitersIssued} لتر) تتجاوز الرصيد الحالي للخزان ({CurrentWarehouseStockLiters} لتر)!");
        }

        var voucher = new AssetFuelDispensingVoucherDto
        {
            VoucherNumber = VoucherNumber,
            TransactionDate = TransactionDate,
            WarehouseId = SelectedWarehouse?.Id ?? Guid.Empty,
            WarehouseName = SelectedWarehouse?.Name ?? string.Empty,
            MovingAverageUnitCost = MovingAverageCost,
            StationAttendantOrDispenser = DispenserName,
            Notes = Notes,
            Lines = validRows.Select(r => new AssetFuelDispensingLineDto
            {
                AssetId = r.Asset!.Id,
                AssetCode = r.Asset.AssetCode,
                AssetName = r.Asset.NameArabic,
                DriverOrOperator = r.DriverOrOperator,
                CurrentMeterReading = r.CurrentMeterReading,
                PreviousMeterReading = r.PreviousMeterReading,
                LitersIssued = r.LitersIssued,
                MovingAverageUnitCost = r.UnitCost,
                HasOverConsumptionAlert = r.HasExcessAlert,
                Notes = r.Notes
            }).ToList()
        };

        await client.SaveFuelDispensingVoucherAsync(voucher);
        notifications.Text(NotificationKind.Success, "تم الصرف بنجاح", $"تم خصم {TotalLitersIssued} لتر مازوت من الخزان وتوزيعها على الآليات بتكلفة إجمالية {GrandTotalCost:N2}");
        ClearForm();
    }

    protected override void ClearPage() => ClearForm();

    private void ClearForm()
    {
        VoucherNumber = "FUEL-" + DateTime.Now.ToString("yyyyMMdd-HHmm");
        TransactionDate = DateTime.Today;
        Rows.Clear();
        AddEmptyRow();
        RecalculateTotals();
    }
}
