namespace PetraERP.Contracts.Ledger;

/// <summary>The root-to-current path (inclusive) and a page of immediate children, ordered by ordinal code.</summary>
public sealed record AccountCardContextResponse
{
    public required IReadOnlyList<AccountResponse> Path { get; init; }
    public required AccountPageResponse Children { get; init; }
}
