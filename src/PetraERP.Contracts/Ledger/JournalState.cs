namespace PetraERP.Contracts.Ledger;

/// <summary>
/// Journal lifecycle state on the wire. The server serializes enums by name only (integers are rejected),
/// so clients compare names and never depend on numeric values. Numeric values mirror the Domain enum
/// and are pinned by contract tests; they are not part of the transport.
/// </summary>
public enum JournalState
{
    Draft = 1,
    Validated = 2,
    Approved = 3,
    Posted = 4,
    Reversed = 5,
}
