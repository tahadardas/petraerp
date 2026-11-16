namespace PetraERP.Application.GeneralLedger.Posting;

/// <summary>Immutable key-to-request binding, including requests whose financial transaction has not completed.</summary>
public sealed class PostingRequestBinding
{
    private PostingRequestBinding(PostingRequest request)
    {
        Scope = request.Scope;
        IdempotencyKey = request.IdempotencyKey;
        Operation = request.Operation;
        Fingerprint = request.Fingerprint;
    }

    public PostingRequestScope Scope { get; }
    public Guid IdempotencyKey { get; }
    public PostingOperation Operation { get; }
    public PostingFingerprint Fingerprint { get; }

    public static PostingRequestBinding Bind(PostingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new PostingRequestBinding(request);
    }

    public bool Matches(PostingRequest incoming)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        if (incoming.Scope.CompanyId != Scope.CompanyId || incoming.Scope.BranchId != Scope.BranchId
            || incoming.Operation != Operation || incoming.IdempotencyKey != IdempotencyKey)
        {
            throw new ArgumentException("Request binding lookup must use the exact company/branch/operation/key scope.", nameof(incoming));
        }

        return Fingerprint == incoming.Fingerprint;
    }
}
