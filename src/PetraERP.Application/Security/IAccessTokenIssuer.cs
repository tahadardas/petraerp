namespace PetraERP.Application.Security;

/// <summary>Issues short-lived signed access tokens bound to a server-side session.</summary>
public interface IAccessTokenIssuer
{
    IssuedAccessToken Issue(Guid userId, Guid sessionId, DateTimeOffset issuedAtUtc);
}

public sealed record IssuedAccessToken(string Value, DateTimeOffset ExpiresAtUtc)
{
    // Never print token material in logs or diagnostics.
    public override string ToString() => $"AccessToken(expires {ExpiresAtUtc:O})";
}
