using System.ComponentModel.DataAnnotations;
using PetraERP.Contracts.Financial;

namespace PetraERP.Contracts.Treasury;

public enum CommercialPaperKind : byte
{
    ChequeReceived = 1,
    ChequeIssued = 2,
    PromissoryNote = 3,
}

public enum CommercialPaperStatus : byte
{
    Received = 1,
    UnderCollection = 2,
    Cleared = 3,
    Bounced = 4,
    Cancelled = 5,
    Endorsed = 6,
}

public enum CommercialPaperAlertSeverity : byte
{
    Normal = 0,
    DueSoon = 1,
    DueToday = 2,
    Overdue = 3,
}

public sealed record RegisterCommercialPaperRequest
{
    [Required]
    public CommercialPaperKind PaperKind { get; init; } = CommercialPaperKind.ChequeReceived;

    [Required, MaxLength(64)]
    public string PaperNumber { get; init; } = string.Empty;

    [Required, MaxLength(128)]
    public string BankName { get; init; } = string.Empty;

    [MaxLength(128)]
    public string? BranchName { get; init; }

    [Required, MaxLength(128)]
    public string DrawerName { get; init; } = string.Empty;

    [MaxLength(128)]
    public string? PayeeName { get; init; }

    [Required]
    public DateOnly IssueDate { get; init; }

    [Required]
    public DateOnly DueDate { get; init; }

    [Required, RegularExpression(FinancialDecimalText.MoneyPattern)]
    public string Amount { get; init; } = string.Empty;

    /// <summary>Optional: when omitted the server uses the party, then the company base currency.</summary>
    [StringLength(3, MinimumLength = 3)]
    public string? CurrencyCode { get; init; }

    /// <summary>Optional decimal string: omitted uses 1 for the base currency or the register rate on the issue date.</summary>
    [RegularExpression(FinancialDecimalText.RatePattern)]
    public string? ExchangeRate { get; init; }

    public Guid? PartyId { get; init; }

    public Guid? PortfolioAccountId { get; init; }

    [MaxLength(500)]
    public string? Notes { get; init; }
}

public sealed record TransitionCommercialPaperStatusRequest
{
    [Required]
    public CommercialPaperStatus NewStatus { get; init; }

    public Guid? TargetAccountId { get; init; }

    public Guid? EndorsedPartyId { get; init; }

    public DateOnly TransitionDate { get; init; }

    [MaxLength(500)]
    public string? Reason { get; init; }
}

public sealed record CommercialPaperFilterRequest
{
    public CommercialPaperStatus? Status { get; init; }
    public CommercialPaperKind? Kind { get; init; }
    public Guid? PartyId { get; init; }
    public DateOnly? FromDueDate { get; init; }
    public DateOnly? ToDueDate { get; init; }
    public string? Query { get; init; }
}

public sealed record CommercialPaperDto
{
    public required Guid Id { get; init; }
    public required Guid CompanyId { get; init; }
    public required Guid BranchId { get; init; }
    public required CommercialPaperKind PaperKind { get; init; }
    public required string PaperKindName { get; init; }
    public required string PaperNumber { get; init; }
    public required string BankName { get; init; }
    public required string? BranchName { get; init; }
    public required string DrawerName { get; init; }
    public required string? PayeeName { get; init; }
    public required DateOnly IssueDate { get; init; }
    public required DateOnly DueDate { get; init; }
    public required string Amount { get; init; }
    public required string CurrencyCode { get; init; }
    public required string ExchangeRate { get; init; }
    public required string BaseAmount { get; init; }
    public required CommercialPaperStatus Status { get; init; }
    public required string StatusName { get; init; }
    public required Guid? PartyId { get; init; }
    public required string? PartyName { get; init; }
    public required Guid? IntermediaryAccountId { get; init; }
    public required Guid? BankAccountId { get; init; }
    public required Guid? EndorsedPartyId { get; init; }
    public required string? EndorsedPartyName { get; init; }
    public required Guid? VoucherId { get; init; }
    public required Guid? LastJournalEntryId { get; init; }
    public required DateOnly StatusDate { get; init; }
    public required string? Notes { get; init; }
    public required CommercialPaperAlertSeverity Severity { get; init; }
}

public sealed record CommercialPaperListResponse
{
    public required IReadOnlyList<CommercialPaperDto> Items { get; init; }
    public required string TotalAmount { get; init; }
    public required int TotalCount { get; init; }
}

public sealed record CommercialPaperAlertsResponse
{
    public required int OverdueCount { get; init; }
    public required int DueTodayCount { get; init; }
    public required int DueSoonCount { get; init; }
    public required IReadOnlyList<CommercialPaperDto> Alerts { get; init; }
}
