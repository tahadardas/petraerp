namespace PetraERP.Infrastructure.Security;

/// <summary>Explicit grant allowing a user's role permissions to apply inside one company.</summary>
internal sealed class UserCompanyScopeRow
{
    public Guid UserId { get; set; }
    public Guid CompanyId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
