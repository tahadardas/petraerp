using System.ComponentModel.DataAnnotations;

namespace PetraERP.Contracts.InterCompany;

public sealed record InterCompanyLinkDto
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public Guid CounterpartyCompanyId { get; init; }
    public string CounterpartyCompanyName { get; init; } = string.Empty;
    public string AccountCode { get; init; } = string.Empty;
    public string CounterpartyAccountCode { get; init; } = string.Empty;
    public string CurrencyCode { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public string? Note { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
}

public sealed record InterCompanyLinkListResponse
{
    public List<InterCompanyLinkDto> Items { get; init; } = [];
}

public sealed record SaveInterCompanyLinkRequest
{
    [Required]
    public Guid CounterpartyCompanyId { get; init; }

    [Required, MaxLength(64)]
    public string AccountCode { get; init; } = string.Empty;

    [Required, MaxLength(64)]
    public string CounterpartyAccountCode { get; init; } = string.Empty;

    [Required, StringLength(3, MinimumLength = 3)]
    public string CurrencyCode { get; init; } = string.Empty;

    [MaxLength(500)]
    public string? Note { get; init; }
}

public sealed record SetInterCompanyLinkActiveRequest
{
    public bool IsActive { get; init; }
}

public sealed record InterCompanyMovementDto
{
    public DateOnly Date { get; init; }
    public Guid JournalId { get; init; }
    public string Description { get; init; } = string.Empty;
    public string Amount { get; init; } = "0.0000";
}

public sealed record InterCompanySideDto
{
    public Guid CompanyId { get; init; }
    public string DatabaseName { get; init; } = string.Empty;
    public bool AccountFound { get; init; }
    /// <summary>Debit-positive decimal string (four decimals) in the link currency.</summary>
    public string Balance { get; init; } = "0.0000";
    public List<InterCompanyMovementDto> RecentMovements { get; init; } = [];
}

public sealed record InterCompanyMatchPairDto
{
    public InterCompanyMovementDto ThisMovement { get; init; } = new();
    public InterCompanyMovementDto CounterpartyMovement { get; init; } = new();
}

/// <summary>Suggested amount-based pairing computed on demand; not a persisted settlement.</summary>
public sealed record InterCompanyMatchingDto
{
    public List<InterCompanyMatchPairDto> Pairs { get; init; } = [];
    public List<InterCompanyMovementDto> UnmatchedThis { get; init; } = [];
    public List<InterCompanyMovementDto> UnmatchedCounterparty { get; init; } = [];
    public bool Truncated { get; init; }
}

public sealed record InterCompanyReconciliationResponse
{
    public Guid LinkId { get; init; }
    public DateOnly AsOfDate { get; init; }
    public string CurrencyCode { get; init; } = string.Empty;
    public InterCompanySideDto ThisSide { get; init; } = new();
    public InterCompanySideDto CounterpartySide { get; init; } = new();
    /// <summary>This side plus the counterparty side; zero means the companies agree.</summary>
    public string Difference { get; init; } = "0.0000";
    public bool Matched { get; init; }
    public string? WarningCode { get; init; }
    public InterCompanyMatchingDto? Matching { get; init; }
}
