using PetraERP.ApiClient;
using PetraERP.Desktop.Currencies;

namespace PetraERP.Desktop.Connection;

public sealed class CurrenciesClient(DesktopSession session) : ICurrenciesClient, IPricingClient
{
    public Task<CompanyCurrencySettingsResponse> GetSettingsAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken) =>
        session.Api.GetCompanyCurrencySettingsAsync(companyId, branchId, cancellationToken);

    public Task<CompanyCurrencySettingsResponse> UpdateCardAsync(Guid companyId, Guid branchId, string code, UpdateCurrencyCardRequest request, CancellationToken cancellationToken) =>
        session.Api.UpdateCurrencyCardAsync(companyId, branchId, code, request, cancellationToken);

    public Task<CompanyCurrencySettingsResponse> EnableCurrencyAsync(Guid companyId, Guid branchId, EnableCurrencyRequest request, CancellationToken cancellationToken) =>
        session.Api.EnableCompanyCurrencyAsync(companyId, branchId, request, cancellationToken);

    public Task<CompanyCurrencySettingsResponse> ChangeBaseCurrencyAsync(Guid companyId, Guid branchId, ChangeBaseCurrencyRequest request,
        CancellationToken cancellationToken) =>
        session.Api.ChangeCompanyBaseCurrencyAsync(companyId, branchId, request, cancellationToken);

    public Task<ExchangeRatePageResponse> ListRatesAsync(Guid companyId, Guid branchId, string? currencyCode, DateOnly? validFrom, DateOnly? validTo, int page, int pageSize,
        CancellationToken cancellationToken) =>
        session.Api.ListExchangeRatesAsync(companyId, branchId, currencyCode, validFrom, validTo, page: page, pageSize: pageSize, cancellationToken: cancellationToken);

    public Task<ExchangeRateDto> RegisterRateAsync(Guid companyId, Guid branchId, RegisterExchangeRateRequest request, CancellationToken cancellationToken) =>
        session.Api.RegisterExchangeRateAsync(companyId, branchId, request, cancellationToken);

    public Task<EffectiveExchangeRateResponse> GetEffectiveRateAsync(Guid companyId, Guid branchId, string currencyCode, DateOnly asOfDate,
        CancellationToken cancellationToken) =>
        session.Api.GetEffectiveExchangeRateAsync(companyId, branchId, currencyCode, asOfDate, cancellationToken);

    public Task<PriceQuoteResponse> QuoteAsync(Guid companyId, Guid branchId, Guid itemId, Guid unitId, string quantity, DateOnly asOfDate, string? currencyCode,
        Guid? partyId, Guid? patternRevisionId, CancellationToken cancellationToken) =>
        session.Api.QuoteItemPriceAsync(companyId, branchId, itemId, unitId, quantity, asOfDate, currencyCode, partyId, patternRevisionId, cancellationToken: cancellationToken);

    public Task<ItemPriceDto> AddPriceAsync(Guid companyId, Guid branchId, Guid itemId, CreateItemPriceRequest request, CancellationToken cancellationToken) =>
        session.Api.AddItemPriceAsync(companyId, branchId, itemId, request, cancellationToken);

    public Task DeletePriceAsync(Guid companyId, Guid branchId, Guid itemId, Guid priceId, CancellationToken cancellationToken) =>
        session.Api.DeleteItemPriceAsync(companyId, branchId, itemId, priceId, cancellationToken);
}
