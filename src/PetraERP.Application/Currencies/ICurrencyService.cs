namespace PetraERP.Application.Currencies;

/// <summary>A currency of the catalog as seen by one company.</summary>
public sealed record CompanyCurrencyResult(string Code, string NameArabic, string? NameEnglish, bool IsActive, bool IsBase,
    string? Symbol = null, string? MinorUnitNameArabic = null, string? MinorUnitNameEnglish = null, int DecimalPlaces = 2,
    PetraERP.Domain.FinancialValues.CurrencyRoundingMode RoundingMode = PetraERP.Domain.FinancialValues.CurrencyRoundingMode.HalfAwayFromZero);

/// <summary>Edits the currency card: symbol, minor-unit names, decimal places and rounding mode. Audited with before/after.</summary>
public sealed record UpdateCurrencyCardCommand(string Code, string? Symbol, string? MinorUnitNameArabic, string? MinorUnitNameEnglish, int DecimalPlaces,
    PetraERP.Domain.FinancialValues.CurrencyRoundingMode RoundingMode, string Reason);

/// <summary>
/// A company's currency settings. The base currency was chosen by the operator at setup; it can be changed only while
/// the company has no accounting or commercial data (<see cref="BaseCurrencyLocked"/>); after that a change needs an
/// approved conversion procedure, which is not implemented.
/// </summary>
public sealed record CompanyCurrencySettingsResult(
    Guid CompanyId,
    string BaseCurrencyCode,
    bool BaseCurrencyLocked,
    string? BaseCurrencyLockReason,
    IReadOnlyList<CompanyCurrencyResult> Currencies);

/// <summary>Enables a currency for the company, defining it in the catalog first when it is new (for example SYP or USD).</summary>
public sealed record EnableCurrencyCommand(string Code, string? NameArabic, string? NameEnglish, string Reason);

public sealed record ChangeBaseCurrencyCommand(string Code, string Reason);

public sealed record RegisterExchangeRateCommand(string CurrencyCode, decimal Rate, DateOnly EffectiveDate, string Source, string Reason);

/// <summary>One line of a rate bulletin: base units per one unit of the currency.</summary>
public sealed record BulletinRate(string CurrencyCode, decimal Rate);

/// <summary>A daily rate bulletin: several currencies registered together, atomically, with one date, source and reason.</summary>
public sealed record RegisterRateBulletinCommand(DateOnly EffectiveDate, string Source, string Reason, IReadOnlyList<BulletinRate> Rates);

public sealed record ExchangeRateQuery(string? CurrencyCode = null, DateOnly? From = null, DateOnly? To = null, int Page = 1, int PageSize = 50);

/// <summary>One register entry: base units per one unit of the currency, effective from a date. Entries are never edited.</summary>
public sealed record ExchangeRateResult(
    Guid Id,
    string CurrencyCode,
    string BaseCurrencyCode,
    decimal Rate,
    DateOnly EffectiveDate,
    string Source,
    string Reason,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAtUtc);

public sealed record ExchangeRatePageResult(IReadOnlyList<ExchangeRateResult> Items, int Page, int PageSize, long TotalCount);

/// <summary>The rate a document dated <see cref="Date"/> would use, or Found = false (no rate is invented).</summary>
public sealed record EffectiveExchangeRateResult(string CurrencyCode, string BaseCurrencyCode, DateOnly Date, bool Found, decimal? Rate, Guid? RateId,
    DateOnly? EffectiveDate, string? Source);

public interface ICurrencyService
{
    Task<CompanyCurrencySettingsResult> GetSettingsAsync(Guid companyId, CancellationToken cancellationToken = default);

    Task<CompanyCurrencySettingsResult> EnableCurrencyAsync(Guid companyId, Guid userId, EnableCurrencyCommand command, CancellationToken cancellationToken = default);

    Task<CompanyCurrencySettingsResult> ChangeBaseCurrencyAsync(Guid companyId, Guid userId, ChangeBaseCurrencyCommand command, CancellationToken cancellationToken = default);

    Task<CompanyCurrencySettingsResult> UpdateCardAsync(Guid companyId, Guid userId, UpdateCurrencyCardCommand command, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExchangeRateResult>> RegisterRateBulletinAsync(Guid companyId, Guid userId, RegisterRateBulletinCommand command, CancellationToken cancellationToken = default);

    Task<ExchangeRatePageResult> ListRatesAsync(Guid companyId, ExchangeRateQuery query, CancellationToken cancellationToken = default);

    Task<ExchangeRateResult> RegisterRateAsync(Guid companyId, Guid userId, RegisterExchangeRateCommand command, CancellationToken cancellationToken = default);

    Task<EffectiveExchangeRateResult> GetEffectiveRateAsync(Guid companyId, string currencyCode, DateOnly asOfDate, CancellationToken cancellationToken = default);
}
