using System.ComponentModel.DataAnnotations;

namespace PetraERP.Contracts.Currencies;

public sealed record FxAccountRevaluationItemDto
{
    public required Guid AccountId { get; init; }
    public required string AccountCode { get; init; }
    public required string AccountNameArabic { get; init; }
    public required string CurrencyCode { get; init; }
    public required string ForeignBalance { get; init; }
    public required string BookBaseBalance { get; init; }
    public required string ClosingRate { get; init; }
    public required string RevaluedBaseBalance { get; init; }
    public required string UnrealizedDifference { get; init; }
    public required bool IsGain { get; init; }
}

public sealed record FxRevaluationPreviewRequest
{
    [Required]
    public DateOnly AsOfDate { get; init; } = DateOnly.FromDateTime(DateTime.Today);

    public string? CurrencyCode { get; init; }

    public string? OverrideClosingRate { get; init; }
}

public sealed record FxRevaluationPreviewResponse
{
    public required DateOnly AsOfDate { get; init; }
    public required string BaseCurrencyCode { get; init; }
    public required IReadOnlyList<FxAccountRevaluationItemDto> Items { get; init; }
    public required string TotalUnrealizedGains { get; init; }
    public required string TotalUnrealizedLosses { get; init; }
    public required string NetDifference { get; init; }
}

public sealed record ExecuteFxRevaluationRequest
{
    [Required]
    public DateOnly AsOfDate { get; init; } = DateOnly.FromDateTime(DateTime.Today);

    [Required]
    public Guid GainAccountId { get; init; }

    [Required]
    public Guid LossAccountId { get; init; }

    [Required, MaxLength(128)]
    public string IdempotencyKey { get; init; } = string.Empty;

    public string? CurrencyCode { get; init; }

    public string? OverrideClosingRate { get; init; }

    public string? Description { get; init; }

    public IReadOnlyList<Guid>? SelectedAccountIds { get; init; }
}

public sealed record FxRateAuditRequest
{
    [Required]
    public DateOnly From { get; init; } = new(DateTime.Today.Year, 1, 1);

    [Required]
    public DateOnly To { get; init; } = DateOnly.FromDateTime(DateTime.Today);

    public string? CurrencyCode { get; init; }
}

public sealed record FxRateAuditItemDto
{
    public required Guid JournalId { get; init; }
    public long? PostingSequence { get; init; }
    public required DateOnly AccountingDate { get; init; }
    public required Guid AccountId { get; init; }
    public required string AccountCode { get; init; }
    public required string AccountNameArabic { get; init; }
    public required string Description { get; init; }
    public required string CurrencyCode { get; init; }
    public required string ForeignAmount { get; init; }
    public required string BookedRate { get; init; }
    public required string BulletinRate { get; init; }
    public required string BookedBase { get; init; }
    public required string BulletinBase { get; init; }
    public required string Difference { get; init; }
}

public sealed record FxRateAuditResponse
{
    public required DateOnly From { get; init; }
    public required DateOnly To { get; init; }
    public required string BaseCurrencyCode { get; init; }
    public required int LinesChecked { get; init; }
    public required int LinesWithoutBulletinRate { get; init; }
    public required IReadOnlyList<FxRateAuditItemDto> Items { get; init; }
    public required string TotalDifference { get; init; }
}

public sealed record FxRevaluationExecutionResponse
{
    public required Guid JournalEntryId { get; init; }
    public required string JournalEntryNumber { get; init; }
    public required int RevaluedAccountsCount { get; init; }
    public required string TotalGainAmount { get; init; }
    public required string TotalLossAmount { get; init; }
    public required string NetRevaluationAmount { get; init; }
}
