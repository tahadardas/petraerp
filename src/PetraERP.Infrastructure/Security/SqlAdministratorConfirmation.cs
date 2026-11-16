using System.Data;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Security;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Persistence.Actors;

namespace PetraERP.Infrastructure.Security;

/// <summary>Reauthenticates only the session actor, using the central verifier and persistent lockout state.</summary>
public sealed class SqlAdministratorConfirmation(CentralDataContext central, IPasswordHasher hasher,
    IAccountingActorAuthority authority, TimeProvider clock) : IAdministratorConfirmation
{
    public async Task ConfirmAsync(Guid actorUserId, string password, CancellationToken cancellationToken)
    {
        await authority.VerifyInstallationAdministratorAsync(actorUserId, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(password) || password.Length > 256)
            throw new BusinessRuleException("administrator.confirmation_failed", "Administrator password confirmation failed.");
        var now = clock.GetUtcNow();
        var transaction = await central.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        await using var lifetime = transaction.ConfigureAwait(false);
        var user = await central.Users.FromSqlInterpolated($"SELECT * FROM [security].[Users] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={actorUserId}")
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var allowed = user is { IsActive: true, MustChangePassword: false } && (user.LockoutEndUtc is null || user.LockoutEndUtc <= now);
        var verified = allowed && hasher.Verify(password, new PasswordHash(user!.PasswordAlgorithm, user.PasswordIterations, user.PasswordSalt, user.PasswordHash));
        if (user is not null && allowed)
        {
            user.AccessFailedCount = verified ? 0 : user.AccessFailedCount + 1;
            if (user.AccessFailedCount >= 5) user.LockoutEndUtc = now.AddMinutes(15);
            await central.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        central.SecurityAuditEvents.Add(SecurityAuditEntries.Create(now,
            verified ? SecurityAuditEventType.LoginSucceeded : SecurityAuditEventType.LoginFailed,
            ClientContext.Unknown, userId: actorUserId, actorUserId: actorUserId, reason: "Administrator reauthentication"));
        await central.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        if (!verified) throw new BusinessRuleException("administrator.confirmation_failed", "Administrator password confirmation failed.");
    }
}
