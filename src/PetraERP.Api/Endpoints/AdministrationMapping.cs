using PetraERP.Application.Common;
using PetraERP.Application.Security;
using PetraERP.Contracts.Administration;
using PetraERP.Contracts.Audit;
using AppAuditType = PetraERP.Application.Security.SecurityAuditEventType;
using ContractAuditType = PetraERP.Contracts.Audit.SecurityAuditEventType;
using ContractScope = PetraERP.Contracts.Administration.PermissionScope;

namespace PetraERP.Api.Endpoints;

/// <summary>Explicit Application → Contracts mapping. No storage row or domain object crosses the HTTP boundary.</summary>
internal static class AdministrationMapping
{
    internal static UserDetailsResponse ToResponse(this UserDetails user) => new()
    {
        Id = user.Id,
        UserName = user.UserName,
        DisplayNameArabic = user.DisplayNameArabic,
        DisplayNameEnglish = user.DisplayNameEnglish,
        IsActive = user.IsActive,
        MustChangePassword = user.MustChangePassword,
        AccessFailedCount = user.AccessFailedCount,
        LockoutEndUtc = user.LockoutEndUtc,
        LastLoginAtUtc = user.LastLoginAtUtc,
        PasswordChangedAtUtc = user.PasswordChangedAtUtc,
        CreatedAtUtc = user.CreatedAtUtc,
        UpdatedAtUtc = user.UpdatedAtUtc,
        RoleIds = user.RoleIds,
        Scopes = user.Scopes.Select(static scope => new CompanyScopeEntry { CompanyId = scope.CompanyId, BranchIds = scope.BranchIds })
            .ToArray(),
        Version = user.Version.ToBase64(),
    };

    internal static UserPageResponse ToResponse(this PagedResult<UserSummary> page) => new()
    {
        Items = page.Items.Select(static user => new UserSummaryResponse
        {
            Id = user.Id,
            UserName = user.UserName,
            DisplayNameArabic = user.DisplayNameArabic,
            DisplayNameEnglish = user.DisplayNameEnglish,
            IsActive = user.IsActive,
            IsLockedOut = user.IsLockedOut,
            MustChangePassword = user.MustChangePassword,
            LastLoginAtUtc = user.LastLoginAtUtc,
        }).ToArray(),
        Page = page.Page,
        PageSize = page.PageSize,
        TotalCount = page.TotalCount,
    };

    internal static RoleDetailsResponse ToResponse(this RoleDetails role) => new()
    {
        Id = role.Id,
        Code = role.Code,
        NameArabic = role.NameArabic,
        NameEnglish = role.NameEnglish,
        PermissionCodes = role.PermissionCodes,
        CreatedAtUtc = role.CreatedAtUtc,
        UpdatedAtUtc = role.UpdatedAtUtc,
        Version = role.Version.ToBase64(),
    };

    internal static RoleListResponse ToResponse(this IReadOnlyList<RoleSummary> roles) => new()
    {
        Items = roles.Select(static role => new RoleSummaryResponse
        {
            Id = role.Id,
            Code = role.Code,
            NameArabic = role.NameArabic,
            NameEnglish = role.NameEnglish,
            PermissionCount = role.PermissionCount,
            UserCount = role.UserCount,
        }).ToArray(),
    };

    internal static PermissionListResponse PermissionCatalogResponse() => new()
    {
        Items = PermissionCatalog.All.Select(static permission => new PermissionResponse
        {
            Code = permission.Code,
            Scope = (ContractScope)(int)permission.Scope,
        }).ToArray(),
    };

    internal static OrganizationListResponse ToResponse(this IReadOnlyList<OrganizationCompany> companies) => new()
    {
        Items = companies.Select(static company => new OrganizationCompanyResponse
        {
            Id = company.Id,
            Code = company.Code,
            NameArabic = company.NameArabic,
            NameEnglish = company.NameEnglish,
            IsActive = company.IsActive,
            Branches = company.Branches.Select(static branch => new OrganizationBranchResponse
            {
                Id = branch.Id,
                Code = branch.Code,
                NameArabic = branch.NameArabic,
                NameEnglish = branch.NameEnglish,
                IsActive = branch.IsActive,
            }).ToArray(),
        }).ToArray(),
    };

    internal static SecurityAuditPageResponse ToResponse(this PagedResult<SecurityAuditRecord> page) => new()
    {
        Items = page.Items.Select(static record => new SecurityAuditEventResponse
        {
            Id = record.Id,
            OccurredAtUtc = record.OccurredAtUtc,
            EventType = (ContractAuditType)(int)record.EventType,
            UserId = record.UserId,
            UserNameSnapshot = record.UserNameSnapshot,
            ActorUserId = record.ActorUserId,
            SessionId = record.SessionId,
            CompanyId = record.CompanyId,
            BranchId = record.BranchId,
            IpAddress = record.IpAddress,
            UserAgent = record.UserAgent,
            CorrelationId = record.CorrelationId,
            Reason = record.Reason,
            Details = record.Details,
        }).ToArray(),
        Page = page.Page,
        PageSize = page.PageSize,
        TotalCount = page.TotalCount,
    };

    internal static AppAuditType? ToApplication(this ContractAuditType? type) =>
        type is { } value && Enum.IsDefined(value) ? (AppAuditType)(int)value : null;
}
