namespace PetraERP.Domain.GeneralLedger;

/// <summary>Persisted facts about an account resolved by the application inside the change transaction.</summary>
public sealed record AccountUsage(bool HasJournalLines, bool HasChildren, bool HasActiveChildren);
