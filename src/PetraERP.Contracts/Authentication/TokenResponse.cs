namespace PetraERP.Contracts.Authentication;

/// <summary>
/// Session credentials. The access token is a short-lived bearer JWT; the refresh token is single-use
/// and rotates on every refresh. Clients must keep both out of logs and plaintext files.
/// </summary>
public sealed record TokenResponse
{
    public required string TokenType { get; init; }
    public required string AccessToken { get; init; }
    public required DateTimeOffset AccessTokenExpiresAtUtc { get; init; }
    public required string RefreshToken { get; init; }
    public required DateTimeOffset RefreshTokenExpiresAtUtc { get; init; }
    public required Guid SessionId { get; init; }
    public required DateTimeOffset SessionExpiresAtUtc { get; init; }
    public required bool MustChangePassword { get; init; }

    public override string ToString() => $"TokenResponse(session {SessionId})";
}
