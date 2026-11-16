using System.ComponentModel.DataAnnotations;

namespace PetraERP.Contracts.Administration;

public sealed record UserSummaryResponse
{
    public required Guid Id { get; init; }
    public required string UserName { get; init; }
    public required string DisplayNameArabic { get; init; }
    public string? DisplayNameEnglish { get; init; }
    public required bool IsActive { get; init; }
    public required bool IsLockedOut { get; init; }
    public required bool MustChangePassword { get; init; }
    public DateTimeOffset? LastLoginAtUtc { get; init; }
}

public sealed record UserPageResponse
{
    public required IReadOnlyList<UserSummaryResponse> Items { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required long TotalCount { get; init; }
}

public sealed record CompanyScopeEntry
{
    [Required]
    public required Guid CompanyId { get; init; }

    [Required]
    [MaxLength(500)]
    public required IReadOnlyList<Guid> BranchIds { get; init; }
}

public sealed record UserDetailsResponse
{
    public required Guid Id { get; init; }
    public required string UserName { get; init; }
    public required string DisplayNameArabic { get; init; }
    public string? DisplayNameEnglish { get; init; }
    public required bool IsActive { get; init; }
    public required bool MustChangePassword { get; init; }
    public required int AccessFailedCount { get; init; }
    public DateTimeOffset? LockoutEndUtc { get; init; }
    public DateTimeOffset? LastLoginAtUtc { get; init; }
    public required DateTimeOffset PasswordChangedAtUtc { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required DateTimeOffset UpdatedAtUtc { get; init; }
    public required IReadOnlyList<Guid> RoleIds { get; init; }
    public required IReadOnlyList<CompanyScopeEntry> Scopes { get; init; }

    /// <summary>Opaque concurrency version (canonical Base64 of an 8-byte rowversion). Send it back unchanged.</summary>
    public required string Version { get; init; }
}

public sealed record CreateUserRequest
{
    [Required]
    [StringLength(64, MinimumLength = 3)]
    [RegularExpression("^[A-Za-z0-9._-]+$")]
    public required string UserName { get; init; }

    [Required]
    [StringLength(200, MinimumLength = 1)]
    public required string DisplayNameArabic { get; init; }

    [StringLength(200)]
    public string? DisplayNameEnglish { get; init; }

    /// <summary>Temporary password; the user must change it at first sign-in.</summary>
    [Required]
    [StringLength(128, MinimumLength = 10)]
    public required string InitialPassword { get; init; }

    public override string ToString() => $"CreateUserRequest({UserName})";
}

public sealed record UpdateUserProfileRequest
{
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public required string DisplayNameArabic { get; init; }

    [StringLength(200)]
    public string? DisplayNameEnglish { get; init; }

    [Required]
    [StringLength(12, MinimumLength = 12)]
    public required string Version { get; init; }
}

public sealed record SetUserStatusRequest
{
    public required bool IsActive { get; init; }

    [Required]
    [StringLength(500, MinimumLength = 1)]
    public required string Reason { get; init; }

    [Required]
    [StringLength(12, MinimumLength = 12)]
    public required string Version { get; init; }
}

public sealed record UnlockUserRequest
{
    [Required]
    [StringLength(12, MinimumLength = 12)]
    public required string Version { get; init; }
}

public sealed record ResetUserPasswordRequest
{
    [Required]
    [StringLength(128, MinimumLength = 10)]
    public required string TemporaryPassword { get; init; }

    [Required]
    [StringLength(500, MinimumLength = 1)]
    public required string Reason { get; init; }

    [Required]
    [StringLength(12, MinimumLength = 12)]
    public required string Version { get; init; }

    public override string ToString() => "ResetUserPasswordRequest";
}

public sealed record SetUserRolesRequest
{
    [Required]
    [MaxLength(100)]
    public required IReadOnlyList<Guid> RoleIds { get; init; }

    [Required]
    [StringLength(12, MinimumLength = 12)]
    public required string Version { get; init; }
}

public sealed record SetUserScopesRequest
{
    [Required]
    [MaxLength(200)]
    public required IReadOnlyList<CompanyScopeEntry> Scopes { get; init; }

    [Required]
    [StringLength(12, MinimumLength = 12)]
    public required string Version { get; init; }
}

public sealed record RevokeUserSessionsRequest
{
    [Required]
    [StringLength(500, MinimumLength = 1)]
    public required string Reason { get; init; }
}

public sealed record RevokeUserSessionsResponse
{
    public required int RevokedSessions { get; init; }
}
