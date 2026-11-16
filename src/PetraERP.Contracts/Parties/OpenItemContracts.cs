using System.ComponentModel.DataAnnotations;
using PetraERP.Contracts.Financial;

namespace PetraERP.Contracts.Parties;

public sealed record CreateOpenItemRequest
{
    [Required]
    public required Guid BranchId { get; init; }

    [Required]
    public required Guid PartyId { get; init; }

    [Range(1, 2)]
    public required byte PartyType { get; init; } // 1=Customer, 2=Supplier

    public Guid? OriginFiscalYearId { get; init; }
    public Guid? OriginDocumentId { get; init; }

    [MaxLength(100)]
    public string? OriginDocumentNumber { get; init; }

    public required DateTimeOffset OriginDocumentDateUtc { get; init; }
    public DateTimeOffset? DueDateUtc { get; init; }

    [Required]
    [StringLength(3, MinimumLength = 3)]
    public required string CurrencyCode { get; init; }

    [Required]
    [RegularExpression(FinancialDecimalText.MoneyPattern)]
    public required string OriginalAmount { get; init; }

    [Required]
    [RegularExpression(FinancialDecimalText.RatePattern)]
    public required string ExchangeRate { get; init; }

    [Range(1, 4)]
    public required byte OriginType { get; init; }

    [MaxLength(200)]
    public string? SourceReference { get; init; }

    public Guid? RolloverBatchId { get; init; }

    [Required]
    public required Guid AccountId { get; init; }
}

public sealed record OpenItemResponse
{
    public required Guid Id { get; init; }
    public required Guid CompanyId { get; init; }
    public required Guid BranchId { get; init; }
    public required Guid PartyId { get; init; }
    public required string PartyNameArabic { get; init; }
    public required byte PartyType { get; init; }
    public Guid? OriginFiscalYearId { get; init; }
    public Guid? OriginDocumentId { get; init; }
    public string? OriginDocumentNumber { get; init; }
    public required DateTimeOffset OriginDocumentDateUtc { get; init; }
    public DateTimeOffset? DueDateUtc { get; init; }
    public required string CurrencyCode { get; init; }
    public required string OriginalAmount { get; init; }
    public required string SettledAmount { get; init; }
    public required string OutstandingAmount { get; init; }
    public required string ExchangeRate { get; init; }
    public required string OriginalBaseCarryingAmount { get; init; }
    public required string CurrentBaseCarryingAmount { get; init; }
    public required byte OriginType { get; init; }
    public string? SourceReference { get; init; }
    public Guid? RolloverBatchId { get; init; }
    public required Guid AccountId { get; init; }
    public required string AccountCode { get; init; }
    public required byte State { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required DateTimeOffset UpdatedAtUtc { get; init; }
    public required string VersionBase64 { get; init; }
}

public sealed record PartyBalanceSummaryResponse
{
    public required Guid PartyId { get; init; }
    public required string PartyNameArabic { get; init; }
    public required byte PartyType { get; init; }
    public required string CurrencyCode { get; init; }
    public required string TotalOriginal { get; init; }
    public required string TotalSettled { get; init; }
    public required string TotalOutstanding { get; init; }
    public required string TotalBaseCarrying { get; init; }
}
