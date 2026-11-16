using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Security;
using PetraERP.Infrastructure.Security;

namespace PetraERP.Infrastructure.Tests.Security;

/// <summary>Explicit SQL Server tests for authentication storage, rotation, revocation, scopes and audit immutability.</summary>
public sealed class SecuritySqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task BootstrapRunsOnceAndGrantsEveryPermissionAndScope()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            using var context = database.Context();
            var result = await SecuritySqlFixture.BootstrapAsync(context, Cancellation).ConfigureAwait(true);
            Assert.Equal(1, result.CompanyScopeCount);
            Assert.Equal(1, result.BranchScopeCount);

            var queries = new SqlUserContextQueries(context);
            Assert.Single(await queries.GetCompaniesAsync(result.AdministratorUserId, Cancellation).ConfigureAwait(true));
            var permissions = await context.RolePermissions.CountAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(PermissionCatalog.All.Count, permissions);

            var again = new SqlSecurityBootstrapper(context, SecuritySqlFixture.Hasher, TimeProvider.System);
            Assert.True(await again.IsCompletedAsync(Cancellation).ConfigureAwait(true));
            var rejected = await Assert.ThrowsAsync<BusinessRuleException>(() => again.BootstrapAsync(
                new BootstrapAdministratorCommand("second.admin", "مدير ثان", null, "Another-Pass-2026", null), Cancellation))
                .ConfigureAwait(true);
            Assert.Equal("security.bootstrap_completed", rejected.Code);
            Assert.Equal(1, await context.Users.CountAsync(Cancellation).ConfigureAwait(true));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task LoginSucceedsStoresOnlyHashesAndRejectsWrongPasswordsUntilLockout()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            using var context = database.Context();
            await SecuritySqlFixture.BootstrapAsync(context, Cancellation).ConfigureAwait(true);
            var authentication = SecuritySqlFixture.Authentication(context);

            var success = await authentication.LoginAsync(
                SecuritySqlFixture.Login(" ADMIN ", SecuritySqlFixture.AdminPassword), SecuritySqlFixture.Client, Cancellation)
                .ConfigureAwait(true);
            Assert.True(success.Succeeded);
            var tokens = success.Tokens!;
            Assert.Equal(43, tokens.RefreshToken.Length);
            Assert.False(tokens.MustChangePassword);

            // The raw refresh token and password never reach storage.
            var storedHashes = await context.RefreshTokens.AsNoTracking().Select(static row => row.TokenHash)
                .ToListAsync(Cancellation).ConfigureAwait(true);
            Assert.Contains(storedHashes, hash => hash.AsSpan().SequenceEqual(RefreshTokenCodec.TryHash(tokens.RefreshToken)));
            var rawTokenRows = await context.Database.SqlQuery<int>(
                    $"SELECT COUNT(*) AS [Value] FROM [security].[RefreshTokens] WHERE CAST([TokenHash] AS varbinary(64)) = CAST({tokens.RefreshToken} AS varbinary(64))")
                .FirstAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(0, rawTokenRows);
            var auditText = string.Join('|', await context.SecurityAuditEvents.AsNoTracking()
                .Select(static row => (row.Details ?? string.Empty) + (row.Reason ?? string.Empty))
                .ToListAsync(Cancellation).ConfigureAwait(true));
            Assert.DoesNotContain(SecuritySqlFixture.AdminPassword, auditText, StringComparison.Ordinal);
            Assert.DoesNotContain(tokens.RefreshToken, auditText, StringComparison.Ordinal);

            for (var attempt = 1; attempt < AuthenticationSettings.Default.MaxFailedAccessAttempts; attempt++)
            {
                var wrong = await authentication.LoginAsync(SecuritySqlFixture.Login("admin", "wrong-password-123"),
                    SecuritySqlFixture.Client, Cancellation).ConfigureAwait(true);
                Assert.Equal(AuthenticationFailure.InvalidCredentials, wrong.Failure);
            }

            var locking = await authentication.LoginAsync(SecuritySqlFixture.Login("admin", "wrong-password-123"),
                SecuritySqlFixture.Client, Cancellation).ConfigureAwait(true);
            Assert.Equal(AuthenticationFailure.LockedOut, locking.Failure);
            Assert.NotNull(locking.LockoutEndUtc);

            // While locked even the correct password is not evaluated.
            var locked = await authentication.LoginAsync(SecuritySqlFixture.Login("admin", SecuritySqlFixture.AdminPassword),
                SecuritySqlFixture.Client, Cancellation).ConfigureAwait(true);
            Assert.Equal(AuthenticationFailure.LockedOut, locked.Failure);

            var unknown = await authentication.LoginAsync(SecuritySqlFixture.Login("nobody", "whatever-password"),
                SecuritySqlFixture.Client, Cancellation).ConfigureAwait(true);
            Assert.Equal(AuthenticationFailure.InvalidCredentials, unknown.Failure);

            var lockouts = await context.SecurityAuditEvents.CountAsync(row => row.EventType == (byte)SecurityAuditEventType.AccountLockedOut,
                Cancellation).ConfigureAwait(true);
            Assert.Equal(1, lockouts);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task RefreshRotatesAndReuseOfAnOldTokenRevokesTheWholeSession()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            using var context = database.Context();
            await SecuritySqlFixture.BootstrapAsync(context, Cancellation).ConfigureAwait(true);
            var authentication = SecuritySqlFixture.Authentication(context);
            var validator = new SqlSessionValidator(context, TimeProvider.System);
            var login = (await authentication.LoginAsync(SecuritySqlFixture.Login("admin", SecuritySqlFixture.AdminPassword),
                SecuritySqlFixture.Client, Cancellation).ConfigureAwait(true)).Tokens!;

            var first = await authentication.RefreshAsync(login.RefreshToken, SecuritySqlFixture.Client, Cancellation).ConfigureAwait(true);
            Assert.True(first.Succeeded);
            Assert.Equal(login.SessionId, first.Tokens!.SessionId);
            Assert.NotEqual(login.RefreshToken, first.Tokens.RefreshToken);

            var second = await authentication.RefreshAsync(first.Tokens.RefreshToken, SecuritySqlFixture.Client, Cancellation)
                .ConfigureAwait(true);
            Assert.True(second.Succeeded);
            Assert.NotNull(await validator.ValidateAsync(login.UserId, login.SessionId, Cancellation).ConfigureAwait(true));

            // Presenting an already rotated token is treated as theft: the family is revoked.
            var reuse = await authentication.RefreshAsync(login.RefreshToken, SecuritySqlFixture.Client, Cancellation).ConfigureAwait(true);
            Assert.Equal(AuthenticationFailure.RefreshTokenReused, reuse.Failure);
            Assert.Null(await validator.ValidateAsync(login.UserId, login.SessionId, Cancellation).ConfigureAwait(true));
            var afterRevocation = await authentication.RefreshAsync(second.Tokens!.RefreshToken, SecuritySqlFixture.Client, Cancellation)
                .ConfigureAwait(true);
            Assert.Equal(AuthenticationFailure.SessionExpired, afterRevocation.Failure);

            var malformed = await authentication.RefreshAsync("not-a-token", SecuritySqlFixture.Client, Cancellation).ConfigureAwait(true);
            Assert.Equal(AuthenticationFailure.InvalidRefreshToken, malformed.Failure);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ConcurrentRefreshWithOneTokenHasExactlyOneWinner()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            using (var setup = database.Context())
            {
                await SecuritySqlFixture.BootstrapAsync(setup, Cancellation).ConfigureAwait(true);
            }

            string refreshToken;
            using (var loginContext = database.Context())
            {
                refreshToken = (await SecuritySqlFixture.Authentication(loginContext).LoginAsync(
                    SecuritySqlFixture.Login("admin", SecuritySqlFixture.AdminPassword), SecuritySqlFixture.Client, Cancellation)
                    .ConfigureAwait(true)).Tokens!.RefreshToken;
            }

            var attempts = Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
            {
                using var context = database.Context();
                return await SecuritySqlFixture.Authentication(context).RefreshAsync(refreshToken, SecuritySqlFixture.Client, Cancellation)
                    .ConfigureAwait(false);
            }, Cancellation)).ToArray();
            var outcomes = await Task.WhenAll(attempts).ConfigureAwait(true);
            Assert.True(outcomes.Count(static outcome => outcome.Succeeded) <= 1);
            Assert.Contains(outcomes, static outcome => outcome.Failure == AuthenticationFailure.RefreshTokenReused);

            using var verify = database.Context();
            var consumed = await verify.RefreshTokens.CountAsync(static row => row.ConsumedAtUtc != null, Cancellation).ConfigureAwait(true);
            Assert.Equal(1, consumed);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task LogoutAndLogoutAllRevokeSessionsImmediately()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            using var context = database.Context();
            await SecuritySqlFixture.BootstrapAsync(context, Cancellation).ConfigureAwait(true);
            var authentication = SecuritySqlFixture.Authentication(context);
            var validator = new SqlSessionValidator(context, TimeProvider.System);
            var one = (await authentication.LoginAsync(SecuritySqlFixture.Login("admin", SecuritySqlFixture.AdminPassword),
                SecuritySqlFixture.Client, Cancellation).ConfigureAwait(true)).Tokens!;
            var two = (await authentication.LoginAsync(SecuritySqlFixture.Login("admin", SecuritySqlFixture.AdminPassword),
                SecuritySqlFixture.Client, Cancellation).ConfigureAwait(true)).Tokens!;
            var three = (await authentication.LoginAsync(SecuritySqlFixture.Login("admin", SecuritySqlFixture.AdminPassword),
                SecuritySqlFixture.Client, Cancellation).ConfigureAwait(true)).Tokens!;

            await authentication.LogoutAsync(one.UserId, one.SessionId, SecuritySqlFixture.Client, Cancellation).ConfigureAwait(true);
            Assert.Null(await validator.ValidateAsync(one.UserId, one.SessionId, Cancellation).ConfigureAwait(true));
            Assert.NotNull(await validator.ValidateAsync(two.UserId, two.SessionId, Cancellation).ConfigureAwait(true));
            Assert.Equal(AuthenticationFailure.SessionExpired,
                (await authentication.RefreshAsync(one.RefreshToken, SecuritySqlFixture.Client, Cancellation).ConfigureAwait(true)).Failure);

            var revoked = await authentication.LogoutAllAsync(two.UserId, two.SessionId, SecuritySqlFixture.Client, Cancellation)
                .ConfigureAwait(true);
            Assert.Equal(2, revoked);
            Assert.Null(await validator.ValidateAsync(two.UserId, two.SessionId, Cancellation).ConfigureAwait(true));
            Assert.Null(await validator.ValidateAsync(three.UserId, three.SessionId, Cancellation).ConfigureAwait(true));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task LogoutAtomicityRollsBackSessionRevocationWhenAuditSaveFails()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            using var context = database.Context();
            await SecuritySqlFixture.BootstrapAsync(context, Cancellation).ConfigureAwait(true);
            var authentication = SecuritySqlFixture.Authentication(context);
            var validator = new SqlSessionValidator(context, TimeProvider.System);

            var login = (await authentication.LoginAsync(SecuritySqlFixture.Login("admin", SecuritySqlFixture.AdminPassword),
                SecuritySqlFixture.Client, Cancellation).ConfigureAwait(true)).Tokens!;

            // Verify session is active
            Assert.NotNull(await validator.ValidateAsync(login.UserId, login.SessionId, Cancellation).ConfigureAwait(true));

            // Inject fault on SecurityAuditEvents insertion via temporary trigger
            await context.Database.ExecuteSqlRawAsync(@"
                CREATE TRIGGER [security].[TR_SecurityAuditEvents_FaultInjection]
                ON [security].[SecurityAuditEvents]
                AFTER INSERT AS
                BEGIN
                    THROW 51099, 'Simulated audit insertion failure for atomicity testing', 1;
                END;", Cancellation).ConfigureAwait(true);

            // Attempt logout: should throw due to audit failure and roll back session revocation
            var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
                authentication.LogoutAsync(login.UserId, login.SessionId, SecuritySqlFixture.Client, Cancellation))
                .ConfigureAwait(true);
            Assert.Contains("51099", ex.ToString());

            // Clear change tracker to inspect database directly
            context.ChangeTracker.Clear();

            // Verify session is STILL active in database because transaction rolled back
            var sessionRow = await context.Sessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == login.SessionId, Cancellation).ConfigureAwait(true);
            Assert.NotNull(sessionRow);
            Assert.Null(sessionRow.RevokedAtUtc);
            Assert.NotNull(await validator.ValidateAsync(login.UserId, login.SessionId, Cancellation).ConfigureAwait(true));

            // Drop fault injection trigger
            await context.Database.ExecuteSqlRawAsync("DROP TRIGGER [security].[TR_SecurityAuditEvents_FaultInjection];", Cancellation).ConfigureAwait(true);

            // Now logout succeeds cleanly
            await authentication.LogoutAsync(login.UserId, login.SessionId, SecuritySqlFixture.Client, Cancellation).ConfigureAwait(true);

            // Verify session is revoked and audit event is recorded
            context.ChangeTracker.Clear();
            var revokedSession = await context.Sessions.AsNoTracking().FirstAsync(s => s.Id == login.SessionId, Cancellation).ConfigureAwait(true);
            Assert.NotNull(revokedSession.RevokedAtUtc);
            Assert.Null(await validator.ValidateAsync(login.UserId, login.SessionId, Cancellation).ConfigureAwait(true));

            var auditRow = await context.SecurityAuditEvents.AsNoTracking()
                .FirstOrDefaultAsync(a => a.SessionId == login.SessionId && a.EventType == (byte)SecurityAuditEventType.Logout, Cancellation)
                .ConfigureAwait(true);
            Assert.NotNull(auditRow);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task DisabledUsersCannotSignInAndAdminChangesNeedTheCurrentVersion()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            using var context = database.Context();
            var bootstrap = await SecuritySqlFixture.BootstrapAsync(context, Cancellation).ConfigureAwait(true);
            var administration = SecuritySqlFixture.Administration(context);
            var actor = new AdministrativeActor(bootstrap.AdministratorUserId, SecuritySqlFixture.Client);
            var created = await administration.CreateUserAsync(
                new CreateUserCommand("accountant", "محاسب أول", "Senior accountant", "Temp-Password-2026"), actor, Cancellation)
                .ConfigureAwait(true);
            Assert.True(created.MustChangePassword);

            var authentication = SecuritySqlFixture.Authentication(context);
            var first = await authentication.LoginAsync(SecuritySqlFixture.Login("accountant", "Temp-Password-2026"),
                SecuritySqlFixture.Client, Cancellation).ConfigureAwait(true);
            Assert.True(first.Tokens!.MustChangePassword);

            // Sign-in updated the row (last login), so the creation-time version is now stale.
            var stale = created.Version;
            var current = await administration.GetUserAsync(created.Id, Cancellation).ConfigureAwait(true);
            Assert.NotEqual(stale, current.Version);
            var disabled = await administration.SetUserActiveAsync(created.Id, false, "انتهاء العقد", current.Version, actor,
                Cancellation).ConfigureAwait(true);
            Assert.False(disabled.IsActive);
            Assert.Null(await new SqlSessionValidator(context, TimeProvider.System)
                .ValidateAsync(created.Id, first.Tokens.SessionId, Cancellation).ConfigureAwait(true));

            var rejected = await authentication.LoginAsync(SecuritySqlFixture.Login("accountant", "Temp-Password-2026"),
                SecuritySqlFixture.Client, Cancellation).ConfigureAwait(true);
            Assert.Equal(AuthenticationFailure.AccountDisabled, rejected.Failure);
            var wrong = await authentication.LoginAsync(SecuritySqlFixture.Login("accountant", "Wrong-Password-2026"),
                SecuritySqlFixture.Client, Cancellation).ConfigureAwait(true);
            Assert.Equal(AuthenticationFailure.InvalidCredentials, wrong.Failure);

            // A stale version cannot overwrite the newer state.
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => administration.UpdateUserProfileAsync(created.Id,
                new UpdateUserProfileCommand("اسم جديد", null, stale), actor, Cancellation)).ConfigureAwait(true);
            var unchanged = await administration.GetUserAsync(created.Id, Cancellation).ConfigureAwait(true);
            Assert.Equal("محاسب أول", unchanged.DisplayNameArabic);
            Assert.False(unchanged.IsActive);

            var adminVersion = (await administration.GetUserAsync(bootstrap.AdministratorUserId, Cancellation).ConfigureAwait(true)).Version;
            var selfDisable = await Assert.ThrowsAsync<BusinessRuleException>(() => administration.SetUserActiveAsync(
                bootstrap.AdministratorUserId, false, "test", adminVersion, actor, Cancellation)).ConfigureAwait(true);
            Assert.Equal("security.cannot_disable_self", selfDisable.Code);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task TheLastAdministratorCannotBeRemoved()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            using var context = database.Context();
            var bootstrap = await SecuritySqlFixture.BootstrapAsync(context, Cancellation).ConfigureAwait(true);
            var administration = SecuritySqlFixture.Administration(context);
            var actor = new AdministrativeActor(bootstrap.AdministratorUserId, SecuritySqlFixture.Client);
            var admin = await administration.GetUserAsync(bootstrap.AdministratorUserId, Cancellation).ConfigureAwait(true);

            var removal = await Assert.ThrowsAsync<BusinessRuleException>(() => administration.SetUserRolesAsync(admin.Id, [],
                admin.Version, actor, Cancellation)).ConfigureAwait(true);
            Assert.Equal("security.last_administrator", removal.Code);
            var unchanged = await administration.GetUserAsync(admin.Id, Cancellation).ConfigureAwait(true);
            Assert.Single(unchanged.RoleIds);

            var role = await administration.GetRoleAsync(bootstrap.AdministratorRoleId, Cancellation).ConfigureAwait(true);
            var stripped = await Assert.ThrowsAsync<BusinessRuleException>(() => administration.UpdateRoleAsync(role.Id,
                new UpdateRoleCommand(role.NameArabic, role.NameEnglish, [PermissionCatalog.UsersView], role.Version), actor,
                Cancellation)).ConfigureAwait(true);
            Assert.Equal("security.last_administrator", stripped.Code);
            Assert.Equal(PermissionCatalog.All.Count,
                (await administration.GetRoleAsync(role.Id, Cancellation).ConfigureAwait(true)).PermissionCodes.Count);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ScopesAreEnforcedForCompanyAndBranchAndCrossCompanyGrantsAreRejected()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            using var context = database.Context();
            var bootstrap = await SecuritySqlFixture.BootstrapAsync(context, Cancellation).ConfigureAwait(true);
            var petra = await context.Companies.AsNoTracking().SingleAsync(Cancellation).ConfigureAwait(true);
            var petraBranch = await context.Branches.AsNoTracking().SingleAsync(Cancellation).ConfigureAwait(true);
            var otherCompany = Guid.CreateVersion7();
            var otherBranch = Guid.CreateVersion7();
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [system].[Companies]([Id],[Code],[NameArabic],[BaseCurrencyCode],[IsActive]) VALUES({otherCompany},N'OTHER',N'شركة أخرى',N'JOD',1)",
                Cancellation).ConfigureAwait(true);
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [system].[Branches]([Id],[CompanyId],[Code],[NameArabic],[IsActive]) VALUES({otherBranch},{otherCompany},N'B2',N'فرع آخر',1)",
                Cancellation).ConfigureAwait(true);

            var evaluator = new SqlAccessEvaluator(context);
            var post = PermissionCatalog.Get(PermissionCatalog.JournalsPost);
            var adminId = bootstrap.AdministratorUserId;
            Assert.Equal(AccessDecision.Granted,
                await evaluator.EvaluatePermissionAsync(adminId, post, petra.Id, petraBranch.Id, Cancellation).ConfigureAwait(true));
            Assert.Equal(AccessDecision.CompanyScopeDenied,
                await evaluator.EvaluatePermissionAsync(adminId, post, otherCompany, otherBranch, Cancellation).ConfigureAwait(true));
            Assert.Equal(AccessDecision.BranchScopeDenied,
                await evaluator.EvaluatePermissionAsync(adminId, post, petra.Id, otherBranch, Cancellation).ConfigureAwait(true));
            Assert.Equal(AccessDecision.CompanyScopeDenied,
                await evaluator.EvaluatePermissionAsync(adminId, post, null, null, Cancellation).ConfigureAwait(true));
            Assert.Equal(AccessDecision.CompanyScopeDenied,
                await evaluator.EvaluateCompanyScopeAsync(adminId, otherCompany, Cancellation).ConfigureAwait(true));

            var administration = SecuritySqlFixture.Administration(context);
            var actor = new AdministrativeActor(adminId, SecuritySqlFixture.Client);
            var clerk = await administration.CreateUserAsync(new CreateUserCommand("clerk", "أمين صندوق", null, "Counter-Password-2026"),
                actor, Cancellation).ConfigureAwait(true);
            Assert.Equal(AccessDecision.PermissionDenied,
                await evaluator.EvaluatePermissionAsync(clerk.Id, post, petra.Id, petraBranch.Id, Cancellation).ConfigureAwait(true));

            // A branch of another company cannot be granted under this company (service and database both refuse).
            var crossGrant = await Assert.ThrowsAsync<RequestValidationException>(() => administration.SetUserScopesAsync(clerk.Id,
                [new CompanyScopeGrant(petra.Id, [otherBranch])], clerk.Version, actor, Cancellation)).ConfigureAwait(true);
            Assert.Equal("security.branch_not_in_company", crossGrant.Code);
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [security].[UserCompanyScopes]([UserId],[CompanyId]) VALUES({clerk.Id},{petra.Id})",
                Cancellation).ConfigureAwait(true);
            var foreignKey = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [security].[UserBranchScopes]([UserId],[CompanyId],[BranchId]) VALUES({clerk.Id},{petra.Id},{otherBranch})",
                Cancellation)).ConfigureAwait(true);
            Assert.Equal(547, foreignKey.Number);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task SecurityAuditRowsCannotBeUpdatedOrDeleted()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            using var context = database.Context();
            await SecuritySqlFixture.BootstrapAsync(context, Cancellation).ConfigureAwait(true);
            var count = await context.SecurityAuditEvents.CountAsync(Cancellation).ConfigureAwait(true);
            Assert.True(count > 0);

            var update = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlRawAsync(
                "UPDATE [security].[SecurityAuditEvents] SET [Reason] = N'tampered';", Cancellation)).ConfigureAwait(true);
            Assert.Equal(51001, update.Number);
            var delete = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlRawAsync(
                "DELETE FROM [security].[SecurityAuditEvents];", Cancellation)).ConfigureAwait(true);
            Assert.Equal(51001, delete.Number);
            Assert.Equal(count, await context.SecurityAuditEvents.CountAsync(Cancellation).ConfigureAwait(true));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ChangingThePasswordRevokesOldSessionsAndClearsTheChangeRequirement()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            using var context = database.Context();
            var bootstrap = await SecuritySqlFixture.BootstrapAsync(context, Cancellation).ConfigureAwait(true);
            var administration = SecuritySqlFixture.Administration(context);
            var actor = new AdministrativeActor(bootstrap.AdministratorUserId, SecuritySqlFixture.Client);
            var user = await administration.CreateUserAsync(new CreateUserCommand("auditor", "مدقق", null, "Initial-Password-2026"),
                actor, Cancellation).ConfigureAwait(true);
            var authentication = SecuritySqlFixture.Authentication(context);
            var login = (await authentication.LoginAsync(SecuritySqlFixture.Login("auditor", "Initial-Password-2026"),
                SecuritySqlFixture.Client, Cancellation).ConfigureAwait(true)).Tokens!;
            Assert.True(login.MustChangePassword);

            var reuse = await authentication.ChangePasswordAsync(new ChangePasswordCommand(user.Id, login.SessionId,
                "Initial-Password-2026", "Initial-Password-2026"), ClientKind.Desktop, SecuritySqlFixture.Client, Cancellation)
                .ConfigureAwait(true);
            Assert.Equal(PasswordPolicyViolation.ReusesCurrentPassword, reuse.PolicyViolation);
            var wrongCurrent = await authentication.ChangePasswordAsync(new ChangePasswordCommand(user.Id, login.SessionId,
                "not-the-password", "A-New-Password-2026"), ClientKind.Desktop, SecuritySqlFixture.Client, Cancellation)
                .ConfigureAwait(true);
            Assert.Equal(AuthenticationFailure.InvalidCurrentPassword, wrongCurrent.Failure);

            var changed = await authentication.ChangePasswordAsync(new ChangePasswordCommand(user.Id, login.SessionId,
                "Initial-Password-2026", "A-New-Password-2026"), ClientKind.Desktop, SecuritySqlFixture.Client, Cancellation)
                .ConfigureAwait(true);
            Assert.True(changed.Succeeded);
            Assert.False(changed.Tokens!.MustChangePassword);
            var validator = new SqlSessionValidator(context, TimeProvider.System);
            Assert.Null(await validator.ValidateAsync(user.Id, login.SessionId, Cancellation).ConfigureAwait(true));
            var fresh = await validator.ValidateAsync(user.Id, changed.Tokens.SessionId, Cancellation).ConfigureAwait(true);
            Assert.NotNull(fresh);
            Assert.False(fresh.MustChangePassword);
            Assert.Equal(AuthenticationFailure.InvalidCredentials, (await authentication.LoginAsync(
                SecuritySqlFixture.Login("auditor", "Initial-Password-2026"), SecuritySqlFixture.Client, Cancellation)
                .ConfigureAwait(true)).Failure);
        }
    }
}
