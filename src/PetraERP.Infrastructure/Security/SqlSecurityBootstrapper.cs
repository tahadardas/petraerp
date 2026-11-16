using System.Data;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Security;
using PetraERP.Domain.FinancialValues;
using PetraERP.Domain.Primitives;
using PetraERP.Infrastructure.Currencies;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Security;

/// <summary>
/// One-time first-administrator installation, run only from the server CLI. It succeeds only while
/// no user and no bootstrap marker exist; the singleton marker row (primary key Id = 1) makes a second
/// bootstrap impossible even under a race. No default or shared password exists anywhere.
/// </summary>
public sealed class SqlSecurityBootstrapper : ISecurityBootstrapper
{
    public const string AdministratorRoleCode = "ADMINISTRATOR";

    private readonly CentralDataContext context;
    private readonly IPasswordHasher hasher;
    private readonly TimeProvider time;

    public SqlSecurityBootstrapper(CentralDataContext context, IPasswordHasher hasher, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(hasher);
        ArgumentNullException.ThrowIfNull(time);
        this.context = context;
        this.hasher = hasher;
        this.time = time;
    }

    public async Task<bool> IsCompletedAsync(CancellationToken cancellationToken) =>
        await context.BootstrapState.AnyAsync(cancellationToken).ConfigureAwait(false)
        || await context.Users.AnyAsync(cancellationToken).ConfigureAwait(false);

    public async Task<BootstrapResult> BootstrapAsync(BootstrapAdministratorCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var (userName, normalized) = SecurityInput.RequireLoginName(command.UserName, "userName");
        var names = SecurityInput.RequireNames(command.DisplayNameArabic, command.DisplayNameEnglish, "displayNameArabic");
        SecurityInput.RequirePassword(command.Password, userName, "password");
        var organization = command.Organization is null ? null : ValidateOrganization(command.Organization);
        var hash = hasher.Hash(command.Password);
        var now = time.GetUtcNow();
        var userId = Guid.CreateVersion7(now);
        var roleId = Guid.CreateVersion7(now);

        var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            .ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            if (await context.BootstrapState.AnyAsync(cancellationToken).ConfigureAwait(false)
                || await context.Users.AnyAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new BusinessRuleException("security.bootstrap_completed",
                    "The administrator bootstrap has already been completed for this installation.");
            }

            if (organization is not null)
            {
                await CreateOrganizationAsync(organization, now, cancellationToken).ConfigureAwait(false);
            }
            else if (!await context.Companies.AnyAsync(static row => row.IsActive, cancellationToken).ConfigureAwait(false))
            {
                throw new BusinessRuleException("security.bootstrap_requires_organization",
                    "No active company exists. Supply the first company and branch to the bootstrap command.");
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
                MustChangePassword = false,
                IsActive = true,
                SecurityStamp = Guid.NewGuid(),
            });
            context.Roles.Add(new RoleRow
            {
                Id = roleId,
                Code = AdministratorRoleCode,
                NameArabic = "مدير النظام",
                NameEnglish = "System administrator",
            });
            context.RolePermissions.AddRange(PermissionCatalog.All.Select(permission =>
                new RolePermissionRow { RoleId = roleId, PermissionCode = permission.Code, CreatedAtUtc = now }));
            context.UserRoles.Add(new UserRoleRow { UserId = userId, RoleId = roleId, CreatedAtUtc = now });
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var companies = await context.Companies.AsNoTracking().Where(static row => row.IsActive)
                .Select(static row => row.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
            var branches = await context.Branches.AsNoTracking().Where(static row => row.IsActive)
                .Select(static row => new { row.Id, row.CompanyId }).ToListAsync(cancellationToken).ConfigureAwait(false);
            var scopedBranches = branches.Where(branch => companies.Contains(branch.CompanyId)).ToList();
            context.UserCompanyScopes.AddRange(companies.Select(id =>
                new UserCompanyScopeRow { UserId = userId, CompanyId = id, CreatedAtUtc = now }));
            context.UserBranchScopes.AddRange(scopedBranches.Select(branch =>
                new UserBranchScopeRow { UserId = userId, CompanyId = branch.CompanyId, BranchId = branch.Id, CreatedAtUtc = now }));
            context.BootstrapState.Add(new BootstrapStateRow { Id = 1, AdministratorUserId = userId, CompletedAtUtc = now });
            context.SecurityAuditEvents.Add(SecurityAuditEntries.Create(now, SecurityAuditEventType.BootstrapCompleted,
                ClientContext.Create(null, "PetraERP.Api bootstrap-admin", null), userId: userId, userName: userName,
                actorUserId: userId, details: new
                {
                    role = AdministratorRoleCode,
                    companies = companies.Count,
                    branches = scopedBranches.Count,
                    createdCompany = organization?.CompanyCode,
                }));
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new BootstrapResult(userId, roleId, companies.Count, scopedBranches.Count);
        }
    }

    private async Task CreateOrganizationAsync(ValidatedOrganization organization, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (await context.Companies.AnyAsync(row => row.Code == organization.CompanyCode, cancellationToken).ConfigureAwait(false))
        {
            throw new ResourceConflictException("organization.company_code_taken", "A company with this code already exists.");
        }

        var currency = await context.Currencies.FirstOrDefaultAsync(row => row.Code == organization.CurrencyCode, cancellationToken)
            .ConfigureAwait(false);
        if (currency is null)
        {
            context.Currencies.Add(new CurrencyRow
            {
                Code = organization.CurrencyCode,
                NameArabic = organization.CurrencyNameArabic,
                IsActive = true,
            });
        }
        else if (!currency.IsActive)
        {
            throw new BusinessRuleException("organization.currency_inactive", "The base currency exists but is inactive.");
        }

        var companyId = Guid.CreateVersion7(now);
        context.Companies.Add(new CompanyRow
        {
            Id = companyId,
            Code = organization.CompanyCode,
            NameArabic = organization.CompanyName.Arabic,
            NameEnglish = organization.CompanyName.English,
            BaseCurrencyCode = organization.CurrencyCode,
            IsActive = true,
        });
        context.Branches.Add(new BranchRow
        {
            Id = Guid.CreateVersion7(now),
            CompanyId = companyId,
            Code = organization.BranchCode,
            NameArabic = organization.BranchName.Arabic,
            NameEnglish = organization.BranchName.English,
            IsActive = true,
        });
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static ValidatedOrganization ValidateOrganization(BootstrapOrganization organization)
    {
        try
        {
            return new ValidatedOrganization(
                EntityCode.Create(organization.CompanyCode).Value,
                BilingualName.Create(organization.CompanyNameArabic, organization.CompanyNameEnglish),
                CurrencyCode.Create(organization.BaseCurrencyCode).Value,
                BilingualName.Create(organization.BaseCurrencyNameArabic).Arabic,
                EntityCode.Create(organization.BranchCode).Value,
                BilingualName.Create(organization.BranchNameArabic, organization.BranchNameEnglish));
        }
        catch (ArgumentException exception)
        {
            throw new RequestValidationException("organization.invalid",
                "Company, currency and branch codes and Arabic names are required and must be valid.", exception.ParamName);
        }
    }

    private sealed record ValidatedOrganization(string CompanyCode, BilingualName CompanyName, string CurrencyCode,
        string CurrencyNameArabic, string BranchCode, BilingualName BranchName);
}
