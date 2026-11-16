namespace PetraERP.Infrastructure.Security;

internal sealed class RolePermissionRow
{
    public Guid RoleId { get; set; }
    public required string PermissionCode { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
