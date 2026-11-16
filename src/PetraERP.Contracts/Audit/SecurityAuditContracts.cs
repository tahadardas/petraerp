namespace PetraERP.Contracts.Audit;

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
}

public sealed record SecurityAuditEventResponse
{
    public required Guid Id { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }
    public required SecurityAuditEventType EventType { get; init; }
    public Guid? UserId { get; init; }
    public string? UserNameSnapshot { get; init; }
    public Guid? ActorUserId { get; init; }
    public Guid? SessionId { get; init; }
    public Guid? CompanyId { get; init; }
    public Guid? BranchId { get; init; }
    public string? IpAddress { get; init; }
    public string? UserAgent { get; init; }
    public string? CorrelationId { get; init; }
    public string? Reason { get; init; }

    /// <summary>Compact JSON with event-specific facts. Never contains passwords or tokens.</summary>
    public string? Details { get; init; }
}

public sealed record SecurityAuditPageResponse
{
    public required IReadOnlyList<SecurityAuditEventResponse> Items { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required long TotalCount { get; init; }
}
