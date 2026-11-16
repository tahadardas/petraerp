using PetraERP.Application.Security;

namespace PetraERP.Api.Configuration;

/// <summary>Security settings bound from <c>Petra:Security</c>. The signing key is a secret and never in source control.</summary>
internal sealed class PetraSecurityOptions
{
    public const string SectionName = "Petra:Security";
    public const int MinimumSigningKeyBytes = 32;

    public SecurityTokenOptions Tokens { get; set; } = new();
    public SecurityLockoutOptions Lockout { get; set; } = new();
    public SecurityPasswordOptions Passwords { get; set; } = new();
    public SecurityRateLimitOptions RateLimits { get; set; } = new();

    internal byte[] SigningKeyBytes()
    {
        if (string.IsNullOrWhiteSpace(Tokens.SigningKey))
        {
            throw new InvalidOperationException(
                "Petra:Security:Tokens:SigningKey is not configured. Provide a Base64 key of at least 32 random bytes through " +
                "user-secrets (development) or a protected environment variable (installation). See docs/security/AUTHENTICATION.md.");
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(Tokens.SigningKey.Trim());
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("Petra:Security:Tokens:SigningKey must be Base64.", exception);
        }

        if (key.Length < MinimumSigningKeyBytes)
        {
            throw new InvalidOperationException("Petra:Security:Tokens:SigningKey must decode to at least 32 bytes.");
        }

        return key;
    }

    internal AuthenticationSettings ToAuthenticationSettings() => new()
    {
        MaxFailedAccessAttempts = Lockout.MaxFailedAttempts,
        LockoutDuration = TimeSpan.FromMinutes(Lockout.LockoutMinutes),
        AccessTokenLifetime = TimeSpan.FromMinutes(Tokens.AccessTokenMinutes),
        RefreshTokenIdleLifetime = TimeSpan.FromHours(Tokens.RefreshTokenIdleHours),
        SessionAbsoluteLifetime = TimeSpan.FromHours(Tokens.SessionAbsoluteHours),
        PasswordHashIterations = Passwords.Pbkdf2Iterations,
    };

    internal void Validate()
    {
        _ = SigningKeyBytes();
        if (string.IsNullOrWhiteSpace(Tokens.Issuer) || string.IsNullOrWhiteSpace(Tokens.Audience))
        {
            throw new InvalidOperationException("Petra:Security:Tokens:Issuer and Audience are required.");
        }

        if (Tokens.ClockSkewSeconds is < 0 or > 120)
        {
            throw new InvalidOperationException("Petra:Security:Tokens:ClockSkewSeconds must be between 0 and 120.");
        }

        if (RateLimits.AuthenticationPermitsPerMinute is < 1 or > 100_000 || RateLimits.ApiPermitsPerMinute is < 10 or > 1_000_000)
        {
            throw new InvalidOperationException("Petra:Security:RateLimits values are out of range.");
        }

        ToAuthenticationSettings().Validate();
    }
}

internal sealed class SecurityTokenOptions
{
    public string Issuer { get; set; } = "PetraERP";
    public string Audience { get; set; } = "PetraERP.Clients";
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 10;
    public int RefreshTokenIdleHours { get; set; } = 8;
    public int SessionAbsoluteHours { get; set; } = 12;
    public int ClockSkewSeconds { get; set; } = 30;
}

internal sealed class SecurityLockoutOptions
{
    public int MaxFailedAttempts { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
}

internal sealed class SecurityPasswordOptions
{
    public int Pbkdf2Iterations { get; set; } = AuthenticationSettings.MinimumPasswordIterations;
}

internal sealed class SecurityRateLimitOptions
{
    public int AuthenticationPermitsPerMinute { get; set; } = 10;
    public int ApiPermitsPerMinute { get; set; } = 600;
}
