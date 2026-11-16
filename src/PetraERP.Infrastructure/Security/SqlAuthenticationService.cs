using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Security;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Security;

/// <summary>
/// SQL Server implementation of login, refresh-token rotation with reuse detection, logout and
/// password change. Failed-attempt counting is a single atomic UPDATE so concurrent guesses cannot
/// lose increments. Every outcome writes an append-only security audit event.
/// </summary>
public sealed class SqlAuthenticationService : IAuthenticationService
{
    private const int MaximumDeviceNameLength = 100;

    private readonly CentralDataContext context;
    private readonly IPasswordHasher hasher;
    private readonly IAccessTokenIssuer tokenIssuer;
    private readonly AuthenticationSettings settings;
    private readonly TimeProvider time;

    public SqlAuthenticationService(CentralDataContext context, IPasswordHasher hasher, IAccessTokenIssuer tokenIssuer,
        AuthenticationSettings settings, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(hasher);
        ArgumentNullException.ThrowIfNull(tokenIssuer);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(time);
        this.context = context;
        this.hasher = hasher;
        this.tokenIssuer = tokenIssuer;
        this.settings = settings;
        this.time = time;
    }

    public async Task<AuthenticationOutcome> LoginAsync(LoginCommand command, ClientContext client, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(client);
        var now = time.GetUtcNow();
        if (!LoginName.TryNormalize(command.UserName, out _, out var normalized) || command.Password.Length == 0
            || command.Password.Length > PasswordPolicy.MaximumLength)
        {
            hasher.SimulateVerification(command.Password.Length > PasswordPolicy.MaximumLength ? string.Empty : command.Password);
            await AppendAuditAsync(SecurityAuditEntries.Create(now, SecurityAuditEventType.LoginFailed, client,
                userName: LoginName.NormalizeForLookup(command.UserName), reason: "invalid_credentials"), cancellationToken)
                .ConfigureAwait(false);
            return AuthenticationOutcome.Fail(AuthenticationFailure.InvalidCredentials);
        }

        var user = await context.Users.AsNoTracking()
            .Where(row => row.NormalizedUserName == normalized)
            .Select(static row => new LoginCandidate(row.Id, row.UserName, row.IsActive, row.LockoutEndUtc, row.SecurityStamp,
                row.MustChangePassword, row.PasswordAlgorithm, row.PasswordIterations, row.PasswordSalt, row.PasswordHash))
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (user is null)
        {
            hasher.SimulateVerification(command.Password);
            await AppendAuditAsync(SecurityAuditEntries.Create(now, SecurityAuditEventType.LoginFailed, client,
                userName: normalized, reason: "invalid_credentials"), cancellationToken).ConfigureAwait(false);
            return AuthenticationOutcome.Fail(AuthenticationFailure.InvalidCredentials);
        }

        // During lockout the password is never verified, so a locked account cannot be probed.
        if (user.LockoutEndUtc is { } lockedUntil && lockedUntil > now)
        {
            await AppendAuditAsync(SecurityAuditEntries.Create(now, SecurityAuditEventType.LoginRejectedLockedOut, client,
                userId: user.Id, userName: user.UserName), cancellationToken).ConfigureAwait(false);
            return AuthenticationOutcome.Fail(AuthenticationFailure.LockedOut, lockedUntil);
        }

        var stored = new PasswordHash(user.PasswordAlgorithm, user.PasswordIterations, user.PasswordSalt, user.PasswordHash);
        if (!hasher.Verify(command.Password, stored))
        {
            var lockoutEnd = await RegisterFailedAttemptAsync(user.Id, user.UserName, now, client,
                SecurityAuditEventType.LoginFailed, cancellationToken).ConfigureAwait(false);
            return lockoutEnd is { } end && end > now
                ? AuthenticationOutcome.Fail(AuthenticationFailure.LockedOut, end)
                : AuthenticationOutcome.Fail(AuthenticationFailure.InvalidCredentials);
        }

        // Disabled accounts are reported only after a correct password, so disabled names cannot be enumerated.
        if (!user.IsActive)
        {
            await AppendAuditAsync(SecurityAuditEntries.Create(now, SecurityAuditEventType.LoginRejectedDisabled, client,
                userId: user.Id, userName: user.UserName), cancellationToken).ConfigureAwait(false);
            return AuthenticationOutcome.Fail(AuthenticationFailure.AccountDisabled);
        }

        var rehash = hasher.NeedsRehash(stored) ? hasher.Hash(command.Password) : null;
        var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            var updated = await context.Users
                .Where(row => row.Id == user.Id && row.IsActive && row.SecurityStamp == user.SecurityStamp)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(static row => row.AccessFailedCount, 0)
                    .SetProperty(static row => row.LockoutEndUtc, (DateTimeOffset?)null)
                    .SetProperty(static row => row.LastLoginAtUtc, now)
                    .SetProperty(static row => row.UpdatedAtUtc, now), cancellationToken)
                .ConfigureAwait(false);
            if (updated == 0)
            {
                // Disabled or credentials rotated concurrently: do not open a session.
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return AuthenticationOutcome.Fail(AuthenticationFailure.InvalidCredentials);
            }

