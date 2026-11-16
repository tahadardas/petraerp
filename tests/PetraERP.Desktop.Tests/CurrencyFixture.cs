using PetraERP.ApiClient;
using PetraERP.Desktop.Currencies;

namespace PetraERP.Desktop.Tests;

/// <summary>
/// In-memory company currencies and exchange-rate register. Only what the documents read is real: the base currency,
/// the enabled codes and the rate in force on a date. A currency with no entry answers "not found" rather than one.
/// </summary>
internal sealed class CurrencyFixture : ICurrenciesClient
{
    /// <summary>Deliberately different from the code the drafts once hard-coded, so a test can tell the two apart.</summary>
    internal string BaseCurrency { get; set; } = "JOD";
    internal List<string> ForeignCurrencies { get; } = ["USD"];

    /// <summary>Counted so a test can prove a page reads the enabled currencies once, not on every field change.</summary>
    internal int SettingsReads { get; private set; }

    internal Dictionary<string, (string Rate, DateOnly EffectiveDate, string Source)> Register { get; } = new(StringComparer.Ordinal);

    /// <summary>Counted so a test can prove the register is asked once per deliberate change, not on every keystroke.</summary>
    internal int RateReads { get; private set; }

    internal List<(string Currency, DateOnly Date)> RateQueries { get; } = [];

    /// <summary>Register page queries, so a test can prove the currency and date-range filters really reach the server.</summary>
    internal List<(string? Currency, DateOnly? From, DateOnly? To)> RateListQueries { get; } = [];
    internal Exception? RateFailure { get; set; }

    public Task<CompanyCurrencySettingsResponse> GetSettingsAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken)
    {
        SettingsReads++;
        return Task.FromResult(new CompanyCurrencySettingsResponse
        {
            CompanyId = companyId,
            BaseCurrencyCode = BaseCurrency,
            BaseCurrencyLocked = true,
            Currencies = [Currency(BaseCurrency, true), .. ForeignCurrencies.Select(code => Currency(code, false))],
        });
    }

    private static CompanyCurrencyDto Currency(string code, bool isBase) => new()
    {
        Code = code, NameArabic = code, NameEnglish = code, IsActive = true, IsBase = isBase,
    };

    public Task<CompanyCurrencySettingsResponse> UpdateCardAsync(Guid companyId, Guid branchId, string code, UpdateCurrencyCardRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<CompanyCurrencySettingsResponse> EnableCurrencyAsync(Guid companyId, Guid branchId, EnableCurrencyRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<CompanyCurrencySettingsResponse> ChangeBaseCurrencyAsync(Guid companyId, Guid branchId, ChangeBaseCurrencyRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<ExchangeRatePageResponse> ListRatesAsync(Guid companyId, Guid branchId, string? currencyCode, DateOnly? validFrom, DateOnly? validTo, int page, int pageSize,
        CancellationToken cancellationToken)
    {
        RateListQueries.Add((currencyCode, validFrom, validTo));
        var rows = Register.Where(entry => currencyCode is null || entry.Key == currencyCode)
            .Where(entry => (validFrom is null || entry.Value.EffectiveDate >= validFrom) && (validTo is null || entry.Value.EffectiveDate <= validTo))
            .OrderByDescending(entry => entry.Value.EffectiveDate)
            .ToList();
        return Task.FromResult(new ExchangeRatePageResponse
        {
            Items = rows.Select(entry => new ExchangeRateDto
            {
                Id = Guid.NewGuid(), CurrencyCode = entry.Key, BaseCurrencyCode = BaseCurrency, Rate = entry.Value.Rate,
                EffectiveDate = entry.Value.EffectiveDate, Source = entry.Value.Source, Reason = string.Empty,
                CreatedByUserId = Guid.NewGuid(), CreatedAtUtc = DateTimeOffset.UnixEpoch,
            }).ToList(),
            Page = page, PageSize = pageSize, TotalCount = rows.Count,
        });
    }

    public Task<ExchangeRateDto> RegisterRateAsync(Guid companyId, Guid branchId, RegisterExchangeRateRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<EffectiveExchangeRateResponse> GetEffectiveRateAsync(Guid companyId, Guid branchId, string currencyCode, DateOnly asOfDate,
        CancellationToken cancellationToken)
    {
        RateReads++;
        RateQueries.Add((currencyCode, asOfDate));
        if (RateFailure is { } failure) return Task.FromException<EffectiveExchangeRateResponse>(failure);
        if (currencyCode == BaseCurrency)
            return Task.FromResult(new EffectiveExchangeRateResponse
            {
                CurrencyCode = currencyCode, BaseCurrencyCode = BaseCurrency, Date = asOfDate, Found = true, Rate = "1.00000000",
            });
        return Register.TryGetValue(currencyCode, out var entry) && entry.EffectiveDate <= asOfDate
            ? Task.FromResult(new EffectiveExchangeRateResponse
            {
                CurrencyCode = currencyCode, BaseCurrencyCode = BaseCurrency, Date = asOfDate, Found = true, Rate = entry.Rate,
                EffectiveDate = entry.EffectiveDate, Source = entry.Source,
            })
            : Task.FromResult(new EffectiveExchangeRateResponse
            {
                CurrencyCode = currencyCode, BaseCurrencyCode = BaseCurrency, Date = asOfDate, Found = false,
            });
    }
}
