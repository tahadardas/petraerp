namespace PetraERP.Domain.GeneralLedger;

public enum JournalAuditAction
{
    Created = 1,
    Revised = 2,
    Validated = 3,
    Approved = 4,
    ReturnedToDraft = 5,
    Posted = 6,
    Reversed = 7,
}
