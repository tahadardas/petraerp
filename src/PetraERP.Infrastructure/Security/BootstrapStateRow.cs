namespace PetraERP.Infrastructure.Security;

/// <summary>Singleton marker (Id = 1) proving the one-time administrator bootstrap has completed.</summary>
internal sealed class BootstrapStateRow
{
    public byte Id { get; set; }
    public Guid AdministratorUserId { get; set; }
    public DateTimeOffset CompletedAtUtc { get; set; }
}
