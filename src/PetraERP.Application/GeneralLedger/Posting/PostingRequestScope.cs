namespace PetraERP.Application.GeneralLedger.Posting;

/// <summary>Resolved application identity and scope; construction alone does not prove authorization.</summary>
public sealed record PostingRequestScope
{
    private PostingRequestScope(Guid companyId, Guid branchId, Guid actorUserId)
    {
        CompanyId = companyId;
        BranchId = branchId;
        ActorUserId = actorUserId;
    }

    public Guid CompanyId { get; }
    public Guid BranchId { get; }
    public Guid ActorUserId { get; }

    public static PostingRequestScope Create(Guid companyId, Guid branchId, Guid actorUserId)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Company ID is required.", nameof(companyId));
        }

        if (branchId == Guid.Empty)
        {
            throw new ArgumentException("Branch ID is required.", nameof(branchId));
        }

        if (actorUserId == Guid.Empty)
        {
            throw new ArgumentException("Actor ID is required.", nameof(actorUserId));
        }

        return new PostingRequestScope(companyId, branchId, actorUserId);
    }
}
