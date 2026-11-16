namespace PetraERP.Domain.Inventory;

public enum InventoryDocumentKind { Transfer = 1, Count = 2, InternalIssue = 3 }
public enum InventoryDocumentState { Draft = 0, Posted = 1, Reversed = 2 }
