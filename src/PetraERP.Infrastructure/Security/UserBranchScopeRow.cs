namespace PetraERP.Infrastructure.Security;

/// <summary>
/// Explicit branch grant. Composite foreign keys require the company grant and that the branch
/// belongs to the same company, so a cross-company branch reference cannot be stored.
/// </summary>
internal sealed class UserBranchScopeRow
{
    public Guid UserId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid BranchId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
