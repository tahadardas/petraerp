namespace PetraERP.Application.Security;

/// <summary>A server-validated session: active, unexpired, unrevoked, and matching the user's security stamp.</summary>
public sealed record SessionPrincipal(Guid UserId, Guid SessionId, string UserName, bool MustChangePassword);
