namespace PetraERP.Application.GeneralLedger.OpeningBalances;

public sealed record OpeningBalanceLineInput(
    Guid AccountId,
    Guid? CostCenterId,
    string CurrencyCode,
    decimal DebitOriginal,
    decimal CreditOriginal,
    decimal ExchangeRate,
    decimal DebitBase,
    decimal CreditBase,
    string? Description,
    int LineOrder);

public sealed record CreateOpeningBalanceBatchCommand(
    DateOnly OpeningDate,
    string SourceType,
    string? SourceReference,
    string? SourceFingerprint,
    string OriginalCurrency,
    string BaseCurrency,
    Guid? BranchId,
    string IdempotencyKey,
    IReadOnlyList<OpeningBalanceLineInput> Lines);

public sealed record OpeningBalanceBatchDto(
    Guid Id,
    Guid CompanyId,
    Guid FiscalYearId,
    Guid? BranchId,
    DateOnly OpeningDate,
    string SourceType,
    string? SourceReference,
    string? SourceFingerprint,
    string OriginalCurrency,
    string BaseCurrency,
    byte ApprovalState,
    Guid ActorId,
    Guid? ApprovedByActorId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? ApprovedAtUtc,
    Guid? PostedJournalId,
    Guid? ReversalJournalId,
    string IdempotencyKey,
    decimal TotalDebitBase,
    decimal TotalCreditBase,
    bool IsBalanced,
    string VersionBase64,
    IReadOnlyList<OpeningBalanceLineDto> Lines);

public sealed record OpeningBalanceLineDto(
    Guid Id,
    Guid BatchId,
    Guid AccountId,
    string AccountCode,
    string AccountNameArabic,
    Guid? CostCenterId,
    string? CostCenterCode,
    string CurrencyCode,
    decimal DebitOriginal,
    decimal CreditOriginal,
    decimal ExchangeRate,
    decimal DebitBase,
    decimal CreditBase,
    string? Description,
    int LineOrder);

public interface IOpeningBalanceService
{
    Task<OpeningBalanceBatchDto> CreateDraftAsync(Guid companyId, Guid fiscalYearId, Guid actorId, CreateOpeningBalanceBatchCommand command, CancellationToken cancellationToken = default);
    Task<OpeningBalanceBatchDto> ValidateAsync(Guid companyId, Guid batchId, CancellationToken cancellationToken = default);
    Task<OpeningBalanceBatchDto> ApproveAsync(Guid companyId, Guid batchId, Guid actorId, string reason, CancellationToken cancellationToken = default);
    Task<OpeningBalanceBatchDto> PostAsync(Guid companyId, Guid batchId, Guid actorId, string reason, CancellationToken cancellationToken = default);
    Task<OpeningBalanceBatchDto> ReverseAsync(Guid companyId, Guid batchId, Guid actorId, string reason, CancellationToken cancellationToken = default);
    Task<OpeningBalanceBatchDto> GetAsync(Guid companyId, Guid batchId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OpeningBalanceBatchDto>> ListAsync(Guid companyId, Guid fiscalYearId, CancellationToken cancellationToken = default);
}
