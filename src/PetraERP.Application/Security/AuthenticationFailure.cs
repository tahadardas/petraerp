namespace PetraERP.Application.Security;

public enum AuthenticationFailure
{
    /// <summary>Unknown login name or wrong password; indistinguishable by design.</summary>
    InvalidCredentials = 1,
    LockedOut = 2,
    AccountDisabled = 3,
    InvalidRefreshToken = 4,
    RefreshTokenReused = 5,
    SessionExpired = 6,
    InvalidCurrentPassword = 7,
    PasswordPolicyViolation = 8,
}
