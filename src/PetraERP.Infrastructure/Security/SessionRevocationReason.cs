namespace PetraERP.Infrastructure.Security;

/// <summary>Persisted ordinals; never renumber.</summary>
internal enum SessionRevocationReason : byte
{
    Logout = 1,
    LogoutAll = 2,
    RefreshTokenReuse = 3,
    UserDisabled = 4,
    PasswordChanged = 5,
    PasswordReset = 6,
    AdministratorRevoked = 7,
    SecurityStampChanged = 8,
}
