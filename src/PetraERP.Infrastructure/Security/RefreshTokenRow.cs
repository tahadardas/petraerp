namespace PetraERP.Infrastructure.Security;

/// <summary>One rotation step of a session's refresh token. Only the SHA-256 hash of the token is stored.</summary>
internal sealed class RefreshTokenRow
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public byte[] TokenHash { get; set; } = [];
    public DateTimeOffset IssuedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? ConsumedAtUtc { get; set; }
    public Guid? ReplacedByTokenId { get; set; }
}
