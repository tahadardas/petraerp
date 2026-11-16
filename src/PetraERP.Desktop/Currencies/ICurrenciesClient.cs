using PetraERP.ApiClient;

namespace PetraERP.Desktop.Currencies;

/// <summary>Typed-client seam for company currencies and the exchange-rate register; the server owns every rule.</summary>
public interface ICurrenciesClient
{
    Task<CompanyCurrencySettingsResponse> GetSettingsAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken);

    Task<CompanyCurrencySettingsResponse> UpdateCardAsync(Guid companyId, Guid branchId, string code, UpdateCurrencyCardRequest request, CancellationToken cancellationToken);

    Task<CompanyCurrencySettingsResponse> EnableCurrencyAsync(Guid companyId, Guid branchId, EnableCurrencyRequest request, CancellationToken cancellationToken);

    Task<CompanyCurrencySettingsResponse> ChangeBaseCurrencyAsync(Guid companyId, Guid branchId, ChangeBaseCurrencyRequest request, CancellationToken cancellationToken);

    Task<ExchangeRatePageResponse> ListRatesAsync(Guid companyId, Guid branchId, string? currencyCode, DateOnly? validFrom, DateOnly? validTo, int page, int pageSize,
        CancellationToken cancellationToken);

    Task<ExchangeRateDto> RegisterRateAsync(Guid companyId, Guid branchId, RegisterExchangeRateRequest request, CancellationToken cancellationToken);

    Task<EffectiveExchangeRateResponse> GetEffectiveRateAsync(Guid companyId, Guid branchId, string currencyCode, DateOnly asOfDate, CancellationToken cancellationToken);
}

/// <summary>Typed-client seam for item price rows and the server price resolver.</summary>
public interface IPricingClient
{
    Task<PriceQuoteResponse> QuoteAsync(Guid companyId, Guid branchId, Guid itemId, Guid unitId, string quantity, DateOnly asOfDate, string? currencyCode,
        Guid? partyId, Guid? patternRevisionId, CancellationToken cancellationToken);

    Task<ItemPriceDto> AddPriceAsync(Guid companyId, Guid branchId, Guid itemId, CreateItemPriceRequest request, CancellationToken cancellationToken);

    Task DeletePriceAsync(Guid companyId, Guid branchId, Guid itemId, Guid priceId, CancellationToken cancellationToken);
}
