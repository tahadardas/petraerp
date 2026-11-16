using System.ComponentModel.DataAnnotations;
using PetraERP.Contracts.Financial;

namespace PetraERP.Contracts.Currencies;

public sealed record CompanyCurrencyDto
{
    public string Code { get; init; } = string.Empty;
    public string NameArabic { get; init; } = string.Empty;
    public string? NameEnglish { get; init; }
    public bool IsActive { get; init; }
    public bool IsBase { get; init; }
    public string? Symbol { get; init; }
    public string? MinorUnitNameArabic { get; init; }
    public string? MinorUnitNameEnglish { get; init; }
    /// <summary>0..4.</summary>
    public int DecimalPlaces { get; init; } = 2;
    /// <summary>HalfAwayFromZero | HalfToEven | TowardZero | AwayFromZero.</summary>
    public string RoundingMode { get; init; } = "HalfAwayFromZero";
}

public sealed record UpdateCurrencyCardRequest
{
    [MaxLength(8)]
    public string? Symbol { get; init; }

    [MaxLength(100)]
    public string? MinorUnitNameArabic { get; init; }

    [MaxLength(100)]
    public string? MinorUnitNameEnglish { get; init; }

    [Range(0, 4)]
    public int DecimalPlaces { get; init; } = 2;

    [Required, MaxLength(32)]
    public string RoundingMode { get; init; } = "HalfAwayFromZero";

    [Required, MaxLength(500)]
    public string Reason { get; init; } = string.Empty;
}

/// <summary>
/// The company's currencies. The base currency was chosen by the operator; <see cref="BaseCurrencyLocked"/> is true once
/// journals, documents, stock, prices or rates exist, after which only an approved conversion procedure could change it.
/// </summary>
public sealed record CompanyCurrencySettingsResponse
{
    public Guid CompanyId { get; init; }
    public string BaseCurrencyCode { get; init; } = string.Empty;
    public bool BaseCurrencyLocked { get; init; }
    public string? BaseCurrencyLockReason { get; init; }
    public List<CompanyCurrencyDto> Currencies { get; init; } = [];
}

public sealed record EnableCurrencyRequest
{
    [Required, StringLength(3, MinimumLength = 3)]
    public string Code { get; init; } = string.Empty;

    /// <summary>Required only when the currency is new to the catalog.</summary>
    [MaxLength(200)]
    public string? NameArabic { get; init; }

    [MaxLength(200)]
    public string? NameEnglish { get; init; }

    [Required, MaxLength(500)]
    public string Reason { get; init; } = string.Empty;
}

public sealed record ChangeBaseCurrencyRequest
{
    [Required, StringLength(3, MinimumLength = 3)]
    public string Code { get; init; } = string.Empty;

    [Required, MaxLength(500)]
    public string Reason { get; init; } = string.Empty;
}

public sealed record BulletinRateRequest
{
    [Required, StringLength(3, MinimumLength = 3)]
    public string CurrencyCode { get; init; } = string.Empty;

    [Required, RegularExpression(FinancialDecimalText.RatePattern)]
    public string Rate { get; init; } = string.Empty;
}

/// <summary>A daily rate bulletin (1..50 currencies) registered atomically with one date, source and reason.</summary>
public sealed record RegisterRateBulletinRequest
{
    [Required]
    public DateOnly EffectiveDate { get; init; }

    [Required, MaxLength(64)]
    public string Source { get; init; } = string.Empty;

    [Required, MaxLength(500)]
    public string Reason { get; init; } = string.Empty;

    [Required, MinLength(1), MaxLength(50)]
    public List<BulletinRateRequest> Rates { get; init; } = [];
}

public sealed record RegisterExchangeRateRequest
{
    [Required, StringLength(3, MinimumLength = 3)]
    public string CurrencyCode { get; init; } = string.Empty;

    /// <summary>Base-currency units per one unit of <see cref="CurrencyCode"/>.</summary>
    [Required, RegularExpression(FinancialDecimalText.RatePattern)]
    public string Rate { get; init; } = string.Empty;

    [Required]
    public DateOnly EffectiveDate { get; init; }

    [Required, MaxLength(64)]
    public string Source { get; init; } = string.Empty;

    [Required, MaxLength(500)]
    public string Reason { get; init; } = string.Empty;
}

public sealed record ExchangeRateDto
{
    public Guid Id { get; init; }
    public string CurrencyCode { get; init; } = string.Empty;
    public string BaseCurrencyCode { get; init; } = string.Empty;

    [Required, RegularExpression(FinancialDecimalText.RatePattern)]
    public string Rate { get; init; } = string.Empty;

    public DateOnly EffectiveDate { get; init; }
    public string Source { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public Guid CreatedByUserId { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
}

public sealed record ExchangeRatePageResponse
{
    public List<ExchangeRateDto> Items { get; init; } = [];
    public int Page { get; init; }
    public int PageSize { get; init; }
    public long TotalCount { get; init; }
}

public sealed record EffectiveExchangeRateResponse
{
    public string CurrencyCode { get; init; } = string.Empty;
    public string BaseCurrencyCode { get; init; } = string.Empty;
    public DateOnly Date { get; init; }
    public bool Found { get; init; }

    [RegularExpression(FinancialDecimalText.RatePattern)]
    public string? Rate { get; init; }

    public Guid? RateId { get; init; }
    public DateOnly? EffectiveDate { get; init; }
    public string? Source { get; init; }
}
