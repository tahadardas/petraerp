using PetraERP.Application.Common;

namespace PetraERP.Application.Security;

public sealed record SecurityAuditRecord(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    SecurityAuditEventType EventType,
    Guid? UserId,
    string? UserNameSnapshot,
    Guid? ActorUserId,
    Guid? SessionId,
    Guid? CompanyId,
    Guid? BranchId,
    string? IpAddress,
    string? UserAgent,
    string? CorrelationId,
    string? Reason,
    string? Details);

public sealed record SecurityAuditQuery(
    Guid? UserId,
    SecurityAuditEventType? EventType,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    PageRequest Page);

/// <summary>Read-only access to the append-only security audit trail, newest first.</summary>
public interface ISecurityAuditQueries
{
    Task<PagedResult<SecurityAuditRecord>> ListAsync(SecurityAuditQuery query, CancellationToken cancellationToken);
}

/// <summary>Records standalone security events (for example a denied authorization attempt).</summary>
public interface ISecurityAuditRecorder
{
    Task RecordAuthorizationDeniedAsync(Guid userId, Guid? sessionId, string permissionOrScope, Guid? companyId, Guid? branchId,
        AccessDecision decision, ClientContext client, CancellationToken cancellationToken);
}
