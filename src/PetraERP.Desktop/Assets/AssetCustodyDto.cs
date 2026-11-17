namespace PetraERP.Desktop.Assets;

/// <summary>
/// Type of custody movement.
/// </summary>
public enum CustodyHandoverType
{
    NewAssignment = 1,     // تسليم عهدة جديد
    CustodyTransfer = 2,   // نقل عهدة من موظف لآخر
    ReturnToWarehouse = 3, // استرداد عهدة للمستودع
    InspectionCheck = 4,   // فحص دوري ومطابقة جرد
}

/// <summary>
/// Physical and technical condition rating of the asset.
/// </summary>
public enum AssetConditionRating
{
    BrandNew = 1,          // جديد تماماً
    Excellent = 2,         // ممتاز
    Good = 3,              // جيد
    NeedsMaintenance = 4,  // بحاجة صيانة
    DamagedOrScrap = 5,    // تالف / خردة
}

/// <summary>
/// Custody handover document / certificate.
/// </summary>
public sealed record AssetCustodyVoucherDto
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string VoucherNumber { get; init; } = string.Empty;
    public DateTime TransactionDate { get; init; } = DateTime.UtcNow;
    public CustodyHandoverType HandoverType { get; init; } = CustodyHandoverType.NewAssignment;
    public Guid AssetId { get; init; }
    public string AssetCode { get; init; } = string.Empty;
    public string AssetName { get; init; } = string.Empty;
    public string SerialNumber { get; init; } = string.Empty;
    public string? PreviousCustodian { get; init; }
    public string CustodianName { get; init; } = string.Empty;
    public string EmployeeCode { get; init; } = string.Empty;
    public string DepartmentOrSite { get; init; } = string.Empty;
    public string PhoneNumber { get; init; } = string.Empty;
    public AssetConditionRating Condition { get; init; } = AssetConditionRating.BrandNew;
    public string AccessoriesChecklist { get; init; } = string.Empty;
    public string TechnicalNotes { get; init; } = string.Empty;
    public string LegalDeclarationText { get; init; } = string.Empty;
    public string CustodianSignatureName { get; init; } = string.Empty;
    public string WarehouseKeeperSignatureName { get; init; } = string.Empty;
    public string DepartmentManagerSignatureName { get; init; } = string.Empty;
}
