using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Security;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Security;

/// <summary>
/// SQL Server implementation of user, role and scope administration. Mutations run under one
/// exclusive administration lock, verify the caller's concurrency version, write an audit event in
/// the same transaction and refuse to leave the installation without an active administrator.
/// </summary>
public sealed class SqlSecurityAdministrationService : ISecurityAdministrationService
{
    private readonly CentralDataContext context;
    private readonly IPasswordHasher hasher;
    private readonly TimeProvider time;

    public SqlSecurityAdministrationService(CentralDataContext context, IPasswordHasher hasher, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(hasher);
        ArgumentNullException.ThrowIfNull(time);
        this.context = context;
        this.hasher = hasher;
        this.time = time;
    }

    public async Task<PagedResult<UserSummary>> ListUsersAsync(UserListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var now = time.GetUtcNow();
        var users = context.Users.AsNoTracking();
        if (SecurityInput.OptionalSearch(query.Search) is { } search)
        {
            var normalized = search.ToUpperInvariant();
            users = users.Where(row => row.NormalizedUserName.Contains(normalized) || row.DisplayNameArabic.Contains(search)
                || (row.DisplayNameEnglish != null && row.DisplayNameEnglish.Contains(search)));
        }

        var total = await users.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var items = await users.OrderBy(static row => row.NormalizedUserName)
            .Skip(query.Page.Skip)
            .Take(query.Page.PageSize)
            .Select(row => new UserSummary(row.Id, row.UserName, row.DisplayNameArabic, row.DisplayNameEnglish, row.IsActive,
                row.LockoutEndUtc != null && row.LockoutEndUtc > now, row.MustChangePassword, row.LastLoginAtUtc))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return new PagedResult<UserSummary>(items.AsReadOnly(), query.Page.Page, query.Page.PageSize, total);
    }

