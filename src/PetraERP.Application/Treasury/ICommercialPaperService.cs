using PetraERP.Domain.Primitives;

namespace PetraERP.Application.Treasury;

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

public sealed record RegisterCommercialPaperCommand
{
    public CommercialPaperKind PaperKind { get; init; } = CommercialPaperKind.ChequeReceived;
    public string PaperNumber { get; init; } = string.Empty;
    public string BankName { get; init; } = string.Empty;
    public string? BranchName { get; init; }
    public string DrawerName { get; init; } = string.Empty;
    public string? PayeeName { get; init; }
    public DateOnly IssueDate { get; init; }
    public DateOnly DueDate { get; init; }
    public decimal Amount { get; init; }
    /// <summary>Null resolves on the server (party, then company base); the rate actually used is stored on the paper.</summary>
    public string? CurrencyCode { get; init; }
    public decimal? ExchangeRate { get; init; }
    public Guid? PartyId { get; init; }
    public Guid? PortfolioAccountId { get; init; }
    public string? Notes { get; init; }
}

public sealed record TransitionCommercialPaperStatusCommand
{
    public CommercialPaperStatus NewStatus { get; init; }
    public Guid? TargetAccountId { get; init; }
    public Guid? EndorsedPartyId { get; init; }
    public DateOnly TransitionDate { get; init; }
    public string? Reason { get; init; }
}

public sealed record CommercialPaperSearchQuery
{
    public CommercialPaperStatus? Status { get; init; }
    public CommercialPaperKind? Kind { get; init; }
    public Guid? PartyId { get; init; }
    public DateOnly? FromDueDate { get; init; }
    public DateOnly? ToDueDate { get; init; }
    public string? Query { get; init; }
}

public sealed record CommercialPaperResult
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public Guid BranchId { get; init; }
    public CommercialPaperKind PaperKind { get; init; }
    public string PaperNumber { get; init; } = string.Empty;
    public string BankName { get; init; } = string.Empty;
    public string? BranchName { get; init; }
    public string DrawerName { get; init; } = string.Empty;
    public string? PayeeName { get; init; }
    public DateOnly IssueDate { get; init; }
    public DateOnly DueDate { get; init; }
    public decimal Amount { get; init; }
    public string CurrencyCode { get; init; } = string.Empty;
    public decimal ExchangeRate { get; init; }
    public decimal BaseAmount { get; init; }
    public CommercialPaperStatus Status { get; init; }
    public Guid? PartyId { get; init; }
    public string? PartyName { get; init; }
    public Guid? IntermediaryAccountId { get; init; }
    public Guid? BankAccountId { get; init; }
    public Guid? EndorsedPartyId { get; init; }
    public string? EndorsedPartyName { get; init; }
    public Guid? VoucherId { get; init; }
    public Guid? LastJournalEntryId { get; init; }
    public DateOnly StatusDate { get; init; }
    public string? Notes { get; init; }
    public CommercialPaperAlertSeverity Severity { get; init; }
}

public sealed record CommercialPaperAlertsResult
{
    public int OverdueCount { get; init; }
    public int DueTodayCount { get; init; }
    public int DueSoonCount { get; init; }
    public IReadOnlyList<CommercialPaperResult> Alerts { get; init; } = [];
}

public interface ICommercialPaperService
{
    Task<CommercialPaperResult> RegisterAsync(Guid companyId, Guid branchId, Guid userId, RegisterCommercialPaperCommand command, CancellationToken cancellationToken = default);
    Task<CommercialPaperResult> TransitionStatusAsync(Guid companyId, Guid branchId, Guid userId, Guid paperId, TransitionCommercialPaperStatusCommand command, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CommercialPaperResult>> ListAsync(Guid companyId, Guid branchId, CommercialPaperSearchQuery query, CancellationToken cancellationToken = default);
    Task<CommercialPaperAlertsResult> GetAlertsAsync(Guid companyId, Guid branchId, int daysAhead = 7, CancellationToken cancellationToken = default);
    Task<CommercialPaperResult> GetByIdAsync(Guid companyId, Guid branchId, Guid paperId, CancellationToken cancellationToken = default);
}
