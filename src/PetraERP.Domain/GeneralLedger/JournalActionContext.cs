using PetraERP.Domain.Primitives;

namespace PetraERP.Domain.GeneralLedger;

/// <summary>Action provenance; the application must obtain identity and time from trusted server context.</summary>
public sealed record JournalActionContext
{
    public const int MaximumReasonLength = AuditReason.MaximumLength;

    private JournalActionContext(Guid actorUserId, Guid correlationId, string reason, DateTimeOffset occurredAtUtc)
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

    public static JournalActionContext Create(Guid actorUserId, Guid correlationId, string reason, DateTimeOffset occurredAtUtc)
    {
        if (actorUserId == Guid.Empty)
        {
            throw new ArgumentException("Actor ID must be nonempty.", nameof(actorUserId));
        }

        if (correlationId == Guid.Empty)
        {
            throw new ArgumentException("Correlation ID must be nonempty.", nameof(correlationId));
        }

        if (occurredAtUtc == default || occurredAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("A nondefault UTC timestamp is required.", nameof(occurredAtUtc));
        }

        return new JournalActionContext(actorUserId, correlationId,
            AuditReason.Create(reason, nameof(reason)).Value, occurredAtUtc);
    }
}
