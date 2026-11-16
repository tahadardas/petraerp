using PetraERP.Application.Common;

namespace PetraERP.Application.Security;

/// <summary>The authenticated administrator performing a change, with request evidence for audit.</summary>
public sealed record AdministrativeActor(Guid UserId, ClientContext Client);

public sealed record UserSummary(
    Guid Id,
    string UserName,
    string DisplayNameArabic,
    string? DisplayNameEnglish,
    bool IsActive,
    bool IsLockedOut,
    bool MustChangePassword,
    DateTimeOffset? LastLoginAtUtc);

public sealed record UserDetails(
    Guid Id,
    string UserName,
    string DisplayNameArabic,
    string? DisplayNameEnglish,
    bool IsActive,
    bool MustChangePassword,
    int AccessFailedCount,
    DateTimeOffset? LockoutEndUtc,
    DateTimeOffset? LastLoginAtUtc,
    DateTimeOffset PasswordChangedAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<Guid> RoleIds,
    IReadOnlyList<CompanyScopeGrant> Scopes,
    ConcurrencyToken Version);

/// <summary>An explicit company grant with the branches granted inside it.</summary>
public sealed record CompanyScopeGrant(Guid CompanyId, IReadOnlyList<Guid> BranchIds);

public sealed record UserListQuery(string? Search, PageRequest Page);

public sealed class CreateUserCommand
{
    public CreateUserCommand(string userName, string displayNameArabic, string? displayNameEnglish, string initialPassword)
    {
        ArgumentNullException.ThrowIfNull(userName);
        ArgumentNullException.ThrowIfNull(displayNameArabic);
        ArgumentNullException.ThrowIfNull(initialPassword);
        UserName = userName;
        DisplayNameArabic = displayNameArabic;
        DisplayNameEnglish = displayNameEnglish;
        InitialPassword = initialPassword;
    }

    public string UserName { get; }
    public string DisplayNameArabic { get; }
    public string? DisplayNameEnglish { get; }
    public string InitialPassword { get; }

    public override string ToString() => $"CreateUserCommand({UserName})";
}

public sealed record UpdateUserProfileCommand(string DisplayNameArabic, string? DisplayNameEnglish, ConcurrencyToken Version);

public sealed class ResetPasswordCommand
{
    public ResetPasswordCommand(string temporaryPassword, string reason, ConcurrencyToken version)
    {
        ArgumentNullException.ThrowIfNull(temporaryPassword);
        ArgumentNullException.ThrowIfNull(reason);
        ArgumentNullException.ThrowIfNull(version);
        TemporaryPassword = temporaryPassword;
        Reason = reason;
        Version = version;
    }

    public string TemporaryPassword { get; }
    public string Reason { get; }
    public ConcurrencyToken Version { get; }

    public override string ToString() => "ResetPasswordCommand";
}

public sealed record RoleSummary(Guid Id, string Code, string NameArabic, string? NameEnglish, int PermissionCount, int UserCount);

public sealed record RoleDetails(
    Guid Id,
    string Code,
    string NameArabic,
    string? NameEnglish,
    IReadOnlyList<string> PermissionCodes,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    ConcurrencyToken Version);

public sealed record CreateRoleCommand(string Code, string NameArabic, string? NameEnglish, IReadOnlyCollection<string> PermissionCodes);

public sealed record UpdateRoleCommand(string NameArabic, string? NameEnglish, IReadOnlyCollection<string> PermissionCodes,
    ConcurrencyToken Version);

public sealed record OrganizationBranch(Guid Id, string Code, string NameArabic, string? NameEnglish, bool IsActive);

public sealed record OrganizationCompany(Guid Id, string Code, string NameArabic, string? NameEnglish, bool IsActive,
    IReadOnlyList<OrganizationBranch> Branches);
