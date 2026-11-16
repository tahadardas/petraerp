namespace PetraERP.Infrastructure.AlAmeenMigration;
public sealed record SourceAccount(
    Guid Id,
    Guid? ParentId,
    string Code,
    string Name,
    short AccountType,
    short FinalAccount);

public sealed record SourceParty(
    Guid Id,
    Guid AccountId,
    string Code,
    string Name,
    string? Phone,
    string? Address,
    bool IsCustomer,
    bool IsSupplier);

public sealed record SourceItem(
    Guid Id,
    Guid? ParentId,
    string Code,
    string Name,
    string? Barcode,
    string UnitName,
    decimal DefaultPrice);

public sealed record SourceWarehouse(
    Guid Id,
    Guid? AccountId,
    string Code,
    string Name);

public sealed record SourceCurrency(
    Guid Id,
    string Code,
    string Name,
    decimal EquivalentRate,
    int Precision);

public sealed record SourceVoucher(
    Guid Id,
    string Number,
    DateTime Date,
    Guid CurrencyId,
    decimal Rate,
    string? Description,
    IReadOnlyList<SourceVoucherLine> Lines);

public sealed record SourceVoucherLine(
    Guid Id,
    Guid ParentId,
    Guid AccountId,
    Guid CurrencyId,
    decimal Rate,
    decimal Debit,
    decimal Credit,
    string? Description,
    Guid? CostCenterId);

public sealed record SourceOpenItem(
    Guid Id,
    Guid PartyId,
    Guid AccountId,
    string DocumentNumber,
    DateTime DocumentDate,
    string CurrencyCode,
    decimal OriginalAmount,
    decimal RemainingAmount,
    int PartyRole);

public sealed record SourceStockBalance(
    Guid ItemId,
    Guid StoreId,
    decimal Quantity,
    decimal UnitCost,
    decimal TotalValue);
