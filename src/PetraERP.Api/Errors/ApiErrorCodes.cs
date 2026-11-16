namespace PetraERP.Api.Errors;

/// <summary>Stable machine-readable error codes returned in the ProblemDetails <c>code</c> extension.</summary>
internal static class ApiErrorCodes
{
    internal const string Unauthenticated = "auth.unauthenticated";
    internal const string TokenExpired = "auth.token_expired";
    internal const string TokenInvalid = "auth.token_invalid";
    internal const string SessionRevoked = "auth.session_revoked";
    internal const string InvalidCredentials = "auth.invalid_credentials";
    internal const string LockedOut = "auth.locked_out";
    internal const string AccountDisabled = "auth.account_disabled";
    internal const string InvalidRefreshToken = "auth.invalid_refresh_token";
    internal const string RefreshTokenReused = "auth.refresh_token_reused";
    internal const string SessionExpired = "auth.session_expired";
    internal const string InvalidCurrentPassword = "auth.invalid_current_password";
    internal const string PasswordChangeRequired = "auth.password_change_required";
    internal const string Forbidden = "auth.forbidden";
    internal const string PermissionDenied = "auth.permission_denied";
    internal const string CompanyScopeDenied = "auth.company_scope_denied";
    internal const string BranchScopeDenied = "auth.branch_scope_denied";
    internal const string UserInactive = "auth.user_inactive";

    internal const string RequestInvalid = "request.invalid";
    internal const string RequestMalformed = "request.malformed";
    internal const string RequestTooLarge = "request.too_large";
    internal const string RequestRateLimited = "request.rate_limited";
    internal const string IdempotencyKeyInvalid = "request.idempotency_key_invalid";
    internal const string VersionInvalid = "request.version_invalid";
    internal const string NotFound = "resource.not_found";
    internal const string MethodNotAllowed = "request.method_not_allowed";
    internal const string ConcurrencyConflict = "concurrency.conflict";

    internal const string IdempotencyConflict = "ledger.idempotency_conflict";
    internal const string PostingRejected = "ledger.posting_rejected";

    internal const string TransientFailure = "server.transient_failure";
    internal const string Unexpected = "server.unexpected";
    internal const string ServiceUnavailable = "server.unavailable";
}
