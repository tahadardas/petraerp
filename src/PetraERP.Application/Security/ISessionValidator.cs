namespace PetraERP.Application.Security;

/// <summary>Checks on every authenticated request that the token's session is still valid in the database.</summary>
public interface ISessionValidator
{
    Task<SessionPrincipal?> ValidateAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken);
}
