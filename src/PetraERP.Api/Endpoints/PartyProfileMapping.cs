using PetraERP.Application.Parties;
using PetraERP.Contracts.Financial;
using PetraERP.Contracts.Parties;

namespace PetraERP.Api.Endpoints;

internal static class PartyProfileMapping
{
    internal static PartyProfileData? ToData(PartyProfileDto? dto) => dto is null ? null : new PartyProfileData(
        Title: Text(dto.Title),
        Suffix: Text(dto.Suffix),
        Barcode: Text(dto.Barcode),
        HideInSearch: dto.HideInSearch,
        DiscountPercent: FinancialDecimalText.TryParse(dto.DiscountPercent, FinancialDecimalText.MoneyScale, 15, out var discount) ? discount.Value : 0m,
        SalesPriceType: Text(dto.SalesPriceType) ?? PartyProfileData.ConsumerPrice,
        ConditionalDiscountAccountId: dto.ConditionalDiscountAccountId,
        VatAccountId: dto.VatAccountId,
        Notes: Text(dto.Notes));

    internal static PartyProfileDto ToDto(PartyProfileData? data)
    {
        data ??= new PartyProfileData();
        return new PartyProfileDto
        {
            Title = data.Title, Suffix = data.Suffix, Barcode = data.Barcode, HideInSearch = data.HideInSearch,
            DiscountPercent = FinancialDecimalText.Format(data.DiscountPercent, FinancialDecimalText.MoneyScale),
            SalesPriceType = data.SalesPriceType,
            ConditionalDiscountAccountId = data.ConditionalDiscountAccountId, VatAccountId = data.VatAccountId,
            Notes = data.Notes,
        };
    }

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
