namespace PetraERP.Contracts.Assets;

/// <summary>
/// One line representing fuel dispensed to a specific vehicle or machine.
/// </summary>
public sealed record AssetFuelDispensingLineDto
{
    public Guid LineId { get; init; } = Guid.NewGuid();
    public Guid AssetId { get; init; }
    public string AssetCode { get; init; } = string.Empty;
    public string AssetName { get; init; } = string.Empty;
    public string DriverOrOperator { get; init; } = string.Empty;
    public decimal CurrentMeterReading { get; init; }
    public decimal LitersIssued { get; init; }
    public decimal MovingAverageUnitCost { get; init; } // السعر الوسطي التلقائي المستخرج من المستودع
    public decimal LineTotalCost => Math.Round(LitersIssued * MovingAverageUnitCost, 4);
    public string Notes { get; init; } = string.Empty;
    public decimal PreviousMeterReading { get; init; }
    public decimal ConsumptionRatePerUnit =>
        (CurrentMeterReading > PreviousMeterReading && LitersIssued > 0)
            ? Math.Round(LitersIssued / (CurrentMeterReading - PreviousMeterReading), 3)
            : 0m;
    public bool HasOverConsumptionAlert { get; init; } // تنبيه استهلاك زائد مقارنة بالمعدل المعياري
}

/// <summary>
/// Fuel and diesel dispensing voucher deducting from warehouse stock.
/// </summary>
public sealed record AssetFuelDispensingVoucherDto
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string VoucherNumber { get; init; } = string.Empty;
    public DateTime TransactionDate { get; init; } = DateTime.UtcNow;
    public Guid WarehouseId { get; init; } // مستودع / خزان الوقود المخصوم منه
    public string WarehouseName { get; init; } = string.Empty;
    public Guid FuelItemId { get; init; } // مادة المازوت / البنزين
    public string FuelItemName { get; init; } = string.Empty;
    public decimal MovingAverageUnitCost { get; init; } // السعر الوسطي للتر من المستودع
    public string StationAttendantOrDispenser { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;
    public IReadOnlyList<AssetFuelDispensingLineDto> Lines { get; init; } = [];
    public decimal TotalLitersIssued => Lines.Sum(l => l.LitersIssued);
    public decimal TotalCost => Lines.Sum(l => l.LineTotalCost);
}
