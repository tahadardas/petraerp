namespace PetraERP.Infrastructure.Parties;

/// <summary>Immutable credit-policy change evidence; operational balances are never stored here.</summary>
internal sealed class PartyPolicyAuditRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid PartyId { get; set; }
    public Guid ActorId { get; set; }
    public decimal BeforeCreditLimit { get; set; }
    public decimal AfterCreditLimit { get; set; }
    public int BeforePolicy { get; set; }
    public int AfterPolicy { get; set; }
    public bool BeforeBlocked { get; set; }
    public bool AfterBlocked { get; set; }
    public DateTimeOffset DateUtc { get; set; }
}
