using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Security;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Security;

/// <summary>
/// Per-request session check: an access token is honoured only while its session is unrevoked,
/// unexpired, the user is active and the user's security stamp still matches the session.
/// This makes logout, disable and password change effective immediately, not at token expiry.
/// </summary>
public sealed class SqlSessionValidator : ISessionValidator
{
    private readonly CentralDataContext context;
    private readonly TimeProvider time;

    public SqlSessionValidator(CentralDataContext context, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(time);
        this.context = context;
        this.time = time;
    }

    public async Task<SessionPrincipal?> ValidateAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var principal = await (
                from session in context.Sessions.AsNoTracking()
                join user in context.Users.AsNoTracking() on session.UserId equals user.Id
                where session.Id == sessionId && session.UserId == userId && session.RevokedAtUtc == null
                    && session.ExpiresAtUtc > now && user.IsActive && user.SecurityStamp == session.UserSecurityStamp
                select new SessionPrincipal(user.Id, session.Id, user.UserName, user.MustChangePassword))
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return principal;
    }
}
