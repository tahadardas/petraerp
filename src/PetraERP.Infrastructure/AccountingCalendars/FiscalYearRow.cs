using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.AccountingCalendars;

internal sealed class FiscalYearRow : ITrackedStorageRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public required string Code { get; set; }
    public DateOnly RangeStart { get; set; }
    public DateOnly RangeEnd { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    // Navigation
    public List<AccountingPeriodRow> Periods { get; set; } = [];
}
