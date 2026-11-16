namespace PetraERP.Application.GeneralLedger.Posting;

/// <summary>
/// Thrown when the posting actor lacks current authorization (permission, company scope, or branch scope)
/// inside the financial transaction or on the replay path.
/// </summary>
public sealed class PostingAuthorizationException : InvalidOperationException
{
    public PostingAuthorizationException(string message)
        : base(message)
    {
    }
}
