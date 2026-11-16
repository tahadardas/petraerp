using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Organizations;

internal sealed class CompanyAdditionalCurrencyRow : ITrackedStorageRow
{
    public Guid CompanyId { get; set; }
    public required string CurrencyCode { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