    public async Task<UserDetails> GetUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await context.Users.AsNoTracking().FirstOrDefaultAsync(row => row.Id == userId, cancellationToken)
            .ConfigureAwait(false) ?? throw new ResourceNotFoundException("user");
        var roleIds = await context.UserRoles.AsNoTracking().Where(row => row.UserId == userId)
            .OrderBy(static row => row.RoleId).Select(static row => row.RoleId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var companies = await context.UserCompanyScopes.AsNoTracking().Where(row => row.UserId == userId)
            .OrderBy(static row => row.CompanyId).Select(static row => row.CompanyId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var branches = await context.UserBranchScopes.AsNoTracking().Where(row => row.UserId == userId)
            .Select(static row => new { row.CompanyId, row.BranchId })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var scopes = companies.Select(companyId => new CompanyScopeGrant(companyId,
                branches.Where(branch => branch.CompanyId == companyId).Select(static branch => branch.BranchId)
                    .Order().ToArray()))
            .ToArray();

        return new UserDetails(user.Id, user.UserName, user.DisplayNameArabic, user.DisplayNameEnglish, user.IsActive,
            user.MustChangePassword, user.AccessFailedCount, user.LockoutEndUtc, user.LastLoginAtUtc, user.PasswordChangedAtUtc,
            user.CreatedAtUtc, user.UpdatedAtUtc, roleIds.AsReadOnly(), scopes, ConcurrencyToken.FromBytes(user.RowVersion));
    }

    public async Task<UserDetails> CreateUserAsync(CreateUserCommand command, AdministrativeActor actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(actor);
        var (userName, normalized) = SecurityInput.RequireLoginName(command.UserName, "userName");
        var names = SecurityInput.RequireNames(command.DisplayNameArabic, command.DisplayNameEnglish, "displayNameArabic");
        SecurityInput.RequirePassword(command.InitialPassword, userName, "initialPassword");
        var hash = hasher.Hash(command.InitialPassword);
        var now = time.GetUtcNow();
        var userId = Guid.CreateVersion7(now);

        var transaction = await SecurityAdministrationLock.BeginAsync(context, cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            if (await context.Users.AnyAsync(row => row.NormalizedUserName == normalized, cancellationToken).ConfigureAwait(false))
            {
                throw new ResourceConflictException("security.user_name_taken", "The login name is already in use.");
            }

            context.Users.Add(new UserRow
            {
                Id = userId,
                UserName = userName,
                NormalizedUserName = normalized,
                DisplayNameArabic = names.Arabic,
                DisplayNameEnglish = names.English,
                PasswordAlgorithm = hash.Algorithm,
                PasswordIterations = hash.Iterations,
                PasswordSalt = hash.Salt.ToArray(),
                PasswordHash = hash.Hash.ToArray(),
                PasswordChangedAtUtc = now,
                MustChangePassword = true,
                IsActive = true,
                SecurityStamp = Guid.NewGuid(),
            });
            Audit(now, SecurityAuditEventType.UserCreated, actor, userId, userName, details: new { userName });
            await SaveAsync("security.user_name_taken", cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        return await ReloadUserAsync(userId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<UserDetails> UpdateUserProfileAsync(Guid userId, UpdateUserProfileCommand command, AdministrativeActor actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(actor);
        var names = SecurityInput.RequireNames(command.DisplayNameArabic, command.DisplayNameEnglish, "displayNameArabic");
        var now = time.GetUtcNow();
        var transaction = await SecurityAdministrationLock.BeginAsync(context, cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            var user = await LoadUserForUpdateAsync(userId, command.Version, cancellationToken).ConfigureAwait(false);
            var previous = new { arabic = user.DisplayNameArabic, english = user.DisplayNameEnglish };
            user.DisplayNameArabic = names.Arabic;
            user.DisplayNameEnglish = names.English;
            Touch(user);
            Audit(now, SecurityAuditEventType.UserProfileUpdated, actor, user.Id, user.UserName,
                details: new { previous, current = new { arabic = names.Arabic, english = names.English } });
            await SaveAsync(null, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        return await ReloadUserAsync(userId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<UserDetails> SetUserActiveAsync(Guid userId, bool isActive, string reason, ConcurrencyToken version,
        AdministrativeActor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(actor);
        var validatedReason = SecurityInput.RequireReason(reason, "reason");
        if (!isActive && userId == actor.UserId)
        {
            throw new BusinessRuleException("security.cannot_disable_self", "Administrators cannot disable their own account.");
        }

        var now = time.GetUtcNow();
        var transaction = await SecurityAdministrationLock.BeginAsync(context, cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            var user = await LoadUserForUpdateAsync(userId, version, cancellationToken).ConfigureAwait(false);
            if (user.IsActive != isActive)
            {
                user.IsActive = isActive;
                if (!isActive)
                {
                    user.SecurityStamp = Guid.NewGuid();
                }

                Touch(user);
                Audit(now, isActive ? SecurityAuditEventType.UserEnabled : SecurityAuditEventType.UserDisabled, actor, user.Id,
                    user.UserName, reason: validatedReason);
                await SaveAsync(null, cancellationToken).ConfigureAwait(false);
                if (!isActive)
                {
                    await RevokeAsync(user.Id, SessionRevocationReason.UserDisabled, now, cancellationToken).ConfigureAwait(false);
                    await EnsureAdministrableAsync(cancellationToken).ConfigureAwait(false);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        return await ReloadUserAsync(userId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<UserDetails> UnlockUserAsync(Guid userId, ConcurrencyToken version, AdministrativeActor actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(actor);
        var now = time.GetUtcNow();
        var transaction = await SecurityAdministrationLock.BeginAsync(context, cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            var user = await LoadUserForUpdateAsync(userId, version, cancellationToken).ConfigureAwait(false);
            user.AccessFailedCount = 0;
            user.LockoutEndUtc = null;
            Touch(user);
            Audit(now, SecurityAuditEventType.UserUnlocked, actor, user.Id, user.UserName);
            await SaveAsync(null, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        return await ReloadUserAsync(userId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<UserDetails> ResetPasswordAsync(Guid userId, ResetPasswordCommand command, AdministrativeActor actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(actor);
        var reason = SecurityInput.RequireReason(command.Reason, "reason");
        var now = time.GetUtcNow();
        var transaction = await SecurityAdministrationLock.BeginAsync(context, cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            var user = await LoadUserForUpdateAsync(userId, command.Version, cancellationToken).ConfigureAwait(false);
            SecurityInput.RequirePassword(command.TemporaryPassword, user.UserName, "temporaryPassword");
            var hash = hasher.Hash(command.TemporaryPassword);
            user.PasswordAlgorithm = hash.Algorithm;
            user.PasswordIterations = hash.Iterations;
            user.PasswordSalt = hash.Salt.ToArray();
            user.PasswordHash = hash.Hash.ToArray();
            user.PasswordChangedAtUtc = now;
            user.MustChangePassword = true;
            user.AccessFailedCount = 0;
            user.LockoutEndUtc = null;
            user.SecurityStamp = Guid.NewGuid();
            Touch(user);
            Audit(now, SecurityAuditEventType.PasswordReset, actor, user.Id, user.UserName, reason: reason);
            await SaveAsync(null, cancellationToken).ConfigureAwait(false);
            await RevokeAsync(user.Id, SessionRevocationReason.PasswordReset, now, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        return await ReloadUserAsync(userId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<UserDetails> SetUserRolesAsync(Guid userId, IReadOnlyCollection<Guid> roleIds, ConcurrencyToken version,
        AdministrativeActor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roleIds);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(actor);
        var desired = new HashSet<Guid>(roleIds);
        if (desired.Count != roleIds.Count || desired.Contains(Guid.Empty))
        {
            throw new RequestValidationException("security.duplicate_role", "Role identifiers must be unique and nonempty.", "roleIds");
        }

        var now = time.GetUtcNow();
        var transaction = await SecurityAdministrationLock.BeginAsync(context, cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            var user = await LoadUserForUpdateAsync(userId, version, cancellationToken).ConfigureAwait(false);
            var desiredIds = desired.ToList();
            var roles = await context.Roles.AsNoTracking().Where(row => desiredIds.Contains(row.Id))
                .Select(static row => new { row.Id, row.Code }).ToListAsync(cancellationToken).ConfigureAwait(false);
            if (roles.Count != desired.Count)
            {
                throw new RequestValidationException("security.unknown_role", "An unknown role was supplied.", "roleIds");
            }

            var existing = await context.UserRoles.Where(row => row.UserId == userId).ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var removed = existing.Where(row => !desired.Contains(row.RoleId)).ToList();
            var addedIds = desired.Where(id => existing.TrueForAll(row => row.RoleId != id)).ToList();
            context.UserRoles.RemoveRange(removed);
            context.UserRoles.AddRange(addedIds.Select(id => new UserRoleRow { UserId = userId, RoleId = id, CreatedAtUtc = now }));
            Touch(user);
            var removedIds = removed.Select(static grant => grant.RoleId).ToList();
            var removedCodes = await context.Roles.AsNoTracking()
                .Where(row => removedIds.Contains(row.Id))
                .Select(static row => row.Code).ToListAsync(cancellationToken).ConfigureAwait(false);
            Audit(now, SecurityAuditEventType.UserRolesChanged, actor, user.Id, user.UserName, details: new
            {
                added = roles.Where(role => addedIds.Contains(role.Id)).Select(static role => role.Code).Order(StringComparer.Ordinal),
                removed = removedCodes.Order(StringComparer.Ordinal),
            });
            await SaveAsync(null, cancellationToken).ConfigureAwait(false);
            await EnsureAdministrableAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        return await ReloadUserAsync(userId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<UserDetails> SetUserScopesAsync(Guid userId, IReadOnlyCollection<CompanyScopeGrant> scopes,
        ConcurrencyToken version, AdministrativeActor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(actor);
        var desiredCompanies = new HashSet<Guid>();
        var desiredBranches = new HashSet<(Guid CompanyId, Guid BranchId)>();
        foreach (var grant in scopes)
        {
            if (grant is null || grant.CompanyId == Guid.Empty || !desiredCompanies.Add(grant.CompanyId))
            {
                throw new RequestValidationException("security.duplicate_company_scope",
                    "Each company may appear once and must be nonempty.", "scopes");
            }

            foreach (var branchId in grant.BranchIds ?? [])
            {
                if (branchId == Guid.Empty || !desiredBranches.Add((grant.CompanyId, branchId)))
                {
                    throw new RequestValidationException("security.duplicate_branch_scope",
                        "Each branch may appear once per company and must be nonempty.", "scopes");
                }
            }
        }

        var now = time.GetUtcNow();
        var transaction = await SecurityAdministrationLock.BeginAsync(context, cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            var user = await LoadUserForUpdateAsync(userId, version, cancellationToken).ConfigureAwait(false);
            var companyIds = desiredCompanies.ToList();
            var knownCompanies = await context.Companies.AsNoTracking().CountAsync(row => companyIds.Contains(row.Id),
                cancellationToken).ConfigureAwait(false);
            if (knownCompanies != desiredCompanies.Count)
            {
                throw new RequestValidationException("security.unknown_company", "An unknown company was supplied.", "scopes");
            }

            var requestedBranchIds = desiredBranches.Select(static pair => pair.BranchId).Distinct().ToList();
            var knownBranches = await context.Branches.AsNoTracking().Where(row => requestedBranchIds.Contains(row.Id))
                .Select(static row => new { row.Id, row.CompanyId }).ToListAsync(cancellationToken).ConfigureAwait(false);
            if (knownBranches.Count != requestedBranchIds.Count || desiredBranches.Any(pair =>
                    !knownBranches.Exists(branch => branch.Id == pair.BranchId && branch.CompanyId == pair.CompanyId)))
            {
                throw new RequestValidationException("security.branch_not_in_company",
                    "Every branch must exist and belong to the company it is granted under.", "scopes");
            }

            var existingCompanies = await context.UserCompanyScopes.Where(row => row.UserId == userId)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            var existingBranches = await context.UserBranchScopes.Where(row => row.UserId == userId)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            var removedBranches = existingBranches.Where(row => !desiredBranches.Contains((row.CompanyId, row.BranchId))).ToList();
            var removedCompanies = existingCompanies.Where(row => !desiredCompanies.Contains(row.CompanyId)).ToList();
            var addedCompanies = desiredCompanies.Where(id => existingCompanies.TrueForAll(row => row.CompanyId != id)).ToList();
            var addedBranches = desiredBranches.Where(pair => existingBranches.TrueForAll(row =>
                row.CompanyId != pair.CompanyId || row.BranchId != pair.BranchId)).ToList();

            context.UserBranchScopes.RemoveRange(removedBranches);
            context.UserCompanyScopes.RemoveRange(removedCompanies);
            context.UserCompanyScopes.AddRange(addedCompanies.Select(id =>
                new UserCompanyScopeRow { UserId = userId, CompanyId = id, CreatedAtUtc = now }));
            context.UserBranchScopes.AddRange(addedBranches.Select(pair =>
                new UserBranchScopeRow { UserId = userId, CompanyId = pair.CompanyId, BranchId = pair.BranchId, CreatedAtUtc = now }));
            Touch(user);
            Audit(now, SecurityAuditEventType.UserScopesChanged, actor, user.Id, user.UserName, details: new
            {
                addedCompanies,
                removedCompanies = removedCompanies.Select(static row => row.CompanyId),
                addedBranches = addedBranches.Select(static pair => pair.BranchId),
                removedBranches = removedBranches.Select(static row => row.BranchId),
            });
            await SaveAsync(null, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        return await ReloadUserAsync(userId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> RevokeUserSessionsAsync(Guid userId, string reason, AdministrativeActor actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var validatedReason = SecurityInput.RequireReason(reason, "reason");
        var now = time.GetUtcNow();
        var transaction = await SecurityAdministrationLock.BeginAsync(context, cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            var user = await context.Users.AsNoTracking().Where(row => row.Id == userId)
                .Select(static row => new { row.Id, row.UserName }).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new ResourceNotFoundException("user");
            var revoked = await RevokeAsync(userId, SessionRevocationReason.AdministratorRevoked, now, cancellationToken)
                .ConfigureAwait(false);
            Audit(now, SecurityAuditEventType.SessionsRevokedByAdministrator, actor, user.Id, user.UserName,
                reason: validatedReason, details: new { revokedSessions = revoked });
            await SaveAsync(null, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return revoked;
        }
    }

    public async Task<IReadOnlyList<RoleSummary>> ListRolesAsync(CancellationToken cancellationToken)
    {
        var roles = await context.Roles.AsNoTracking().OrderBy(static row => row.Code)
            .Select(row => new RoleSummary(row.Id, row.Code, row.NameArabic, row.NameEnglish,
                context.RolePermissions.Count(permission => permission.RoleId == row.Id),
                context.UserRoles.Count(grant => grant.RoleId == row.Id)))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return roles.AsReadOnly();
    }

    public async Task<RoleDetails> GetRoleAsync(Guid roleId, CancellationToken cancellationToken)
    {
        var role = await context.Roles.AsNoTracking().FirstOrDefaultAsync(row => row.Id == roleId, cancellationToken)
            .ConfigureAwait(false) ?? throw new ResourceNotFoundException("role");
        var permissions = await context.RolePermissions.AsNoTracking().Where(row => row.RoleId == roleId)
            .Select(static row => row.PermissionCode).ToListAsync(cancellationToken).ConfigureAwait(false);
        permissions.Sort(StringComparer.Ordinal);
        return new RoleDetails(role.Id, role.Code, role.NameArabic, role.NameEnglish, permissions.AsReadOnly(), role.CreatedAtUtc,
            role.UpdatedAtUtc, ConcurrencyToken.FromBytes(role.RowVersion));
    }

    public async Task<RoleDetails> CreateRoleAsync(CreateRoleCommand command, AdministrativeActor actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(actor);
        var code = SecurityInput.RequireRoleCode(command.Code, "code");
        var names = SecurityInput.RequireNames(command.NameArabic, command.NameEnglish, "nameArabic");
        var permissions = SecurityInput.RequirePermissionCodes(command.PermissionCodes, "permissionCodes");
        var now = time.GetUtcNow();
        var roleId = Guid.CreateVersion7(now);
        var transaction = await SecurityAdministrationLock.BeginAsync(context, cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            if (await context.Roles.AnyAsync(row => row.Code == code, cancellationToken).ConfigureAwait(false))
            {
                throw new ResourceConflictException("security.role_code_taken", "The role code is already in use.");
            }

            context.Roles.Add(new RoleRow { Id = roleId, Code = code, NameArabic = names.Arabic, NameEnglish = names.English });
            context.RolePermissions.AddRange(permissions.Select(permission =>
                new RolePermissionRow { RoleId = roleId, PermissionCode = permission, CreatedAtUtc = now }));
            Audit(now, SecurityAuditEventType.RoleCreated, actor, null, null, details: new { roleId, code, permissions });
            await SaveAsync("security.role_code_taken", cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        context.ChangeTracker.Clear();
        return await GetRoleAsync(roleId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<RoleDetails> UpdateRoleAsync(Guid roleId, UpdateRoleCommand command, AdministrativeActor actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(actor);
        var names = SecurityInput.RequireNames(command.NameArabic, command.NameEnglish, "nameArabic");
        var permissions = SecurityInput.RequirePermissionCodes(command.PermissionCodes, "permissionCodes");
        var now = time.GetUtcNow();
        var transaction = await SecurityAdministrationLock.BeginAsync(context, cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            var role = await context.Roles.FirstOrDefaultAsync(row => row.Id == roleId, cancellationToken).ConfigureAwait(false)
                ?? throw new ResourceNotFoundException("role");
            if (!command.Version.Matches(role.RowVersion))
            {
                throw new ConcurrencyConflictException("role");
            }

            context.Entry(role).Property(static row => row.RowVersion).OriginalValue = command.Version.ToBytes();
            var existing = await context.RolePermissions.Where(row => row.RoleId == roleId).ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var removed = existing.Where(row => !permissions.Contains(row.PermissionCode, StringComparer.Ordinal)).ToList();
            var added = permissions.Where(code => existing.TrueForAll(row =>
                !string.Equals(row.PermissionCode, code, StringComparison.Ordinal))).ToList();
            context.RolePermissions.RemoveRange(removed);
            context.RolePermissions.AddRange(added.Select(code =>
                new RolePermissionRow { RoleId = roleId, PermissionCode = code, CreatedAtUtc = now }));
            role.NameArabic = names.Arabic;
            role.NameEnglish = names.English;
            context.Entry(role).Property(static row => row.UpdatedAtUtc).IsModified = true;
            Audit(now, SecurityAuditEventType.RoleUpdated, actor, null, null, details: new
            {
                roleId,
                role.Code,
                added,
                removed = removed.Select(static row => row.PermissionCode),
            });
            await SaveAsync(null, cancellationToken).ConfigureAwait(false);
            await EnsureAdministrableAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        context.ChangeTracker.Clear();
        return await GetRoleAsync(roleId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OrganizationCompany>> ListOrganizationAsync(CancellationToken cancellationToken)
    {
        var companies = await context.Companies.AsNoTracking().OrderBy(static row => row.Code)
            .Select(static row => new { row.Id, row.Code, row.NameArabic, row.NameEnglish, row.IsActive })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var branches = await context.Branches.AsNoTracking().OrderBy(static row => row.Code)
            .Select(static row => new { row.Id, row.CompanyId, row.Code, row.NameArabic, row.NameEnglish, row.IsActive })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return companies.Select(company => new OrganizationCompany(company.Id, company.Code, company.NameArabic,
                company.NameEnglish, company.IsActive,
                branches.Where(branch => branch.CompanyId == company.Id)
                    .Select(static branch => new OrganizationBranch(branch.Id, branch.Code, branch.NameArabic, branch.NameEnglish,
                        branch.IsActive))
                    .ToArray()))
            .ToArray();
    }

    private async Task<UserRow> LoadUserForUpdateAsync(Guid userId, ConcurrencyToken version, CancellationToken cancellationToken)
    {
        var user = await context.Users.FirstOrDefaultAsync(row => row.Id == userId, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("user");
        if (!version.Matches(user.RowVersion))
        {
            throw new ConcurrencyConflictException("user");
        }

        // Also enforced by the UPDATE predicate, so a change between this read and the write is rejected.
        context.Entry(user).Property(static row => row.RowVersion).OriginalValue = version.ToBytes();
        return user;
    }

    private void Touch(UserRow user) => context.Entry(user).Property(static row => row.UpdatedAtUtc).IsModified = true;

    private void Audit(DateTimeOffset now, SecurityAuditEventType eventType, AdministrativeActor actor, Guid? userId,
        string? userName, string? reason = null, object? details = null) =>
        context.SecurityAuditEvents.Add(SecurityAuditEntries.Create(now, eventType, actor.Client, userId: userId,
            userName: userName, actorUserId: actor.UserId, reason: reason, details: details));

    private async Task<int> RevokeAsync(Guid userId, SessionRevocationReason reason, DateTimeOffset now,
        CancellationToken cancellationToken) =>
        await context.Sessions.Where(row => row.UserId == userId && row.RevokedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(static row => row.RevokedAtUtc, now)
                .SetProperty(static row => row.RevocationReason, (byte?)reason), cancellationToken)
            .ConfigureAwait(false);

    private async Task SaveAsync(string? uniqueViolationCode, CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConcurrencyConflictException("The resource was changed by another operation.", exception);
        }
        catch (DbUpdateException exception) when (uniqueViolationCode is not null
            && exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new ResourceConflictException(uniqueViolationCode, "A unique value is already in use.");
        }
    }

    private async Task EnsureAdministrableAsync(CancellationToken cancellationToken)
    {
        var usersManage = PermissionCatalog.UsersManage;
        var rolesManage = PermissionCatalog.RolesManage;
        var administrable = await context.Users.AnyAsync(user => user.IsActive
            && context.UserRoles.Any(grant => grant.UserId == user.Id && context.RolePermissions.Any(permission =>
                permission.RoleId == grant.RoleId && permission.PermissionCode == usersManage))
            && context.UserRoles.Any(grant => grant.UserId == user.Id && context.RolePermissions.Any(permission =>
                permission.RoleId == grant.RoleId && permission.PermissionCode == rolesManage)), cancellationToken)
            .ConfigureAwait(false);
        if (!administrable)
        {
            throw new BusinessRuleException("security.last_administrator",
                "The change would leave no active user able to manage users and roles.");
        }
    }

    private async Task<UserDetails> ReloadUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        return await GetUserAsync(userId, cancellationToken).ConfigureAwait(false);
    }
}
