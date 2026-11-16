namespace PetraERP.Domain.Inventory;

public enum StockMovementKind
{
    OpeningBalance = 1,
    PurchaseReceipt = 2,
    PurchaseReturn = 3,
    SalesDelivery = 4,
    SalesReturn = 5,
    TransferIn = 6,
    TransferOut = 7,
    AdjustmentIncrease = 8,
    AdjustmentDecrease = 9,
    InternalIssue = 10
}

public static class StockMovementKindExtensions
{
    public static bool IsInward(this StockMovementKind kind) => kind switch
    {
        StockMovementKind.OpeningBalance => true,
        StockMovementKind.PurchaseReceipt => true,
        StockMovementKind.SalesReturn => true,
        StockMovementKind.TransferIn => true,
        StockMovementKind.AdjustmentIncrease => true,
        _ => false
    };

    public static bool IsOutward(this StockMovementKind kind) => kind switch
    {
        StockMovementKind.PurchaseReturn => true,
        StockMovementKind.SalesDelivery => true,
        StockMovementKind.TransferOut => true,
        StockMovementKind.AdjustmentDecrease => true,
        StockMovementKind.InternalIssue => true,
        _ => false
    };

    public static int DirectionSign(this StockMovementKind kind) =>
        kind.IsInward() ? 1 : (kind.IsOutward() ? -1 : 0);
}
