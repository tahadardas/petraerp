namespace PetraERP.Domain.Parties;

public sealed record PartyAddress
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Label { get; init; } = string.Empty;
    public string? City { get; init; }
    public string? Street { get; init; }
    public string? PostalCode { get; init; }
    public string? Notes { get; init; }
}
