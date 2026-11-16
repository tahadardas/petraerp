using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.AccountingCalendars;

internal sealed class AccountingPeriodRow : ITrackedStorageRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid FiscalYearId { get; set; }
    public required string Code { get; set; }
    public DateOnly RangeStart { get; set; }
    public DateOnly RangeEnd { get; set; }

    /// <summary>
    /// 0 = Open, 1 = SoftClosed, 2 = Closed — matches <see cref="PetraERP.Domain.AccountingCalendars.AccountingPeriodState"/>.
    /// </summary>
    public byte State { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    // Navigation
    public FiscalYearRow? FiscalYear { get; set; }
}
