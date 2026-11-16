using System.ComponentModel.DataAnnotations;

namespace PetraERP.Contracts.Ledger;

public enum AccountingPeriodState { Open = 0, SoftClosed = 1, Closed = 2 }

public sealed record PeriodDefinitionRequest
{
    [Required]
    [StringLength(64, MinimumLength = 1)]
    public required string Code { get; init; }

    [Required]
    public required DateOnly Start { get; init; }

    [Required]
    public required DateOnly End { get; init; }
}

public sealed record CreateFiscalYearRequest
{
    [Required]
    [StringLength(64, MinimumLength = 1)]
    public required string Code { get; init; }

    [Required]
    public required DateOnly Start { get; init; }

    [Required]
    public required DateOnly End { get; init; }

    /// <summary>Optional explicit periods; when omitted, calendar-month periods P01.. cover the year.</summary>
    [MaxLength(24)]
    public IReadOnlyList<PeriodDefinitionRequest>? Periods { get; init; }
}

public sealed record PeriodTransitionRequest
{
    [Required]
    [StringLength(12, MinimumLength = 12)]
    public required string ExpectedVersion { get; init; }

    [Required]
    [StringLength(500, MinimumLength = 1)]
    public required string Reason { get; init; }
}

public sealed record AccountingPeriodResponse
{
    public required Guid Id { get; init; }
    public required Guid FiscalYearId { get; init; }
    public required string Code { get; init; }
    public required DateOnly RangeStart { get; init; }
    public required DateOnly RangeEnd { get; init; }
    public required AccountingPeriodState State { get; init; }
    public required string Version { get; init; }
}

public sealed record FiscalYearResponse
{
    public required Guid Id { get; init; }
    public required Guid CompanyId { get; init; }
    public required string Code { get; init; }
    public required DateOnly RangeStart { get; init; }
    public required DateOnly RangeEnd { get; init; }
    public required IReadOnlyList<AccountingPeriodResponse> Periods { get; init; }
    public required string Version { get; init; }
}

public sealed record PeriodAuditResponse
{
    public required Guid EventId { get; init; }
    public required AccountingPeriodState PreviousState { get; init; }
    public required AccountingPeriodState NewState { get; init; }
    public required Guid ActorUserId { get; init; }
    public required string Reason { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }
}

public sealed record CurrencyResponse
{
    public required string Code { get; init; }
    public required string NameArabic { get; init; }
    public string? NameEnglish { get; init; }
}

public sealed record CompanyCurrencyContextResponse
{
    public required Guid CompanyId { get; init; }
    public required CurrencyResponse BaseCurrency { get; init; }
    public required IReadOnlyList<CurrencyResponse> AdditionalCurrencies { get; init; }
}
