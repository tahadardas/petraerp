using PetraERP.Application.Inventory;
using PetraERP.Contracts.Financial;
using PetraERP.Contracts.Inventory;

namespace PetraERP.Api.Endpoints;

/// <summary>Maps the Al-Ameen item profile between the wire contract (decimal strings) and the application record.</summary>
internal static class ItemProfileMapping
{
    internal static ItemProfileData? ToData(ItemProfileDto? dto) => dto is null ? null : new ItemProfileData(
        Unit2Id: dto.Unit2Id,
        Unit2Factor: Rate(dto.Unit2Factor),
        Unit2FactorVariable: dto.Unit2FactorVariable,
        Unit3Id: dto.Unit3Id,
        Unit3Factor: Rate(dto.Unit3Factor),
        Unit3FactorVariable: dto.Unit3FactorVariable,
        DefaultUnitIndex: dto.DefaultUnitIndex,
        GiftQuantity: Money(dto.GiftQuantity),
        GiftPerQuantity: Money(dto.GiftPerQuantity),
        Specifications: Text(dto.Specifications),
        Measure: Text(dto.Measure),
        Source: Text(dto.Source),
        Location: Text(dto.Location),
        Manufacturer: Text(dto.Manufacturer),
        Color: Text(dto.Color),
        OriginCountry: Text(dto.OriginCountry),
        Quality: Text(dto.Quality),
        Model: Text(dto.Model),
        ItemKind: dto.ItemKind,
        IsAssembly: dto.IsAssembly,
        ComponentPricing: dto.ComponentPricing,
        TracksSerialNumber: dto.TracksSerialNumber,
        EnforceSerialOnReceipt: dto.EnforceSerialOnReceipt,
        EnforceSerialOnIssue: dto.EnforceSerialOnIssue,
        TracksProductionDate: dto.TracksProductionDate,
        TracksExpiryDate: dto.TracksExpiryDate,
        EnforceDatesOnReceipt: dto.EnforceDatesOnReceipt,
        EnforceDatesOnIssue: dto.EnforceDatesOnIssue,
        HideInSearch: dto.HideInSearch,
        IntegerQuantityOnInvoice: dto.IntegerQuantityOnInvoice,
        ImagePath: Text(dto.ImagePath),
        PricingPolicy: dto.PricingPolicy,
        LastPurchaseRate: Rate(dto.LastPurchaseRate));

    internal static ItemProfileDto ToDto(ItemProfileData? data)
    {
        data ??= new ItemProfileData();
        return new ItemProfileDto
        {
            Unit2Id = data.Unit2Id,
            Unit2Factor = Format(data.Unit2Factor, FinancialDecimalText.RateScale),
            Unit2FactorVariable = data.Unit2FactorVariable,
            Unit3Id = data.Unit3Id,
            Unit3Factor = Format(data.Unit3Factor, FinancialDecimalText.RateScale),
            Unit3FactorVariable = data.Unit3FactorVariable,
            DefaultUnitIndex = data.DefaultUnitIndex,
            GiftQuantity = Format(data.GiftQuantity, FinancialDecimalText.MoneyScale),
            GiftPerQuantity = Format(data.GiftPerQuantity, FinancialDecimalText.MoneyScale),
            Specifications = data.Specifications,
            Measure = data.Measure,
            Source = data.Source,
            Location = data.Location,
            Manufacturer = data.Manufacturer,
            Color = data.Color,
            OriginCountry = data.OriginCountry,
            Quality = data.Quality,
            Model = data.Model,
            ItemKind = data.ItemKind,
            IsAssembly = data.IsAssembly,
            ComponentPricing = data.ComponentPricing,
            TracksSerialNumber = data.TracksSerialNumber,
            EnforceSerialOnReceipt = data.EnforceSerialOnReceipt,
            EnforceSerialOnIssue = data.EnforceSerialOnIssue,
            TracksProductionDate = data.TracksProductionDate,
            TracksExpiryDate = data.TracksExpiryDate,
            EnforceDatesOnReceipt = data.EnforceDatesOnReceipt,
            EnforceDatesOnIssue = data.EnforceDatesOnIssue,
            HideInSearch = data.HideInSearch,
            IntegerQuantityOnInvoice = data.IntegerQuantityOnInvoice,
            ImagePath = data.ImagePath,
            PricingPolicy = data.PricingPolicy,
            LastPurchaseRate = Format(data.LastPurchaseRate, FinancialDecimalText.RateScale),
        };
    }

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static decimal? Money(string? text) =>
        FinancialDecimalText.TryParse(text, FinancialDecimalText.MoneyScale, 15, out var value) ? value : null;

    private static decimal? Rate(string? text) =>
        FinancialDecimalText.TryParse(text, FinancialDecimalText.RateScale, FinancialDecimalText.RateIntegerDigits, out var value) ? value : null;

    private static string? Format(decimal? value, int scale) => value.HasValue ? FinancialDecimalText.Format(value.Value, scale) : null;
}
