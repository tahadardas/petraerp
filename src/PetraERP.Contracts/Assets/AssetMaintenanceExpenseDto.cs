namespace PetraERP.Contracts.Assets;

/// <summary>
/// Operational category of the maintenance or fleet expense.
/// </summary>
public enum MaintenanceExpenseType
{
    OilAndFilterChange = 1, // تغيير زيوت وفلاتر
    PeriodicService = 2,    // صيانة دورية وقائية
    TiresAndBatteries = 3,  // إطارات وبطاريات
    MechanicalRepair = 4,   // إصلاح ميكانيكي ومحركات
    HydraulicRepair = 5,    // إصلاح هيدروليك وضغط
    ElectricalRepair = 6,   // إصلاح كهرباء وحساسات
    BodyAndPaint = 7,       // سمكرة ودهان وهيكل
    WashingAndCleaning = 8, // غسيل ونظافة وتلميع
    LicenseAndInsurance = 9,// رسوم ترخيص وتأمين وفحص
    SparePartsConsumed = 10,// قطع غيار مستهلكة
    OtherOperating = 11,    // مصاريف تشغيلية أخرى
}

/// <summary>
/// One line in the rapid batch maintenance & expense ledger.
/// </summary>
public sealed record AssetMaintenanceExpenseLineDto
{
    public Guid LineId { get; init; } = Guid.NewGuid();
    public Guid AssetId { get; init; }
    public string AssetCode { get; init; } = string.Empty;
    public string AssetName { get; init; } = string.Empty;
    public MaintenanceExpenseType ExpenseType { get; init; } = MaintenanceExpenseType.OilAndFilterChange;
    public decimal Amount { get; init; }
    public decimal CurrentMeterReading { get; init; }
    public string Description { get; init; } = string.Empty;
    public Guid? PaymentAccountId { get; init; } // حساب الصندوق / العهدة النقدية المحدد للسطر (إن اختلف عن الافتراضي)
    public string PaymentAccountName { get; init; } = string.Empty;
    public string? SupplierOrWorkshopName { get; init; }
    public string? InvoiceReference { get; init; }
    public bool IsCapitalizedRepair { get; init; } // هل هي عمرة رأسمالية تزيد من قيمة الأصل الدفترية؟
}

/// <summary>
/// Batch / Journal header for fleet maintenance and expense entries.
/// </summary>
public sealed record AssetMaintenanceExpenseBatchDto
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string VoucherNumber { get; init; } = string.Empty;
    public DateTime TransactionDate { get; init; } = DateTime.UtcNow;
    public Guid DefaultPaymentAccountId { get; init; } // الحساب الافتراضي للدفع (عهدة المشرف فلان أو الصندوق الرئيسي)
    public string DefaultPaymentAccountName { get; init; } = string.Empty;
    public Guid BranchId { get; init; }
    public string Notes { get; init; } = string.Empty;
    public IReadOnlyList<AssetMaintenanceExpenseLineDto> Lines { get; init; } = [];
    public decimal TotalAmount => Lines.Sum(l => l.Amount);
}
