using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using PetraERP.Api.Configuration;
using PetraERP.Application.Security;

namespace PetraERP.Api.Security;

/// <summary>
/// Issues HS256 JWT access tokens carrying only the user id, session id and a unique token id. Authorization
/// facts (roles, scopes, account state) are never embedded: they are resolved from the database per request.
/// </summary>
internal sealed class JwtAccessTokenIssuer : IAccessTokenIssuer
{
    private readonly JsonWebTokenHandler handler = new();
    private readonly SigningCredentials credentials;
    private readonly string issuer;
    private readonly string audience;
    private readonly TimeSpan lifetime;

    public JwtAccessTokenIssuer(IOptions<PetraSecurityOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var security = options.Value;
        credentials = new SigningCredentials(new SymmetricSecurityKey(security.SigningKeyBytes()), SecurityAlgorithms.HmacSha256);
        issuer = security.Tokens.Issuer;
        audience = security.Tokens.Audience;
        lifetime = TimeSpan.FromMinutes(security.Tokens.AccessTokenMinutes);
    }

    public IssuedAccessToken Issue(Guid userId, Guid sessionId, DateTimeOffset issuedAtUtc)
    {
        // JWT times have one-second resolution; report the exact expiry the token carries.
        var issued = DateTimeOffset.FromUnixTimeSeconds(issuedAtUtc.ToUnixTimeSeconds());
        var expires = issued + lifetime;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = issued.UtcDateTime,
            NotBefore = issued.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = credentials,
            Claims = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [PetraClaims.Subject] = userId.ToString("D"),
                [PetraClaims.Session] = sessionId.ToString("D"),
                [PetraClaims.TokenId] = Guid.NewGuid().ToString("N"),
            },
        };
        return new IssuedAccessToken(handler.CreateToken(descriptor), expires);
    }
}
