using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;

namespace PetraERP.Application.GeneralLedger.Calendars;

public enum PeriodStateCode
{
    Open = 0,
    SoftClosed = 1,
    Closed = 2,
}

public sealed record PeriodDefinitionInput(string Code, DateOnly Start, DateOnly End);

/// <summary>When <see cref="Periods"/> is null, calendar-month periods P01..Pnn are generated to cover the range.</summary>
public sealed record CreateFiscalYearInput(string Code, DateOnly Start, DateOnly End, IReadOnlyList<PeriodDefinitionInput>? Periods);

public sealed record AccountingPeriodDto
{
    public required Guid Id { get; init; }
    public required Guid FiscalYearId { get; init; }
    public required string Code { get; init; }
    public required DateOnly RangeStart { get; init; }
    public required DateOnly RangeEnd { get; init; }
    public required PeriodStateCode State { get; init; }
    public required string VersionBase64 { get; init; }
}

public sealed record FiscalYearDto
{
    public required Guid Id { get; init; }
    public required Guid CompanyId { get; init; }
    public required string Code { get; init; }
    public required DateOnly RangeStart { get; init; }
    public required DateOnly RangeEnd { get; init; }
    public required IReadOnlyList<AccountingPeriodDto> Periods { get; init; }
    public required string VersionBase64 { get; init; }
}

public sealed record PeriodAuditDto
{
    public required Guid EventId { get; init; }
    public required PeriodStateCode PreviousState { get; init; }
    public required PeriodStateCode NewState { get; init; }
    public required Guid ActorUserId { get; init; }
    public required string Reason { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }
}

public enum PeriodTransition
{
    SoftClose,
    Close,
    Reopen,
}

public interface IAccountingCalendarService
{
    Task<IReadOnlyList<FiscalYearDto>> ListFiscalYearsAsync(LedgerMasterScope scope, CancellationToken cancellationToken);
    Task<FiscalYearDto> GetFiscalYearAsync(LedgerMasterScope scope, Guid fiscalYearId, CancellationToken cancellationToken);
    Task<FiscalYearDto> CreateFiscalYearAsync(LedgerMasterScope scope, CreateFiscalYearInput input, CancellationToken cancellationToken);

    /// <summary>Applies a Domain period transition and its audit record in one transaction. Authorization is per transition.</summary>
    Task<AccountingPeriodDto> TransitionPeriodAsync(LedgerMasterScope scope, Guid periodId, PeriodTransition transition, string reason,
        ConcurrencyToken expectedVersion, CancellationToken cancellationToken);

    Task<IReadOnlyList<PeriodAuditDto>> GetPeriodAuditAsync(LedgerMasterScope scope, Guid periodId, CancellationToken cancellationToken);
}
