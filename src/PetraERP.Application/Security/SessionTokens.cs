namespace PetraERP.Application.Security;

/// <summary>Credentials returned once to the client. Only hashes of refresh tokens are stored.</summary>
public sealed class SessionTokens
{
    public SessionTokens(Guid sessionId, Guid userId, IssuedAccessToken accessToken, string refreshToken,
        DateTimeOffset refreshTokenExpiresAtUtc, DateTimeOffset sessionExpiresAtUtc, bool mustChangePassword)
    {
        ArgumentNullException.ThrowIfNull(accessToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);
        SessionId = sessionId;
        UserId = userId;
        AccessToken = accessToken;
        RefreshToken = refreshToken;
        RefreshTokenExpiresAtUtc = refreshTokenExpiresAtUtc;
        SessionExpiresAtUtc = sessionExpiresAtUtc;
        MustChangePassword = mustChangePassword;
    }

    public Guid SessionId { get; }
    public Guid UserId { get; }
    public IssuedAccessToken AccessToken { get; }
    public string RefreshToken { get; }
    public DateTimeOffset RefreshTokenExpiresAtUtc { get; }
    public DateTimeOffset SessionExpiresAtUtc { get; }
    public bool MustChangePassword { get; }

    public override string ToString() => $"SessionTokens(session {SessionId})";
}
