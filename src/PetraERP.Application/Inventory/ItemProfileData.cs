using PetraERP.Application.Common;

namespace PetraERP.Application.Inventory;

/// <summary>
/// Al-Ameen item-card attributes that sit beside the core item: units 2 and 3 (unit 1 is the item's base unit), the
/// stock-level gift rule, the specification lists and the item options. All members are optional so an item created
/// before this profile existed reads back as the defaults.
/// </summary>
public sealed record ItemProfileData(
    Guid? Unit2Id = null,
    decimal? Unit2Factor = null,
    bool Unit2FactorVariable = false,
    Guid? Unit3Id = null,
    decimal? Unit3Factor = null,
    bool Unit3FactorVariable = false,
    int DefaultUnitIndex = 1,
    decimal? GiftQuantity = null,
    decimal? GiftPerQuantity = null,
    string? Specifications = null,
    string? Measure = null,
    string? Source = null,
    string? Location = null,
    string? Manufacturer = null,
    string? Color = null,
    string? OriginCountry = null,
    string? Quality = null,
    string? Model = null,
    int ItemKind = ItemProfileData.StockItem,
    bool IsAssembly = false,
    bool ComponentPricing = false,
    bool TracksSerialNumber = false,
    bool EnforceSerialOnReceipt = false,
    bool EnforceSerialOnIssue = false,
    bool TracksProductionDate = false,
    bool TracksExpiryDate = false,
    bool EnforceDatesOnReceipt = false,
    bool EnforceDatesOnIssue = false,
    bool HideInSearch = false,
    bool IntegerQuantityOnInvoice = false,
    string? ImagePath = null,
    int PricingPolicy = ItemProfileData.RealPricing,
    decimal? LastPurchaseRate = null)
{
    /// <summary>"مستودعية" - the only item kind visible on the Al-Ameen card.</summary>
    public const int StockItem = 1;

    /// <summary>"حقيقي" - the pricing policy shown on the Al-Ameen price page.</summary>
    public const int RealPricing = 1;

    public const int MaxTextLength = 100;
    public const int MaxSpecificationsLength = 2000;
    public const int MaxImagePathLength = 500;

    /// <summary>Server-side rules shared by create and update. Unit existence is checked by the service.</summary>
    public void Validate(Guid baseUnitId)
    {
        if (DefaultUnitIndex is < 1 or > 3)
            throw new BusinessRuleException("inventory.profile_default_unit", "The default unit must be unit 1, 2 or 3.", "profile.defaultUnitIndex");
        ValidateUnit(Unit2Id, Unit2Factor, baseUnitId, 2);
        ValidateUnit(Unit3Id, Unit3Factor, baseUnitId, 3);
        if (Unit2Id is { } second && Unit3Id is { } third && second == third)
            throw new BusinessRuleException("inventory.profile_unit_duplicate", "Units 2 and 3 must be different units.", "profile.unit3Id");
        if (Unit3Id is not null && Unit2Id is null)
            throw new BusinessRuleException("inventory.profile_unit_order", "Unit 3 requires unit 2.", "profile.unit3Id");
        if (DefaultUnitIndex == 2 && Unit2Id is null || DefaultUnitIndex == 3 && Unit3Id is null)
            throw new BusinessRuleException("inventory.profile_default_unit", "The default unit must be a unit that is set.", "profile.defaultUnitIndex");
        if (GiftQuantity is < 0 || GiftPerQuantity is < 0)
            throw new BusinessRuleException("inventory.profile_gift_negative", "The gift quantities cannot be negative.", "profile.giftQuantity");
        if (GiftQuantity is > 0 && GiftPerQuantity is not > 0)
            throw new BusinessRuleException("inventory.profile_gift_basis", "A gift needs the quantity it is given per.", "profile.giftPerQuantity");
        if (LastPurchaseRate is <= 0)
            throw new BusinessRuleException("inventory.profile_rate_invalid", "The last-purchase rate must be greater than zero.", "profile.lastPurchaseRate");
        if (ItemKind != StockItem)
            throw new BusinessRuleException("inventory.profile_kind_invalid", "Unknown item kind.", "profile.itemKind");
        if (PricingPolicy != RealPricing)
            throw new BusinessRuleException("inventory.profile_pricing_policy", "Unknown pricing policy.", "profile.pricingPolicy");
        if (!TracksSerialNumber && (EnforceSerialOnReceipt || EnforceSerialOnIssue))
            throw new BusinessRuleException("inventory.profile_serial_enforce", "Serial enforcement requires serial tracking.", "profile.tracksSerialNumber");
        if (!TracksProductionDate && !TracksExpiryDate && (EnforceDatesOnReceipt || EnforceDatesOnIssue))
            throw new BusinessRuleException("inventory.profile_date_enforce", "Date enforcement requires a production or expiry date.", "profile.tracksExpiryDate");
        foreach (var (name, value) in new[] { ("measure", Measure), ("source", Source), ("location", Location), ("manufacturer", Manufacturer),
                     ("color", Color), ("originCountry", OriginCountry), ("quality", Quality), ("model", Model) })
            if (value is { Length: > MaxTextLength })
                throw new BusinessRuleException("inventory.profile_text_length", $"The value is longer than {MaxTextLength} characters.", $"profile.{name}");
        if (Specifications is { Length: > MaxSpecificationsLength })
            throw new BusinessRuleException("inventory.profile_text_length", $"The specifications are longer than {MaxSpecificationsLength} characters.", "profile.specifications");
        if (ImagePath is { Length: > MaxImagePathLength })
            throw new BusinessRuleException("inventory.profile_text_length", $"The image path is longer than {MaxImagePathLength} characters.", "profile.imagePath");
    }

    private static void ValidateUnit(Guid? unitId, decimal? factor, Guid baseUnitId, int index)
    {
        if (unitId is null)
        {
            if (factor is not null)
                throw new BusinessRuleException("inventory.profile_factor_orphan", "A conversion factor needs its unit.", $"profile.unit{index}Factor");
            return;
        }

        if (unitId == baseUnitId)
            throw new BusinessRuleException("inventory.profile_unit_is_base", "Units 2 and 3 must differ from the base unit.", $"profile.unit{index}Id");
        if (factor is not > 0)
            throw new BusinessRuleException("inventory.profile_factor_invalid", "The conversion factor must be greater than zero.", $"profile.unit{index}Factor");
    }
}
