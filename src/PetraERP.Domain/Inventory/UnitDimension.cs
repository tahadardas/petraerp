namespace PetraERP.Domain.Inventory;

/// <summary>
/// The physical dimension a unit measures. Company-wide conversions may only join units of the same known dimension; a
/// cross-dimension conversion (for example m³ → ton) is a density, which belongs to one item and is never assumed.
/// </summary>
public enum UnitDimension
{
    /// <summary>Legacy or not yet classified; no dimension rule or precision rule is applied.</summary>
    Unspecified = 0,
    Mass = 1,
    Volume = 2,
    Length = 3,
    Area = 4,
    Count = 5,
    Time = 6,
    Other = 7,
}
