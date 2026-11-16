namespace PetraERP.Application.Security;

/// <summary>Validated authentication and session policy for one installation.</summary>
public sealed record AuthenticationSettings
{
    public const int MinimumPasswordIterations = 210_000;

    public required int MaxFailedAccessAttempts { get; init; }
    public required TimeSpan LockoutDuration { get; init; }
    public required TimeSpan AccessTokenLifetime { get; init; }
    public required TimeSpan RefreshTokenIdleLifetime { get; init; }
    public required TimeSpan SessionAbsoluteLifetime { get; init; }
    public required int PasswordHashIterations { get; init; }

    public static AuthenticationSettings Default { get; } = new()
    {
        MaxFailedAccessAttempts = 5,
        LockoutDuration = TimeSpan.FromMinutes(15),
        AccessTokenLifetime = TimeSpan.FromMinutes(10),
        RefreshTokenIdleLifetime = TimeSpan.FromHours(8),
        SessionAbsoluteLifetime = TimeSpan.FromHours(12),
        PasswordHashIterations = MinimumPasswordIterations,
    };

    /// <summary>Rejects unsafe configuration at startup instead of weakening security silently.</summary>
    public void Validate()
    {
        if (MaxFailedAccessAttempts is < 3 or > 20)
        {
            throw new InvalidOperationException("MaxFailedAccessAttempts must be between 3 and 20.");
        }

        if (LockoutDuration < TimeSpan.FromMinutes(1) || LockoutDuration > TimeSpan.FromDays(1))
        {
            throw new InvalidOperationException("LockoutDuration must be between 1 minute and 1 day.");
        }

        if (AccessTokenLifetime < TimeSpan.FromMinutes(1) || AccessTokenLifetime > TimeSpan.FromMinutes(30))
        {
            throw new InvalidOperationException("AccessTokenLifetime must be between 1 and 30 minutes.");
        }

        if (RefreshTokenIdleLifetime < AccessTokenLifetime || RefreshTokenIdleLifetime > TimeSpan.FromDays(7))
        {
            throw new InvalidOperationException("RefreshTokenIdleLifetime must be at least the access-token lifetime and at most 7 days.");
        }

        if (SessionAbsoluteLifetime < RefreshTokenIdleLifetime || SessionAbsoluteLifetime > TimeSpan.FromDays(30))
        {
            throw new InvalidOperationException("SessionAbsoluteLifetime must be at least the refresh idle lifetime and at most 30 days.");
        }

        if (PasswordHashIterations is < MinimumPasswordIterations or > 5_000_000)
        {
            throw new InvalidOperationException("PasswordHashIterations must be between 210000 and 5000000.");
        }
    }
}
