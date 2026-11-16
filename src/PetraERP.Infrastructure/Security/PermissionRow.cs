namespace PetraERP.Infrastructure.Security;

/// <summary>Referential anchor for permission codes; rows are seeded from the application permission catalog.</summary>
internal sealed class PermissionRow
{
    public required string Code { get; set; }
}
