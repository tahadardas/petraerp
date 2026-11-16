using PetraERP.Domain.Primitives;

namespace PetraERP.Domain.Patterns;

public sealed class PatternRevision
{
    private PatternRevision() { }

    public PatternRevision(
        Guid id,
        Guid patternId,
        int revisionNumber,
        string configurationJson,
        Guid actorUserId,
        AuditReason reason,
        DateTime createdAtUtc)
    {
        if (id == Guid.Empty) throw new ArgumentException("Revision ID cannot be empty.", nameof(id));
        if (patternId == Guid.Empty) throw new ArgumentException("Pattern ID cannot be empty.", nameof(patternId));
        if (revisionNumber < 1) throw new ArgumentOutOfRangeException(nameof(revisionNumber), "Revision number must be >= 1.");
        if (actorUserId == Guid.Empty) throw new ArgumentException("Actor user ID cannot be empty.", nameof(actorUserId));
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationJson);
        ArgumentNullException.ThrowIfNull(reason);

        Id = id;
        PatternId = patternId;
        RevisionNumber = revisionNumber;
        State = PatternRevisionState.Draft;
        ConfigurationJson = configurationJson;
        ActorUserId = actorUserId;
        Reason = reason;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid PatternId { get; private set; }
    public int RevisionNumber { get; private set; }
    public PatternRevisionState State { get; private set; }
    public DateTime? EffectiveFromUtc { get; private set; }
    public DateTime? EffectiveToUtc { get; private set; }
    public string ConfigurationJson { get; private set; } = string.Empty;
    public Guid ActorUserId { get; private set; }
    public AuditReason Reason { get; private set; } = AuditReason.Create("Initial draft");
    public DateTime CreatedAtUtc { get; private set; }

    public bool IsImmutable => State is PatternRevisionState.Published or PatternRevisionState.Retired;

    public void UpdateDraft(string configurationJson, Guid actorUserId, AuditReason reason)
    {
        if (IsImmutable)
        {
            throw RuleViolation.Create(RuleViolation.PatternRevisionImmutable, $"Pattern revision {RevisionNumber} is {State} and cannot be modified. Create a new draft revision instead.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(configurationJson);
        ArgumentNullException.ThrowIfNull(reason);
        if (actorUserId == Guid.Empty) throw new ArgumentException("Actor user ID cannot be empty.", nameof(actorUserId));

        ConfigurationJson = configurationJson;
        ActorUserId = actorUserId;
        Reason = reason;
        State = PatternRevisionState.Draft;
    }

    public void Validate()
    {
        if (State != PatternRevisionState.Draft)
        {
            throw RuleViolation.Create(RuleViolation.PatternRevisionInvalidState, $"Only draft revisions can be validated. Current state: {State}.");
        }

        State = PatternRevisionState.Validated;
    }

    public void Publish(DateTime effectiveFromUtc)
    {
        if (State is not (PatternRevisionState.Draft or PatternRevisionState.Validated))
        {
            throw RuleViolation.Create(RuleViolation.PatternRevisionInvalidState, $"Cannot publish a revision in state {State}.");
        }

        State = PatternRevisionState.Published;
        EffectiveFromUtc = effectiveFromUtc;
    }

    public void Retire(DateTime effectiveToUtc)
    {
        if (State != PatternRevisionState.Published)
        {
            throw RuleViolation.Create(RuleViolation.PatternRevisionInvalidState, $"Only published revisions can be retired. Current state: {State}.");
        }

        State = PatternRevisionState.Retired;
        EffectiveToUtc = effectiveToUtc;
    }
}
