namespace PetraERP.Application.Security;

public sealed record CurrentUserProfile(
    Guid UserId,
    string UserName,
    string DisplayNameArabic,
    string? DisplayNameEnglish,
    bool MustChangePassword,
    Guid SessionId,
    DateTimeOffset SessionExpiresAtUtc,
    IReadOnlyList<string> Permissions);
