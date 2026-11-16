namespace PetraERP.Application.Security;

/// <summary>Stored security event types. Ordinals are persisted; never renumber existing members.</summary>
public enum SecurityAuditEventType
{
    LoginSucceeded = 1,
    LoginFailed = 2,
    LoginRejectedLockedOut = 3,
    LoginRejectedDisabled = 4,
    AccountLockedOut = 5,
    TokenRefreshed = 6,
    RefreshTokenReuseDetected = 7,
    RefreshRejected = 8,
    Logout = 9,
    LogoutAllSessions = 10,
    PasswordChanged = 11,
    PasswordChangeFailed = 12,
    BootstrapCompleted = 13,
    UserCreated = 14,
    UserProfileUpdated = 15,
    UserDisabled = 16,
    UserEnabled = 17,
    UserUnlocked = 18,
    PasswordReset = 19,
    UserRolesChanged = 20,
    UserScopesChanged = 21,
    SessionsRevokedByAdministrator = 22,
    RoleCreated = 23,
    RoleUpdated = 24,
    AuthorizationDenied = 25,

    /// <summary>A non-financial action performed by an external module (financial effects are audited by the journal trail).</summary>
    ModuleAction = 26,
}
