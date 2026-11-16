namespace PetraERP.Infrastructure.Persistence;

internal interface ITrackedStorageRow
{
    DateTimeOffset CreatedAtUtc { get; set; }
    DateTimeOffset UpdatedAtUtc { get; set; }
    byte[] RowVersion { get; set; }
}
