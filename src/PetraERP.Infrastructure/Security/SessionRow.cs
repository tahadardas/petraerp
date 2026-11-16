namespace PetraERP.Infrastructure.Security;

/// <summary>A login session and refresh-token family. Revocation invalidates every token in the family.</summary>
internal sealed class SessionRow
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public byte ClientKind { get; set; }
    public string? DeviceName { get; set; }
    public Guid UserSecurityStamp { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset LastRefreshedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
    public byte? RevocationReason { get; set; }
}
