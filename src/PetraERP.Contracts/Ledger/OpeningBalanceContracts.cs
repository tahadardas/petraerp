using System.ComponentModel.DataAnnotations;
using PetraERP.Contracts.Financial;

namespace PetraERP.Contracts.Ledger;

public sealed record OpeningBalanceLineInputDto
{
    [Required]
    public required Guid AccountId { get; init; }

    public Guid? CostCenterId { get; init; }

    [Required]
    [StringLength(3, MinimumLength = 3)]
    public required string CurrencyCode { get; init; }

    [Required]
    [RegularExpression(FinancialDecimalText.MoneyPattern)]
    public required string DebitOriginal { get; init; }

    [Required]
    [RegularExpression(FinancialDecimalText.MoneyPattern)]
    public required string CreditOriginal { get; init; }

    [Required]
    [RegularExpression(FinancialDecimalText.RatePattern)]
    public required string ExchangeRate { get; init; }

    [Required]
    [RegularExpression(FinancialDecimalText.MoneyPattern)]
    public required string DebitBase { get; init; }

    [Required]
    [RegularExpression(FinancialDecimalText.MoneyPattern)]
    public required string CreditBase { get; init; }

    [MaxLength(1000)]
    public string? Description { get; init; }

    public int LineOrder { get; init; }
}

public sealed record CreateOpeningBalanceBatchRequest
{
    [Required]
    public required DateOnly OpeningDate { get; init; }

    [Required]
    [MaxLength(50)]
    public required string SourceType { get; init; }

    [MaxLength(200)]
    public string? SourceReference { get; init; }

    [MaxLength(128)]
    public string? SourceFingerprint { get; init; }

    [Required]
    [StringLength(3, MinimumLength = 3)]
    public required string OriginalCurrency { get; init; }

    [Required]
    [StringLength(3, MinimumLength = 3)]
    public required string BaseCurrency { get; init; }

    public Guid? BranchId { get; init; }

    [Required]
    [MaxLength(128)]
    public required string IdempotencyKey { get; init; }

    [Required]
    public required IReadOnlyList<OpeningBalanceLineInputDto> Lines { get; init; }
}

public sealed record OpeningBalanceLineResponse
{
    public required Guid Id { get; init; }
    public required Guid BatchId { get; init; }
    public required Guid AccountId { get; init; }
    public required string AccountCode { get; init; }
    public required string AccountNameArabic { get; init; }
    public Guid? CostCenterId { get; init; }
    public string? CostCenterCode { get; init; }
    public required string CurrencyCode { get; init; }
    public required string DebitOriginal { get; init; }
    public required string CreditOriginal { get; init; }
    public required string ExchangeRate { get; init; }
    public required string DebitBase { get; init; }
    public required string CreditBase { get; init; }
    public string? Description { get; init; }
    public required int LineOrder { get; init; }
}

public sealed record OpeningBalanceBatchResponse
{
    public required Guid Id { get; init; }
    public required Guid CompanyId { get; init; }
    public required Guid FiscalYearId { get; init; }
    public Guid? BranchId { get; init; }
    public required DateOnly OpeningDate { get; init; }
    public required string SourceType { get; init; }
    public string? SourceReference { get; init; }
    public string? SourceFingerprint { get; init; }
    public required string OriginalCurrency { get; init; }
    public required string BaseCurrency { get; init; }
    public required byte ApprovalState { get; init; }
    public required Guid ActorId { get; init; }
    public Guid? ApprovedByActorId { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required DateTimeOffset UpdatedAtUtc { get; init; }
    public DateTimeOffset? ApprovedAtUtc { get; init; }
    public Guid? PostedJournalId { get; init; }
    public Guid? ReversalJournalId { get; init; }
    public required string IdempotencyKey { get; init; }
    public required string TotalDebitBase { get; init; }
    public required string TotalCreditBase { get; init; }
    public required bool IsBalanced { get; init; }
    public required string VersionBase64 { get; init; }
    public required IReadOnlyList<OpeningBalanceLineResponse> Lines { get; init; }
}

public sealed record ApproveOpeningBalanceRequest
{
    [Required]
    [MaxLength(500)]
    public required string Reason { get; init; }
}

public sealed record PostOpeningBalanceRequest
{
    [Required]
    [MaxLength(500)]
    public required string Reason { get; init; }
}

public sealed record ReverseOpeningBalanceRequest
{
    [Required]
    [MaxLength(500)]
    public required string Reason { get; init; }
}
