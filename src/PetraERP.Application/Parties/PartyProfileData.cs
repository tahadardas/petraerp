using PetraERP.Application.Common;

namespace PetraERP.Application.Parties;

/// <summary>
/// Al-Ameen customer / supplier card attributes beside the core party: title and suffix, barcode, search visibility, the default
/// discount percentage, the sales price type, and the two extra accounts (conditional discount counter-account and VAT account).
/// </summary>
public sealed record PartyProfileData(
    string? Title = null,
    string? Suffix = null,
    string? Barcode = null,
    bool HideInSearch = false,
    decimal DiscountPercent = 0m,
    string SalesPriceType = PartyProfileData.ConsumerPrice,
    Guid? ConditionalDiscountAccountId = null,
    Guid? VatAccountId = null,
    string? Notes = null)
{
    public const string ConsumerPrice = "CONSUMER";

    /// <summary>The price-list codes the price page of the item card writes; a party picks one of them for its sales price.</summary>
    public static readonly IReadOnlyList<string> PriceTypes =
        ["WHOLESALE", "SEMI_WHOLESALE", "DISTRIBUTOR", "EXPORT", "RETAIL", "CONSUMER"];

    public void Validate()
    {
        if (Title is { Length: > 100 } || Suffix is { Length: > 100 })
            throw new BusinessRuleException("parties.profile_text_length", "The title and suffix allow at most 100 characters.", "profile.title");
        if (Notes is { Length: > 1000 })
            throw new BusinessRuleException("parties.profile_notes_length", "The notes allow at most 1000 characters.", "profile.notes");
        if (Barcode is { Length: > 64 })
            throw new BusinessRuleException("parties.profile_barcode_length", "The barcode allows at most 64 characters.", "profile.barcode");
        if (DiscountPercent is < 0m or > 100m)
            throw new BusinessRuleException("parties.profile_discount_range", "The discount percentage must be between 0 and 100.", "profile.discountPercent");
        if (!PriceTypes.Contains(SalesPriceType, StringComparer.Ordinal))
            throw new BusinessRuleException("parties.profile_price_type", "Unknown sales price type.", "profile.salesPriceType");
    }
}
