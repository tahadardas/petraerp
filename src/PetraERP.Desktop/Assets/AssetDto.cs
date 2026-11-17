namespace PetraERP.Desktop.Assets;

/// <summary>
/// Major classification of an asset.
/// </summary>
public enum AssetType
{
    Fixed = 1,              // أصل ثابت (مباني، أثاث، تجهيزات)
    Movable = 2,            // أصل متحرك عهدة (أجهزة كمبيوتر، أجهزة مساحة، عدد وأدوات)
    MachineryVehicle = 3,   // آليات ومركبات ومعدات ثقيلة (شاحنات، حفارات، تركسات، مولدات، سيارات)
}

/// <summary>
/// Status of an asset throughout its lifecycle.
/// </summary>
public enum AssetStatus
{
    InService = 1,          // في الخدمة ومتاح بالمستودع
    InCustodyEmployee = 2,  // في عهدة موظف
    InCustodyCustomer = 3,  // مؤجر / معار لزبون (برسم الأمانة)
    InMaintenance = 4,      // قيد الصيانة والإصلاح
    Suspended = 5,          // موقوف الاهتلاك مؤقتاً
    Disposed = 6,           // مستبعد / مباع / متلف
}

/// <summary>
/// Depreciation method applied to the asset.
/// </summary>
public enum AssetDepreciationMethod
{
    StraightLine = 1,       // القسط الثابت
    DecliningBalance = 2,   // القسط المتناقص
    UnitsOfProduction = 3,  // وحدات الإنتاج وساعات التشغيل والكيلومترات
    NoDepreciation = 4,     // بدون اهتلاك (أراضي أو أصول تشغيلية غير مهتلكة)
}

/// <summary>
/// Core Asset Data Transfer Object.
/// </summary>
public sealed record AssetDto
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string AssetCode { get; init; } = string.Empty;
    public string NameArabic { get; init; } = string.Empty;
    public string NameEnglish { get; init; } = string.Empty;
    public string CategoryCode { get; init; } = string.Empty;
    public string CategoryName { get; init; } = string.Empty;
    public AssetType AssetType { get; init; } = AssetType.Fixed;
    public AssetStatus Status { get; init; } = AssetStatus.InService;
    public string BarcodeOrQr { get; init; } = string.Empty;
    public string SerialNumber { get; init; } = string.Empty;
    public string LocationOrBranch { get; init; } = string.Empty;
    public string? CurrentCustodian { get; init; }
    public DateTime PurchaseDate { get; init; }
    public decimal AcquisitionCost { get; init; }
    public decimal SalvageValue { get; init; }
    public int UsefulLifeMonths { get; init; }
    public AssetDepreciationMethod DepreciationMethod { get; init; } = AssetDepreciationMethod.StraightLine;
    public decimal AccumulatedDepreciation { get; init; }
    public decimal CurrentBookValue => Math.Max(0m, AcquisitionCost - AccumulatedDepreciation);
    public MachineryVehicleDetailsDto? MachineryDetails { get; init; }
}
