using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.GeneralLedger;

/// <summary>
/// Persistent journal line. All monetary amounts stored as decimal(19,4).
/// ExchangeRate stored as decimal(19,8). Rounding stored as tinyint matching
/// <see cref="System.MidpointRounding"/> ordinal: 0=ToEven, 1=AwayFromZero,
/// 2=ToZero, 3=ToNegativeInfinity, 4=ToPositiveInfinity.
/// </summary>
internal sealed class JournalLineRow : ITrackedStorageRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid JournalId { get; set; }
    public Guid AccountId { get; set; }
    public Guid? CostCenterId { get; set; }

    // Transaction amounts (in the transaction currency)
    public required string TransactionCurrencyCode { get; set; }
    public decimal TransactionDebit { get; set; }
    public decimal TransactionCredit { get; set; }

    // Base currency amounts (converted)
    public required string BaseCurrencyCode { get; set; }
    public decimal BaseDebit { get; set; }
    public decimal BaseCredit { get; set; }

    // Rate used for conversion: source→target, stored as rate value
    public required string RateSourceCurrencyCode { get; set; }
    public required string RateTargetCurrencyCode { get; set; }
    public decimal RateValue { get; set; }

    public byte Rounding { get; set; }
    public required string Description { get; set; }

    // Populated only for reversal lines
    public Guid? ReversesLineId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    // Navigation
    public JournalEntryRow? Journal { get; set; }
}
