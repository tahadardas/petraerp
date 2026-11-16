namespace PetraERP.Domain.GeneralLedger;

public enum JournalEntryState
{
    Draft = 1,
    Validated = 2,
    Approved = 3,
    Posted = 4,
    Reversed = 5,
}
