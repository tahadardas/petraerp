namespace PetraERP.Infrastructure.Persistence.Actors;

/// <summary>Immutable first-verification evidence, never an authentication or authorization cache.</summary>
internal sealed class ActorReferenceRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public required string DisplayNameArabic { get; set; }
    public string? DisplayNameEnglish { get; set; }
    public DateTimeOffset FirstVerifiedAtUtc { get; set; }
}

/// <summary>A server-bound operation. Permission is derived, never supplied independently of the operation.</summary>
public sealed record AccountingWriteScope
{
    public AccountingWriteScope(Guid CompanyId, Guid FiscalYearId, Guid ActorUserId, AccountingOperation Operation, Guid? BranchId = null)
    {
        _ = AccountingOperationPolicy.Permission(Operation);
        this.CompanyId = CompanyId;
        this.FiscalYearId = FiscalYearId;
        this.ActorUserId = ActorUserId;
        this.Operation = Operation;
        this.BranchId = BranchId;
    }

    public AccountingWriteScope(Guid CompanyId, Guid FiscalYearId, Guid ActorUserId, string PermissionCode, Guid? BranchId = null)
        : this(CompanyId, FiscalYearId, ActorUserId, AccountingOperationPolicy.FromPermission(PermissionCode), BranchId) { }

    public Guid CompanyId { get; }
    public Guid FiscalYearId { get; }
    public Guid ActorUserId { get; }
    public AccountingOperation Operation { get; }
    public string PermissionCode => AccountingOperationPolicy.Permission(Operation);
    public Guid? BranchId { get; }
}

public sealed record VerifiedAccountingActor(Guid Id, string DisplayNameArabic, string? DisplayNameEnglish);

/// <summary>Authoritative, uncached central verification performed immediately before every accounting save.</summary>
public interface IAccountingActorAuthority
{
    Task VerifyInstallationAdministratorAsync(Guid actorId, CancellationToken cancellationToken);
    Task<VerifiedAccountingActor> VerifyAsync(AccountingWriteScope scope, bool provisioning, CancellationToken cancellationToken);
}
