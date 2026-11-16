namespace PetraERP.Infrastructure.Security;

internal sealed class UserRoleRow
{
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
