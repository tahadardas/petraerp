using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Currencies;

/// <summary>
/// One append-only entry of a company's exchange-rate register: base units per one unit of <see cref="CurrencyCode"/>,
/// effective from <see cref="EffectiveDate"/>. Corrections are new entries with a reason; nothing is overwritten, so the
/// register is its own audit trail. Documents copy the rate they used and never read it back.
/// </summary>
internal sealed class ExchangeRateRow : ITrackedStorageRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public required string CurrencyCode { get; set; }
    public required string BaseCurrencyCode { get; set; }
    public decimal Rate { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public required string Source { get; set; }
    public required string Reason { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
