namespace PetraERP.Desktop.Security;

public sealed record StoredSession(string RefreshToken, DateTimeOffset RefreshExpiresUtc, DateTimeOffset SessionExpiresUtc)
{
    public override string ToString() => nameof(StoredSession);
}

public interface IProtectedSessionStore
{
    StoredSession? Read();
    void Save(StoredSession session);
    void Delete();
}

internal sealed class VolatileSessionStore : IProtectedSessionStore
{
    public StoredSession? Read() => null;
    public void Save(StoredSession session) { }
    public void Delete() { }
}
