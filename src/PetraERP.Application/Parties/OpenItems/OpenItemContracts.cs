namespace PetraERP.Application.Parties.OpenItems;

public sealed record CreateOpenItemCommand(
    Guid BranchId,
    Guid PartyId,
    byte PartyType, // 1=Customer, 2=Supplier
    Guid? OriginFiscalYearId,
    Guid? OriginDocumentId,
    string? OriginDocumentNumber,
    DateTimeOffset OriginDocumentDateUtc,
    DateTimeOffset? DueDateUtc,
    string CurrencyCode,
    decimal OriginalAmount,
    decimal ExchangeRate,
    byte OriginType, // 1=CurrentYearInvoice, 2=PriorYearInvoiceRollover, 3=OpeningBalance, 4=MigrationOpenItem
    string? SourceReference,
    Guid? RolloverBatchId,
    Guid AccountId);

public sealed record OpenItemDto(
    Guid Id,
    Guid CompanyId,
    Guid BranchId,
    Guid PartyId,
    string PartyNameArabic,
    byte PartyType,
    Guid? OriginFiscalYearId,
    Guid? OriginDocumentId,
    string? OriginDocumentNumber,
    DateTimeOffset OriginDocumentDateUtc,
    DateTimeOffset? DueDateUtc,
    string CurrencyCode,
    decimal OriginalAmount,
    decimal SettledAmount,
    decimal OutstandingAmount,
    decimal ExchangeRate,
    decimal OriginalBaseCarryingAmount,
    decimal CurrentBaseCarryingAmount,
    byte OriginType,
    string? SourceReference,
    Guid? RolloverBatchId,
    Guid AccountId,
    string AccountCode,
    byte State,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string VersionBase64);

public sealed record PartyBalanceSummaryDto(
    Guid PartyId,
    string PartyNameArabic,
    byte PartyType,
    string CurrencyCode,
    decimal TotalOriginal,
    decimal TotalSettled,
    decimal TotalOutstanding,
    decimal TotalBaseCarrying);

public interface IOpenItemService
{
    Task<OpenItemDto> CreateOpenItemAsync(Guid companyId, CreateOpenItemCommand command, CancellationToken cancellationToken = default);
    Task<OpenItemDto> GetOpenItemAsync(Guid companyId, Guid openItemId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OpenItemDto>> ListOpenItemsAsync(Guid companyId, Guid? branchId, Guid? partyId, byte? partyType, byte? state, string? currencyCode, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PartyBalanceSummaryDto>> GetPartyBalancesAsync(Guid companyId, Guid? branchId, byte? partyType, CancellationToken cancellationToken = default);
}
