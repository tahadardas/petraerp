using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.Calendars;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;
using PetraERP.Domain.AccountingCalendars;
using PetraERP.Domain.Primitives;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.AccountingCalendars;

/// <summary>
/// Fiscal years and period states. Year overlap is prevented under an update lock on the company row; period
/// transitions use the Domain FiscalYear rules and commit together with their audit record.
/// </summary>
public sealed class SqlAccountingCalendarService : IAccountingCalendarService
{
    private readonly FinancialDbContext context;
    private readonly TimeProvider time;

    public SqlAccountingCalendarService(FinancialDbContext context, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(time);
        this.context = context;
        this.time = time;
    }

    public async Task<IReadOnlyList<FiscalYearDto>> ListFiscalYearsAsync(LedgerMasterScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var years = await context.FiscalYears.AsNoTracking().Include(y => y.Periods)
            .Where(y => y.CompanyId == scope.CompanyId).OrderByDescending(y => y.RangeStart)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return years.Select(Map).ToList().AsReadOnly();
    }

    public async Task<FiscalYearDto> GetFiscalYearAsync(LedgerMasterScope scope, Guid fiscalYearId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var year = await context.FiscalYears.AsNoTracking().Include(y => y.Periods)
            .FirstOrDefaultAsync(y => y.Id == fiscalYearId && y.CompanyId == scope.CompanyId, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("fiscal year");
        return Map(year);
    }

    public async Task<FiscalYearDto> CreateFiscalYearAsync(LedgerMasterScope scope, CreateFiscalYearInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(input);
        var company = await LedgerRowMapping.LoadCompanyAsync(context, scope.CompanyId, cancellationToken).ConfigureAwait(false);
        FiscalYear year;
        try
        {
            var range = DateRange.Create(input.Start, input.End);
            var definitions = (input.Periods ?? MonthlyPeriods(input.Start, input.End))
                .Select(p => PeriodDefinition.Create(EntityCode.Create(p.Code), DateRange.Create(p.Start, p.End)));
            year = FiscalYear.Create(company, EntityCode.Create(input.Code), range, definitions);
        }
        catch (ArgumentException exception)
        {
            throw new BusinessRuleException(LedgerMasterErrorCodes.FiscalYearInvalid, exception.Message);
        }

        if (year.Range.End.DayNumber - year.Range.Start.DayNumber > 731)
        {
            throw new BusinessRuleException(LedgerMasterErrorCodes.FiscalYearInvalid, "A fiscal year cannot exceed two calendar years.");
        }

        var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            // Serializes calendar creation per company so two concurrent requests cannot both pass the overlap check.
            _ = await context.Companies.FromSqlInterpolated(
                    $"SELECT * FROM [system].[Companies] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {scope.CompanyId}")
                .AsNoTracking().FirstAsync(cancellationToken).ConfigureAwait(false);
            var overlaps = await context.FiscalYears.AnyAsync(y => y.CompanyId == scope.CompanyId
                && y.RangeStart <= year.Range.End && y.RangeEnd >= year.Range.Start, cancellationToken).ConfigureAwait(false);
            if (overlaps)
            {
                throw new ResourceConflictException(LedgerMasterErrorCodes.FiscalYearOverlap, "The fiscal year overlaps an existing fiscal year.");
            }

            var row = new FiscalYearRow
            {
                Id = year.Id,
                CompanyId = year.CompanyId,
                Code = year.Code.Value,
                RangeStart = year.Range.Start,
                RangeEnd = year.Range.End,
                Periods = year.Periods.Select(p => new AccountingPeriodRow
                {
                    Id = p.Id,
                    CompanyId = p.CompanyId,
                    FiscalYearId = p.FiscalYearId,
                    Code = p.Code.Value,
                    RangeStart = p.Range.Start,
                    RangeEnd = p.Range.End,
                    State = (byte)p.State,
                }).ToList(),
            };
            context.FiscalYears.Add(row);
            try
            {
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
            {
                context.ChangeTracker.Clear();
                throw new ResourceConflictException(LedgerMasterErrorCodes.FiscalYearCodeTaken, "The fiscal year code is already in use.");
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        return await GetFiscalYearAsync(scope, year.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AccountingPeriodDto> TransitionPeriodAsync(LedgerMasterScope scope, Guid periodId, PeriodTransition transition, string reason,
        ConcurrencyToken expectedVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(expectedVersion);
        var company = await LedgerRowMapping.LoadCompanyAsync(context, scope.CompanyId, cancellationToken).ConfigureAwait(false);
        var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            // Same lock order as posting (period row with UPDLOCK) so a close and a posting into that period serialize.
            var period = await context.AccountingPeriods.FromSqlInterpolated(
                    $"SELECT * FROM [ledger].[AccountingPeriods] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {periodId} AND [CompanyId] = {scope.CompanyId}")
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? throw new ResourceNotFoundException("accounting period");
            LedgerRowMapping.VerifyVersion(period.RowVersion, expectedVersion, "accounting period");
            var yearRow = await context.FiscalYears.AsNoTracking().FirstAsync(y => y.Id == period.FiscalYearId, cancellationToken).ConfigureAwait(false);
            var periods = await context.AccountingPeriods.AsNoTracking().Where(p => p.FiscalYearId == yearRow.Id)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            var year = FiscalYear.Reconstitute(yearRow.Id, yearRow.CompanyId, EntityCode.Create(yearRow.Code),
                DateRange.Create(yearRow.RangeStart, yearRow.RangeEnd),
                periods.Select(p => AccountingPeriod.Reconstitute(p.Id, p.CompanyId, p.FiscalYearId, EntityCode.Create(p.Code),
                    DateRange.Create(p.RangeStart, p.RangeEnd), (AccountingPeriodState)p.State)));
            PeriodTransitionContext transitionContext;
            try
            {
                transitionContext = PeriodTransitionContext.Create(scope.ActorUserId, Guid.NewGuid(), reason, time.GetUtcNow());
            }
            catch (ArgumentException)
            {
                throw new RequestValidationException("request.reason_invalid", "A reason is required.", "reason");
            }

            var change = transition switch
            {
                PeriodTransition.SoftClose => year.SoftClosePeriod(company, periodId, transitionContext),
                PeriodTransition.Close => year.ClosePeriod(company, periodId, transitionContext),
                PeriodTransition.Reopen => year.ReopenPeriod(company, periodId, transitionContext),
                _ => throw new RequestValidationException(RuleViolation.PeriodTransitionInvalid, "Unknown transition.", "transition"),
            };

            period.State = (byte)change.Change.NewState;
            context.PeriodAudit.Add(new PeriodAuditRow
            {
                EventId = change.Change.EventId,
                CompanyId = change.Change.CompanyId,
                FiscalYearId = change.Change.FiscalYearId,
                PeriodId = change.Change.PeriodId,
                PreviousState = (byte)change.Change.PreviousState,
                NewState = (byte)change.Change.NewState,
                ActorUserId = change.Change.ActorUserId,
                CorrelationId = change.Change.CorrelationId,
                Reason = change.Change.Reason,
                OccurredAtUtc = change.Change.OccurredAtUtc,
            });
            await LedgerRowMapping.SaveAsync(context, RuleViolation.PeriodTransitionInvalid, "accounting period", cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        var saved = await context.AccountingPeriods.AsNoTracking().FirstAsync(p => p.Id == periodId, cancellationToken).ConfigureAwait(false);
        return Map(saved);
    }

    public async Task<IReadOnlyList<PeriodAuditDto>> GetPeriodAuditAsync(LedgerMasterScope scope, Guid periodId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (!await context.AccountingPeriods.AnyAsync(p => p.Id == periodId && p.CompanyId == scope.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            throw new ResourceNotFoundException("accounting period");
        }

        var rows = await context.PeriodAudit.AsNoTracking().Where(a => a.PeriodId == periodId && a.CompanyId == scope.CompanyId)
            .OrderBy(a => a.OccurredAtUtc).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(a => new PeriodAuditDto
        {
            EventId = a.EventId,
            PreviousState = (PeriodStateCode)a.PreviousState,
            NewState = (PeriodStateCode)a.NewState,
            ActorUserId = a.ActorUserId,
            Reason = a.Reason,
            OccurredAtUtc = a.OccurredAtUtc,
        }).ToList().AsReadOnly();
    }

    internal static IReadOnlyList<PeriodDefinitionInput> MonthlyPeriods(DateOnly start, DateOnly end)
    {
        var result = new List<PeriodDefinitionInput>();
        var current = start;
        while (current <= end && result.Count < 24)
        {
            var monthEnd = new DateOnly(current.Year, current.Month, DateTime.DaysInMonth(current.Year, current.Month));
            var periodEnd = monthEnd < end ? monthEnd : end;
            result.Add(new PeriodDefinitionInput("P" + (result.Count + 1).ToString("D2", CultureInfo.InvariantCulture), current, periodEnd));
            current = periodEnd.AddDays(1);
        }

        return result;
    }

    private static FiscalYearDto Map(FiscalYearRow row) => new()
    {
        Id = row.Id,
        CompanyId = row.CompanyId,
        Code = row.Code,
        RangeStart = row.RangeStart,
        RangeEnd = row.RangeEnd,
        Periods = row.Periods.OrderBy(p => p.RangeStart).Select(Map).ToList().AsReadOnly(),
        VersionBase64 = Convert.ToBase64String(row.RowVersion),
    };

    private static AccountingPeriodDto Map(AccountingPeriodRow row) => new()
    {
        Id = row.Id,
        FiscalYearId = row.FiscalYearId,
        Code = row.Code,
        RangeStart = row.RangeStart,
        RangeEnd = row.RangeEnd,
        State = (PeriodStateCode)row.State,
        VersionBase64 = Convert.ToBase64String(row.RowVersion),
    };
}
