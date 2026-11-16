using System.ComponentModel.DataAnnotations;

namespace PetraERP.Contracts.Administration;

public enum PermissionScope
{
    Installation = 1,
    Company = 2,
    Branch = 3,
}

public sealed record PermissionResponse
{
    public required string Code { get; init; }
    public required PermissionScope Scope { get; init; }
}

public sealed record PermissionListResponse
{
    public required IReadOnlyList<PermissionResponse> Items { get; init; }
}

public sealed record RoleSummaryResponse
{
    public required Guid Id { get; init; }
    public required string Code { get; init; }
    public required string NameArabic { get; init; }
    public string? NameEnglish { get; init; }
    public required int PermissionCount { get; init; }
    public required int UserCount { get; init; }
}

public sealed record RoleListResponse
{
    public required IReadOnlyList<RoleSummaryResponse> Items { get; init; }
}

public sealed record RoleDetailsResponse
{
    public required Guid Id { get; init; }
    public required string Code { get; init; }
    public required string NameArabic { get; init; }
    public string? NameEnglish { get; init; }
    public required IReadOnlyList<string> PermissionCodes { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required DateTimeOffset UpdatedAtUtc { get; init; }
    public required string Version { get; init; }
}

public sealed record CreateRoleRequest
{
    [Required]
    [StringLength(64, MinimumLength = 2)]
    [RegularExpression("^[A-Za-z0-9._-]+$")]
    public required string Code { get; init; }

    [Required]
    [StringLength(200, MinimumLength = 1)]
    public required string NameArabic { get; init; }

    [StringLength(200)]
    public string? NameEnglish { get; init; }

    [Required]
    [MaxLength(200)]
    public required IReadOnlyList<string> PermissionCodes { get; init; }
}

public sealed record UpdateRoleRequest
{
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public required string NameArabic { get; init; }

    [StringLength(200)]
    public string? NameEnglish { get; init; }

    [Required]
    [MaxLength(200)]
    public required IReadOnlyList<string> PermissionCodes { get; init; }

    [Required]
    [StringLength(12, MinimumLength = 12)]
    public required string Version { get; init; }
}

public sealed record OrganizationBranchResponse
{
    public required Guid Id { get; init; }
    public required string Code { get; init; }
    public required string NameArabic { get; init; }
    public string? NameEnglish { get; init; }
    public required bool IsActive { get; init; }
}

public sealed record OrganizationCompanyResponse
{
    public required Guid Id { get; init; }
    public required string Code { get; init; }
    public required string NameArabic { get; init; }
    public string? NameEnglish { get; init; }
    public required bool IsActive { get; init; }
    public required IReadOnlyList<OrganizationBranchResponse> Branches { get; init; }
}

public sealed record OrganizationListResponse
{
    public required IReadOnlyList<OrganizationCompanyResponse> Items { get; init; }
}
