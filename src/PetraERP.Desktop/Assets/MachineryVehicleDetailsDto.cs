namespace PetraERP.Desktop.Assets;

/// <summary>
/// Metric tracked for vehicle or heavy machinery usage.
/// </summary>
public enum MeterType
{
    Kilometers = 1,       // عداد كيلومترات (سيارات وشاحنات)
    OperatingHours = 2,   // عداد ساعات تشغيل (مولدات، حفارات، تركسات، روافع)
}

/// <summary>
/// Fuel type consumed by the engine.
/// </summary>
public enum FuelType
{
    Diesel = 1,           // مازوت / ديزل
    Gasoline95 = 2,       // بنزين 95
    Gasoline90 = 3,       // بنزين 90
    Electric = 4,         // كهربائي
    Hybrid = 5,           // هجين
}

/// <summary>
/// Specialized technical and operational specifications for vehicles, fleet, and heavy equipment.
/// </summary>
public sealed record MachineryVehicleDetailsDto
{
    public string PlateNumber { get; init; } = string.Empty;
    public string ChassisNumberVin { get; init; } = string.Empty;
    public string EngineNumber { get; init; } = string.Empty;
    public string Manufacturer { get; init; } = string.Empty;
    public string ModelName { get; init; } = string.Empty;
    public int ModelYear { get; init; }
    public MeterType MeterType { get; init; } = MeterType.Kilometers;
    public decimal CurrentMeterReading { get; init; }
    public FuelType FuelType { get; init; } = FuelType.Diesel;
    public decimal StandardFuelConsumptionRate { get; init; } // استهلاك معياري: لتر/100كم أو لتر/ساعة عمل
    public decimal OilChangeIntervalMeter { get; init; } = 5000m; // دورة تغيير الزيت (مثلاً كل 5000 كم أو 250 ساعة)
    public decimal LastOilChangeMeter { get; init; }
    public decimal NextOilChangeMeter => LastOilChangeMeter + OilChangeIntervalMeter;
    public DateTime? LicenseExpiryDate { get; init; }
    public DateTime? InsuranceExpiryDate { get; init; }
    public DateTime? InspectionExpiryDate { get; init; }
    public string? CurrentDriverOrOperator { get; init; }
    public string? AssignedProjectOrSite { get; init; }
}
