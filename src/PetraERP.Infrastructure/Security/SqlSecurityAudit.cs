using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Security;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Security;

public sealed class SqlSecurityAuditQueries : ISecurityAuditQueries
{
    private readonly CentralDataContext context;

    public SqlSecurityAuditQueries(CentralDataContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        this.context = context;
    }

    public async Task<PagedResult<SecurityAuditRecord>> ListAsync(SecurityAuditQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var rows = context.SecurityAuditEvents.AsNoTracking();
        if (query.UserId is { } userId)
        {
            rows = rows.Where(row => row.UserId == userId || row.ActorUserId == userId);
        }

        if (query.EventType is { } eventType)
        {
            var type = (byte)eventType;
            rows = rows.Where(row => row.EventType == type);
        }

        if (query.FromUtc is { } from)
        {
            rows = rows.Where(row => row.OccurredAtUtc >= from);
        }

        if (query.ToUtc is { } to)
        {
            rows = rows.Where(row => row.OccurredAtUtc < to);
        }

        var total = await rows.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var page = await rows
            .OrderByDescending(static row => row.OccurredAtUtc)
            .ThenByDescending(static row => row.Id)
            .Skip(query.Page.Skip)
            .Take(query.Page.PageSize)
            .Select(static row => new SecurityAuditRecord(row.Id, row.OccurredAtUtc, (SecurityAuditEventType)row.EventType,
                row.UserId, row.UserNameSnapshot, row.ActorUserId, row.SessionId, row.CompanyId, row.BranchId, row.IpAddress,
                row.UserAgent, row.CorrelationId, row.Reason, row.Details))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return new PagedResult<SecurityAuditRecord>(page.AsReadOnly(), query.Page.Page, query.Page.PageSize, total);
    }
}

public sealed class SqlSecurityAuditRecorder : ISecurityAuditRecorder
{
    private readonly CentralDataContext context;
    private readonly TimeProvider time;

    public SqlSecurityAuditRecorder(CentralDataContext context, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(time);
        this.context = context;
        this.time = time;
    }

    public async Task RecordAuthorizationDeniedAsync(Guid userId, Guid? sessionId, string permissionOrScope, Guid? companyId,
        Guid? branchId, AccessDecision decision, ClientContext client, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(permissionOrScope);
        ArgumentNullException.ThrowIfNull(client);
        context.SecurityAuditEvents.Add(SecurityAuditEntries.Create(time.GetUtcNow(), SecurityAuditEventType.AuthorizationDenied,
            client, userId: userId, actorUserId: userId, sessionId: sessionId, companyId: companyId, branchId: branchId,
            reason: decision.ToString(), details: new { requirement = permissionOrScope }));
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
