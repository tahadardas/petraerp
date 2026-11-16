namespace PetraERP.Contracts.Context;

public sealed record CurrentUserResponse
{
    public required Guid UserId { get; init; }
    public required string UserName { get; init; }
    public required string DisplayNameArabic { get; init; }
    public string? DisplayNameEnglish { get; init; }
    public required bool MustChangePassword { get; init; }
    public required Guid SessionId { get; init; }
    public required DateTimeOffset SessionExpiresAtUtc { get; init; }

    /// <summary>Permission codes granted through the user's roles. Scope is still checked per request.</summary>
    public required IReadOnlyList<string> Permissions { get; init; }
}
