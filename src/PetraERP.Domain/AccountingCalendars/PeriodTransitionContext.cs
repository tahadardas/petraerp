using PetraERP.Domain.Primitives;

namespace PetraERP.Domain.AccountingCalendars;

public sealed class PeriodTransitionContext
{
    public const int MaximumReasonLength = AuditReason.MaximumLength;

    private PeriodTransitionContext(Guid actorUserId, Guid correlationId, string reason, DateTimeOffset occurredAtUtc)
    {
        ActorUserId = actorUserId;
        CorrelationId = correlationId;
        Reason = reason;
        OccurredAtUtc = occurredAtUtc;
    }

    public Guid ActorUserId { get; }
    public Guid CorrelationId { get; }
    public string Reason { get; }
    public DateTimeOffset OccurredAtUtc { get; }

    public static PeriodTransitionContext Create(Guid actorUserId, Guid correlationId, string reason, DateTimeOffset occurredAtUtc)
    {
        if (actorUserId == Guid.Empty)
        {
            throw new ArgumentException("Actor ID must be nonempty.", nameof(actorUserId));
        }

        if (correlationId == Guid.Empty)
        {
            throw new ArgumentException("Correlation ID must be nonempty.", nameof(correlationId));
        }

        if (occurredAtUtc.Offset != TimeSpan.Zero || occurredAtUtc == default)
        {
            throw new ArgumentException("A nondefault UTC event timestamp is required.", nameof(occurredAtUtc));
        }

        return new PeriodTransitionContext(actorUserId, correlationId,
            AuditReason.Create(reason, nameof(reason)).Value, occurredAtUtc);
    }
}
