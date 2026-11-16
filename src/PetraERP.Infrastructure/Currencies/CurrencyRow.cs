using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Currencies;

internal sealed class CurrencyRow : ITrackedStorageRow
{
    public required string Code { get; set; }
    public required string NameArabic { get; set; }
    public string? NameEnglish { get; set; }
    public bool IsActive { get; set; }
    /// <summary>Display symbol, for example "$". Optional.</summary>
    public string? Symbol { get; set; }
    public string? MinorUnitNameArabic { get; set; }
    public string? MinorUnitNameEnglish { get; set; }
    /// <summary>Decimal places used when rounding amounts in this currency (0..4).</summary>
    public byte DecimalPlaces { get; set; } = 2;
    /// <summary>A <see cref="PetraERP.Domain.FinancialValues.CurrencyRoundingMode"/> value.</summary>
    public byte RoundingMode { get; set; } = 1;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
