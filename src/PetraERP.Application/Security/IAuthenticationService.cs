namespace PetraERP.Application.Security;

/// <summary>Authoritative authentication, refresh-token rotation and session revocation.</summary>
public interface IAuthenticationService
{
    Task<AuthenticationOutcome> LoginAsync(LoginCommand command, ClientContext client, CancellationToken cancellationToken);

    Task<AuthenticationOutcome> RefreshAsync(string refreshToken, ClientContext client, CancellationToken cancellationToken);

    /// <summary>Revokes the caller's current session. Idempotent.</summary>
    Task LogoutAsync(Guid userId, Guid sessionId, ClientContext client, CancellationToken cancellationToken);

    /// <summary>Revokes every active session of the user, including the current one. Returns the revoked count.</summary>
    Task<int> LogoutAllAsync(Guid userId, Guid sessionId, ClientContext client, CancellationToken cancellationToken);

    /// <summary>Verifies the current password, applies the policy, revokes all sessions and opens a new one.</summary>
    Task<AuthenticationOutcome> ChangePasswordAsync(ChangePasswordCommand command, ClientKind clientKind, ClientContext client,
        CancellationToken cancellationToken);
}
