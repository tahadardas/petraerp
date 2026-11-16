using PetraERP.Domain.Primitives;

namespace PetraERP.Domain.Patterns;

public sealed class DocumentPattern
{
    private readonly List<PatternRevision> revisions = [];
    private readonly List<PatternBranchGrant> branchGrants = [];

    private DocumentPattern() { }

    public DocumentPattern(
        Guid id,
        Guid companyId,
        BusinessOperationKind operationKind,
        EntityCode code,
        BilingualName name,
        string shortCode,
        string? icon = null,
        string? group = null,
        bool isActive = true,
        bool isDefault = false)
    {
        if (id == Guid.Empty) throw new ArgumentException("Pattern ID cannot be empty.", nameof(id));
        if (companyId == Guid.Empty) throw new ArgumentException("Company ID cannot be empty.", nameof(companyId));
        if (!Enum.IsDefined(operationKind)) throw new ArgumentOutOfRangeException(nameof(operationKind), "Unknown business operation kind.");
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(name);
        DomainText.Required(shortCode, 16, nameof(shortCode));

        Id = id;
        CompanyId = companyId;
        OperationKind = operationKind;
        Code = code;
        Name = name;
        ShortCode = shortCode.Trim().ToUpperInvariant();
        Icon = string.IsNullOrWhiteSpace(icon) ? null : icon.Trim();
        Group = string.IsNullOrWhiteSpace(group) ? null : group.Trim();
        IsActive = isActive;
        IsDefault = isDefault;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public BusinessOperationKind OperationKind { get; private set; }
    public EntityCode Code { get; private set; } = null!;
    public BilingualName Name { get; private set; } = null!;
    public string ShortCode { get; private set; } = string.Empty;
    public string? Icon { get; private set; }
    public string? Group { get; private set; }
    public bool IsActive { get; private set; }
    public bool IsDefault { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyList<PatternRevision> Revisions => revisions.AsReadOnly();
    public IReadOnlyList<PatternBranchGrant> BranchGrants => branchGrants.AsReadOnly();

    public PatternRevision? ActiveRevision =>
        revisions.FirstOrDefault(r => r.State == PatternRevisionState.Published &&
                                      (!r.EffectiveToUtc.HasValue || r.EffectiveToUtc.Value > DateTime.UtcNow));

    public bool IsRunnable => BusinessOperationCatalog.IsRunnable(OperationKind);

    /// <summary>
    /// Updates mutable header fields. OperationKind and Code are strictly fixed and immutable after creation.
    /// </summary>
    public void Update(BilingualName name, string shortCode, string? icon, string? group, bool isActive, bool isDefault)
    {
        ArgumentNullException.ThrowIfNull(name);
        DomainText.Required(shortCode, 16, nameof(shortCode));

        Name = name;
        ShortCode = shortCode.Trim().ToUpperInvariant();
        Icon = string.IsNullOrWhiteSpace(icon) ? null : icon.Trim();
        Group = string.IsNullOrWhiteSpace(group) ? null : group.Trim();
        IsActive = isActive;
        IsDefault = isDefault;
    }

    /// <summary>
    /// Creates a new draft revision for this pattern. If revisions already exist, assigns the next revision number.
    /// </summary>
    public PatternRevision AddDraftRevision(Guid revisionId, string configurationJson, Guid actorUserId, AuditReason reason, DateTime utcNow)
    {
        if (revisions.Any(r => r.State == PatternRevisionState.Draft))
        {
            throw RuleViolation.Create(RuleViolation.PatternDraftAlreadyExists, "A draft revision already exists for this pattern. Revise or publish the existing draft first.");
        }

        var nextNumber = revisions.Count > 0 ? revisions.Max(r => r.RevisionNumber) + 1 : 1;
        var revision = new PatternRevision(revisionId, Id, nextNumber, configurationJson, actorUserId, reason, utcNow);
        revisions.Add(revision);
        return revision;
    }

    /// <summary>
    /// Publishes a draft or validated revision. Unimplemented roadmap operation kinds cannot be published into runnable workflows.
    /// Supersedes and retires any previously published revision.
    /// </summary>
    public void PublishRevision(Guid revisionId, DateTime utcNow)
    {
        if (!IsRunnable)
        {
            throw RuleViolation.Create(RuleViolation.PatternOperationNotRunnable, $"Operation kind '{OperationKind}' is not yet runnable in this system release and cannot be published into active operation.");
        }

        var target = revisions.FirstOrDefault(r => r.Id == revisionId)
            ?? throw RuleViolation.Create(RuleViolation.PatternRevisionInvalidState, $"Revision '{revisionId}' was not found in pattern '{Code}'.");

        // Retire currently published revision
        foreach (var pub in revisions.Where(r => r.State == PatternRevisionState.Published))
        {
            pub.Retire(utcNow);
        }

        target.Publish(utcNow);
    }

    public void Retire(DateTime utcNow)
    {
        IsActive = false;
        foreach (var pub in revisions.Where(r => r.State == PatternRevisionState.Published))
        {
            pub.Retire(utcNow);
        }
    }

    public void GrantBranch(Guid branchId)
    {
        if (branchId == Guid.Empty) throw new ArgumentException("Branch ID cannot be empty.", nameof(branchId));
        if (branchGrants.All(g => g.BranchId != branchId))
        {
            branchGrants.Add(new PatternBranchGrant(Guid.NewGuid(), Id, CompanyId, branchId));
        }
    }

    public void RevokeBranch(Guid branchId)
    {
        branchGrants.RemoveAll(g => g.BranchId == branchId);
    }

    public bool IsAvailableForBranch(Guid branchId) =>
        branchGrants.Count == 0 || branchGrants.Any(g => g.BranchId == branchId);
}
