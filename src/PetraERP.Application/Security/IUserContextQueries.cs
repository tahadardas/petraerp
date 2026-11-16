namespace PetraERP.Application.Security;

/// <summary>Read models for the authenticated user's own profile and permitted organization context.</summary>
public interface IUserContextQueries
{
    Task<CurrentUserProfile?> GetProfileAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken);

    /// <summary>Active companies with an explicit company-scope grant, in code order.</summary>
    Task<IReadOnlyList<CompanyContext>> GetCompaniesAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Active branches with an explicit branch-scope grant inside an already authorized company.</summary>
    Task<IReadOnlyList<BranchContext>> GetBranchesAsync(Guid userId, Guid companyId, CancellationToken cancellationToken);
}
