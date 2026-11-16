using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Organizations;

internal sealed class CompanyRow : ITrackedStorageRow
{
    public Guid Id { get; set; }
    public required string Code { get; set; }
    public required string NameArabic { get; set; }
    public string? NameEnglish { get; set; }
    public required string BaseCurrencyCode { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