            if (rehash is not null)
            {
                await context.Users.Where(row => row.Id == user.Id)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(static row => row.PasswordIterations, rehash.Iterations)
                        .SetProperty(static row => row.PasswordSalt, rehash.Salt.ToArray())
                        .SetProperty(static row => row.PasswordHash, rehash.Hash.ToArray()), cancellationToken)
                    .ConfigureAwait(false);
            }

            var tokens = OpenSession(user.Id, user.SecurityStamp, user.MustChangePassword, command.ClientKind,
                command.DeviceName, now);
            context.SecurityAuditEvents.Add(SecurityAuditEntries.Create(now, SecurityAuditEventType.LoginSucceeded, client,
                userId: user.Id, userName: user.UserName, sessionId: tokens.SessionId,
                details: new { clientKind = command.ClientKind.ToString() }));
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return AuthenticationOutcome.Success(tokens);
        }
    }

    public async Task<AuthenticationOutcome> RefreshAsync(string refreshToken, ClientContext client, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        var now = time.GetUtcNow();
        var hash = RefreshTokenCodec.TryHash(refreshToken);
        if (hash is null)
        {
            await AppendAuditAsync(SecurityAuditEntries.Create(now, SecurityAuditEventType.RefreshRejected, client,
                reason: "malformed_token"), cancellationToken).ConfigureAwait(false);
            return AuthenticationOutcome.Fail(AuthenticationFailure.InvalidRefreshToken);
        }

        var candidate = await (
                from token in context.RefreshTokens.AsNoTracking()
                join session in context.Sessions.AsNoTracking() on token.SessionId equals session.Id
                join user in context.Users.AsNoTracking() on session.UserId equals user.Id
                where token.TokenHash == hash
                select new RefreshCandidate(token.Id, token.ExpiresAtUtc, token.ConsumedAtUtc, session.Id, session.ExpiresAtUtc,
                    session.RevokedAtUtc, session.UserSecurityStamp, (ClientKind)session.ClientKind, user.Id, user.UserName,
                    user.IsActive, user.SecurityStamp, user.MustChangePassword))
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (candidate is null)
        {
            await AppendAuditAsync(SecurityAuditEntries.Create(now, SecurityAuditEventType.RefreshRejected, client,
                reason: "unknown_token"), cancellationToken).ConfigureAwait(false);
            return AuthenticationOutcome.Fail(AuthenticationFailure.InvalidRefreshToken);
        }

        if (candidate.ConsumedAtUtc is not null)
        {
            // A rotated token was presented again: treat the whole family as compromised.
            await RevokeFamilyForReuseAsync(candidate, now, client, cancellationToken).ConfigureAwait(false);
            return AuthenticationOutcome.Fail(AuthenticationFailure.RefreshTokenReused);
        }

        var rejection = EvaluateRefreshCandidate(candidate, now);
        if (rejection is { } failure)
        {
            if (failure == AuthenticationFailure.AccountDisabled || candidate.SessionStamp != candidate.UserStamp)
            {
                var rejectionTx = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                await using (rejectionTx.ConfigureAwait(false))
                {
                    await RevokeSessionsAsync(context.Sessions.Where(row => row.Id == candidate.SessionId),
                        failure == AuthenticationFailure.AccountDisabled ? SessionRevocationReason.UserDisabled : SessionRevocationReason.SecurityStampChanged,
                        now, cancellationToken).ConfigureAwait(false);
                    context.SecurityAuditEvents.Add(SecurityAuditEntries.Create(now, SecurityAuditEventType.RefreshRejected, client,
                        userId: candidate.UserId, userName: candidate.UserName, sessionId: candidate.SessionId,
                        reason: failure.ToString()));
                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    await rejectionTx.CommitAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            else
            {
                await AppendAuditAsync(SecurityAuditEntries.Create(now, SecurityAuditEventType.RefreshRejected, client,
                    userId: candidate.UserId, userName: candidate.UserName, sessionId: candidate.SessionId,
                    reason: failure.ToString()), cancellationToken).ConfigureAwait(false);
            }

            return AuthenticationOutcome.Fail(failure);
        }

        var (newToken, newHash) = RefreshTokenCodec.Generate();
        var newTokenId = Guid.CreateVersion7(now);
        var refreshExpires = Min(now + settings.RefreshTokenIdleLifetime, candidate.SessionExpiresAtUtc);
        var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            // Conditional consumption: exactly one concurrent presenter can rotate a token.
            var consumed = await context.RefreshTokens
                .Where(row => row.Id == candidate.TokenId && row.ConsumedAtUtc == null)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(static row => row.ConsumedAtUtc, now)
                    .SetProperty(static row => row.ReplacedByTokenId, newTokenId), cancellationToken)
                .ConfigureAwait(false);
            var touched = consumed == 0 ? 0 : await context.Sessions
                .Where(row => row.Id == candidate.SessionId && row.RevokedAtUtc == null && row.ExpiresAtUtc > now)
                .ExecuteUpdateAsync(setters => setters.SetProperty(static row => row.LastRefreshedAtUtc, now), cancellationToken)
                .ConfigureAwait(false);
            if (consumed == 0 || touched == 0)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                if (consumed == 0)
                {
                    await RevokeFamilyForReuseAsync(candidate, now, client, cancellationToken).ConfigureAwait(false);
                    return AuthenticationOutcome.Fail(AuthenticationFailure.RefreshTokenReused);
                }

                return AuthenticationOutcome.Fail(AuthenticationFailure.SessionExpired);
            }

            context.RefreshTokens.Add(new RefreshTokenRow
            {
                Id = newTokenId,
                SessionId = candidate.SessionId,
                TokenHash = newHash,
                IssuedAtUtc = now,
                ExpiresAtUtc = refreshExpires,
            });
            context.SecurityAuditEvents.Add(SecurityAuditEntries.Create(now, SecurityAuditEventType.TokenRefreshed, client,
                userId: candidate.UserId, userName: candidate.UserName, sessionId: candidate.SessionId));
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        var access = tokenIssuer.Issue(candidate.UserId, candidate.SessionId, now);
        return AuthenticationOutcome.Success(new SessionTokens(candidate.SessionId, candidate.UserId, access, newToken,
            refreshExpires, candidate.SessionExpiresAtUtc, candidate.MustChangePassword));
    }

    public async Task LogoutAsync(Guid userId, Guid sessionId, ClientContext client, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        var now = time.GetUtcNow();
        var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            var revoked = await RevokeSessionsAsync(
                context.Sessions.Where(row => row.Id == sessionId && row.UserId == userId),
                SessionRevocationReason.Logout, now, cancellationToken).ConfigureAwait(false);
            if (revoked > 0)
            {
                context.SecurityAuditEvents.Add(SecurityAuditEntries.Create(now, SecurityAuditEventType.Logout, client,
                    userId: userId, sessionId: sessionId));
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<int> LogoutAllAsync(Guid userId, Guid sessionId, ClientContext client, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        var now = time.GetUtcNow();
        var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            var revoked = await RevokeSessionsAsync(context.Sessions.Where(row => row.UserId == userId),
                SessionRevocationReason.LogoutAll, now, cancellationToken).ConfigureAwait(false);
            context.SecurityAuditEvents.Add(SecurityAuditEntries.Create(now, SecurityAuditEventType.LogoutAllSessions, client,
                userId: userId, sessionId: sessionId, details: new { revokedSessions = revoked }));
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return revoked;
        }
    }

    public async Task<AuthenticationOutcome> ChangePasswordAsync(ChangePasswordCommand command, ClientKind clientKind,
        ClientContext client, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(client);
        var now = time.GetUtcNow();
        var user = await context.Users.FirstOrDefaultAsync(row => row.Id == command.UserId && row.IsActive, cancellationToken)
            .ConfigureAwait(false);
        if (user is null)
        {
            return AuthenticationOutcome.Fail(AuthenticationFailure.SessionExpired);
        }

        if (user.LockoutEndUtc is { } lockedUntil && lockedUntil > now)
        {
            return AuthenticationOutcome.Fail(AuthenticationFailure.LockedOut, lockedUntil);
        }

        var stored = new PasswordHash(user.PasswordAlgorithm, user.PasswordIterations, user.PasswordSalt, user.PasswordHash);
        if (command.CurrentPassword.Length > PasswordPolicy.MaximumLength || !hasher.Verify(command.CurrentPassword, stored))
        {
            context.ChangeTracker.Clear();
            var lockoutEnd = await RegisterFailedAttemptAsync(user.Id, user.UserName, now, client,
                SecurityAuditEventType.PasswordChangeFailed, cancellationToken).ConfigureAwait(false);
            return lockoutEnd is { } end && end > now
                ? AuthenticationOutcome.Fail(AuthenticationFailure.LockedOut, end)
                : AuthenticationOutcome.Fail(AuthenticationFailure.InvalidCurrentPassword);
        }

        var violation = PasswordPolicy.Validate(command.NewPassword, user.UserName);
        if (violation is null && hasher.Verify(command.NewPassword, stored))
        {
            violation = PasswordPolicyViolation.ReusesCurrentPassword;
        }

        if (violation is { } policyViolation)
        {
            return AuthenticationOutcome.PolicyFailure(policyViolation);
        }

        var hash = hasher.Hash(command.NewPassword);
        var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            user.PasswordAlgorithm = hash.Algorithm;
            user.PasswordIterations = hash.Iterations;
            user.PasswordSalt = hash.Salt.ToArray();
            user.PasswordHash = hash.Hash.ToArray();
            user.PasswordChangedAtUtc = now;
            user.MustChangePassword = false;
            user.AccessFailedCount = 0;
            user.LockoutEndUtc = null;
            user.SecurityStamp = Guid.NewGuid();
            try
            {
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException exception)
            {
                throw new ConcurrencyConflictException("The user account changed during the password update.", exception);
            }

            await RevokeSessionsAsync(context.Sessions.Where(row => row.UserId == user.Id),
                SessionRevocationReason.PasswordChanged, now, cancellationToken).ConfigureAwait(false);
            var tokens = OpenSession(user.Id, user.SecurityStamp, mustChangePassword: false, clientKind, null, now);
            context.SecurityAuditEvents.Add(SecurityAuditEntries.Create(now, SecurityAuditEventType.PasswordChanged, client,
                userId: user.Id, userName: user.UserName, actorUserId: user.Id, sessionId: tokens.SessionId,
                details: new { previousSessionId = command.SessionId }));
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return AuthenticationOutcome.Success(tokens);
        }
    }

    private SessionTokens OpenSession(Guid userId, Guid securityStamp, bool mustChangePassword, ClientKind clientKind,
        string? deviceName, DateTimeOffset now)
    {
        var sessionId = Guid.CreateVersion7(now);
        var sessionExpires = now + settings.SessionAbsoluteLifetime;
        var refreshExpires = Min(now + settings.RefreshTokenIdleLifetime, sessionExpires);
        var (refreshToken, refreshHash) = RefreshTokenCodec.Generate();
        context.Sessions.Add(new SessionRow
        {
            Id = sessionId,
            UserId = userId,
            ClientKind = Enum.IsDefined(clientKind) ? (byte)clientKind : (byte)ClientKind.Desktop,
            DeviceName = SecurityText.BoundLabel(deviceName, MaximumDeviceNameLength),
            UserSecurityStamp = securityStamp,
            CreatedAtUtc = now,
            LastRefreshedAtUtc = now,
            ExpiresAtUtc = sessionExpires,
        });
        context.RefreshTokens.Add(new RefreshTokenRow
        {
            Id = Guid.CreateVersion7(now),
            SessionId = sessionId,
            TokenHash = refreshHash,
            IssuedAtUtc = now,
            ExpiresAtUtc = refreshExpires,
        });
        var access = tokenIssuer.Issue(userId, sessionId, now);
        return new SessionTokens(sessionId, userId, access, refreshToken, refreshExpires, sessionExpires, mustChangePassword);
    }

    private async Task<DateTimeOffset?> RegisterFailedAttemptAsync(Guid userId, string userName, DateTimeOffset now,
        ClientContext client, SecurityAuditEventType eventType, CancellationToken cancellationToken)
    {
        var maximum = settings.MaxFailedAccessAttempts;
        DateTimeOffset? lockoutEnd = now + settings.LockoutDuration;
        var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            // Single statement: SET expressions read pre-update values, so the threshold test is exact under concurrency.
            await context.Users.Where(row => row.Id == userId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(static row => row.LockoutEndUtc,
                        row => row.AccessFailedCount + 1 >= maximum ? lockoutEnd : row.LockoutEndUtc)
                    .SetProperty(static row => row.AccessFailedCount,
                        row => row.AccessFailedCount + 1 >= maximum ? 0 : row.AccessFailedCount + 1)
                    .SetProperty(static row => row.UpdatedAtUtc, now), cancellationToken)
                .ConfigureAwait(false);
            var state = await context.Users.AsNoTracking().Where(row => row.Id == userId)
                .Select(static row => row.LockoutEndUtc).FirstAsync(cancellationToken).ConfigureAwait(false);
            var lockedNow = state is { } end && end > now && end == lockoutEnd;
            context.SecurityAuditEvents.Add(SecurityAuditEntries.Create(now, eventType, client, userId: userId,
                userName: userName, reason: "invalid_credentials"));
            if (lockedNow)
            {
                context.SecurityAuditEvents.Add(SecurityAuditEntries.Create(now, SecurityAuditEventType.AccountLockedOut, client,
                    userId: userId, userName: userName, details: new { lockoutEndUtc = state }));
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return state;
        }
    }

    private async Task RevokeFamilyForReuseAsync(RefreshCandidate candidate, DateTimeOffset now, ClientContext client,
        CancellationToken cancellationToken)
    {
        var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            await RevokeSessionsAsync(context.Sessions.Where(row => row.Id == candidate.SessionId),
                SessionRevocationReason.RefreshTokenReuse, now, cancellationToken).ConfigureAwait(false);
            context.SecurityAuditEvents.Add(SecurityAuditEntries.Create(now, SecurityAuditEventType.RefreshTokenReuseDetected, client,
                userId: candidate.UserId, userName: candidate.UserName, sessionId: candidate.SessionId));
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static Task<int> RevokeSessionsAsync(IQueryable<SessionRow> sessions, SessionRevocationReason reason,
        DateTimeOffset now, CancellationToken cancellationToken) =>
        sessions.Where(static row => row.RevokedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(static row => row.RevokedAtUtc, now)
                .SetProperty(static row => row.RevocationReason, (byte?)reason), cancellationToken);

    private async Task AppendAuditAsync(SecurityAuditEventRow row, CancellationToken cancellationToken)
    {
        context.SecurityAuditEvents.Add(row);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static AuthenticationFailure? EvaluateRefreshCandidate(RefreshCandidate candidate, DateTimeOffset now)
    {
        if (!candidate.UserIsActive)
        {
            return AuthenticationFailure.AccountDisabled;
        }

        if (candidate.SessionRevokedAtUtc is not null || candidate.SessionExpiresAtUtc <= now
            || candidate.TokenExpiresAtUtc <= now || candidate.SessionStamp != candidate.UserStamp)
        {
            return AuthenticationFailure.SessionExpired;
        }

        return null;
    }

    private static DateTimeOffset Min(DateTimeOffset first, DateTimeOffset second) => first <= second ? first : second;

    private sealed record LoginCandidate(Guid Id, string UserName, bool IsActive, DateTimeOffset? LockoutEndUtc,
        Guid SecurityStamp, bool MustChangePassword, byte PasswordAlgorithm, int PasswordIterations, byte[] PasswordSalt,
        byte[] PasswordHash);

    private sealed record RefreshCandidate(Guid TokenId, DateTimeOffset TokenExpiresAtUtc, DateTimeOffset? ConsumedAtUtc,
        Guid SessionId, DateTimeOffset SessionExpiresAtUtc, DateTimeOffset? SessionRevokedAtUtc, Guid SessionStamp,
        ClientKind ClientKind, Guid UserId, string UserName, bool UserIsActive, Guid UserStamp, bool MustChangePassword);
}
