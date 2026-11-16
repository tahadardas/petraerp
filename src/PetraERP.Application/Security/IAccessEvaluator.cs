namespace PetraERP.Application.Security;

/// <summary>
/// Evaluates User → Permission → Company scope → Branch scope from current database state.
/// Request identifiers are treated as the requested target only, never as proof of access.
/// </summary>
public interface IAccessEvaluator
{
    Task<AccessDecision> EvaluatePermissionAsync(Guid userId, PermissionDefinition permission, Guid? companyId, Guid? branchId,
        CancellationToken cancellationToken);

    Task<AccessDecision> EvaluateCompanyScopeAsync(Guid userId, Guid companyId, CancellationToken cancellationToken);
}
