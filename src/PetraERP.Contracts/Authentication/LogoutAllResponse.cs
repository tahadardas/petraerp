namespace PetraERP.Contracts.Authentication;

public sealed record LogoutAllResponse
{
    public required int RevokedSessions { get; init; }
}
