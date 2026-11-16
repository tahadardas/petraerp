using System.Collections.ObjectModel;
using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger;
using PetraERP.Domain.AccountingCalendars;
using PetraERP.Domain.FinancialValues;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Domain.Organizations;
using PetraERP.Domain.Primitives;
using PetraERP.Infrastructure.AccountingCalendars;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.GeneralLedger;

public sealed class SqlJournalEntryService : IJournalEntryService
{
    private readonly FinancialDbContext context;
    private readonly TimeProvider time;

    public SqlJournalEntryService(FinancialDbContext context, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(time);
        this.context = context;
        this.time = time;
    }

    public async Task<JournalDetailsDto> CreateDraftAsync(
        JournalEntryScope scope,
        CreateJournalDraftCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(command);

        var transaction = await PetraERP.Infrastructure.Currencies.CurrencyWriteTransaction.BeginAsync(context, scope.CompanyId, cancellationToken).ConfigureAwait(false);
        await using var currencyLifetime = transaction.ConfigureAwait(false);
        var (companyDomain, branchScope) = await LoadScopeAsync(scope, cancellationToken).ConfigureAwait(false);
        var now = time.GetUtcNow();
        var actionContext = JournalActionContext.Create(scope.ActorUserId, Guid.NewGuid(), "Created", now);

        var domainLines = BuildDomainLines(companyDomain, command.Lines);
        var entry = JournalEntry.CreateManualDraft(branchScope, command.AccountingDate, command.Description, domainLines, actionContext);

        {
            var journalRow = new JournalEntryRow
            {
                Id = entry.Id,
                CompanyId = entry.CompanyId,
                BranchId = entry.BranchId,
                AccountingDate = entry.AccountingDate,
                Description = entry.Description,
                State = (byte)entry.State,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };
            context.JournalEntries.Add(journalRow);

            foreach (var line in entry.Lines)
            {
                context.JournalLines.Add(MapLineRow(line, entry.Id, now));
            }

            context.JournalAudit.Add(new JournalAuditRow
            {
                EventId = Guid.NewGuid(),
                CompanyId = entry.CompanyId,
                BranchId = entry.BranchId,
                JournalId = entry.Id,
                Revision = 1,
                Action = (byte)JournalAuditAction.Created,
                PreviousState = null,
                NewState = (byte)entry.State,
                ActorUserId = scope.ActorUserId,
                CorrelationId = actionContext.CorrelationId,
                Reason = actionContext.Reason,
                OccurredAtUtc = actionContext.OccurredAtUtc,
                CreatedAtUtc = now,
            });

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        return await GetJournalAsync(scope, entry.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<JournalDetailsDto> ReviseDraftAsync(
        JournalEntryScope scope,
        Guid journalId,
        ReviseJournalDraftCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(command);

        var (companyDomain, _) = await LoadScopeAsync(scope, cancellationToken).ConfigureAwait(false);
        var now = time.GetUtcNow();
        var actionContext = JournalActionContext.Create(scope.ActorUserId, Guid.NewGuid(), command.Reason, now);

        var transaction = await OwnedDatabaseTransaction.BeginAsync(context, IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            var journalRow = await context.JournalEntries
                .FromSqlInterpolated($"SELECT * FROM [ledger].[JournalEntries] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {journalId}")
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false)
                ?? throw new ResourceNotFoundException("journal");

            VerifyOwnership(journalRow, scope);
            VerifyVersion(journalRow, command.ExpectedVersion);

            if (journalRow.State != (byte)JournalEntryState.Draft)
            {
                throw RuleViolation.Create(RuleViolation.JournalStateInvalid, $"Only draft journals can be revised. Current state is {(JournalEntryState)journalRow.State}.");
            }

            if (journalRow.ReversesEntryId is not null)
            {
                throw RuleViolation.Create(RuleViolation.JournalStateInvalid, "Reversal draft content cannot be revised.");
            }

            var domainLines = BuildDomainLines(companyDomain, command.Lines);
            var existingAudits = await context.JournalAudit
                .Where(a => a.JournalId == journalId)
                .OrderBy(a => a.Revision)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var existingDomainAudits = existingAudits.Select(a => JournalAuditEvent.Reconstitute(
                a.EventId, a.CompanyId, a.BranchId, a.JournalId, a.Revision,
                (JournalAuditAction)a.Action,
                a.PreviousState.HasValue ? (JournalEntryState)a.PreviousState.Value : null,
                (JournalEntryState)a.NewState,
                JournalActionContext.Create(a.ActorUserId, a.CorrelationId, a.Reason, a.OccurredAtUtc),
                a.RelatedEntryId)).ToList();

            var existingLines = await context.JournalLines.Where(l => l.JournalId == journalId).ToListAsync(cancellationToken).ConfigureAwait(false);
            context.JournalLines.RemoveRange(existingLines);

            var existingDomain = JournalEntry.Reconstitute(
                journalRow.Id, journalRow.CompanyId, journalRow.BranchId,
                journalRow.AccountingDate, journalRow.Description,
                [], (JournalEntryState)journalRow.State,
                existingDomainAudits, null, null, null, null, null);

            var revised = existingDomain.Revise(command.AccountingDate, command.Description, domainLines, actionContext);

            journalRow.AccountingDate = revised.AccountingDate;
            journalRow.Description = revised.Description;
            journalRow.UpdatedAtUtc = now;

            foreach (var line in revised.Lines)
            {
                context.JournalLines.Add(MapLineRow(line, journalId, now));
            }

            var nextRevision = existingAudits.Count > 0 ? existingAudits.Max(a => a.Revision) + 1 : 1;
            context.JournalAudit.Add(new JournalAuditRow
            {
                EventId = Guid.NewGuid(),
                CompanyId = journalRow.CompanyId,
                BranchId = journalRow.BranchId,
                JournalId = journalId,
                Revision = nextRevision,
                Action = (byte)JournalAuditAction.Revised,
                PreviousState = (byte)JournalEntryState.Draft,
                NewState = (byte)JournalEntryState.Draft,
                ActorUserId = scope.ActorUserId,
                CorrelationId = actionContext.CorrelationId,
                Reason = actionContext.Reason,
                OccurredAtUtc = actionContext.OccurredAtUtc,
                CreatedAtUtc = now,
            });

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        return await GetJournalAsync(scope, journalId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<JournalDetailsDto> ValidateDraftAsync(
        JournalEntryScope scope,
        Guid journalId,
        JournalDraftActionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(command);

        var (companyDomain, branchScope) = await LoadScopeAsync(scope, cancellationToken).ConfigureAwait(false);
        var now = time.GetUtcNow();
        var actionContext = JournalActionContext.Create(scope.ActorUserId, Guid.NewGuid(), command.Reason, now);

        var transaction = await OwnedDatabaseTransaction.BeginAsync(context, IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            var journalRow = await context.JournalEntries
                .FromSqlInterpolated($"SELECT * FROM [ledger].[JournalEntries] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {journalId}")
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false)
                ?? throw new ResourceNotFoundException("journal");

            VerifyOwnership(journalRow, scope);
            VerifyVersion(journalRow, command.ExpectedVersion);

            if (journalRow.State != (byte)JournalEntryState.Draft)
            {
                throw RuleViolation.Create(RuleViolation.JournalStateInvalid, $"Only draft journals can be validated. Current state is {(JournalEntryState)journalRow.State}.");
            }

            var lines = await context.JournalLines
                .Where(l => l.JournalId == journalId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (lines.Count == 0)
            {
                throw RuleViolation.Create(RuleViolation.JournalUnbalanced, "Journal entry has no lines to validate.");
            }

            var validationContext = await BuildValidationContextAsync(companyDomain, branchScope, journalRow.AccountingDate, lines, cancellationToken).ConfigureAwait(false);

            var existingAudits = await context.JournalAudit
                .Where(a => a.JournalId == journalId)
                .OrderBy(a => a.Revision)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var domain = BuildDomainEntry(journalRow, lines, existingAudits);
            var validated = domain.Validate(validationContext, actionContext);

            journalRow.State = (byte)validated.State;
            journalRow.UpdatedAtUtc = now;

            var nextRevision = existingAudits.Count > 0 ? existingAudits.Max(a => a.Revision) + 1 : 1;
            context.JournalAudit.Add(new JournalAuditRow
            {
                EventId = Guid.NewGuid(),
                CompanyId = journalRow.CompanyId,
                BranchId = journalRow.BranchId,
                JournalId = journalId,
                Revision = nextRevision,
                Action = (byte)JournalAuditAction.Validated,
                PreviousState = (byte)JournalEntryState.Draft,
                NewState = (byte)JournalEntryState.Validated,
                ActorUserId = scope.ActorUserId,
                CorrelationId = actionContext.CorrelationId,
                Reason = actionContext.Reason,
                OccurredAtUtc = actionContext.OccurredAtUtc,
                CreatedAtUtc = now,
            });

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        return await GetJournalAsync(scope, journalId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<JournalDetailsDto> ApproveDraftAsync(
        JournalEntryScope scope,
        Guid journalId,
        JournalDraftActionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(command);

        var (_, _) = await LoadScopeAsync(scope, cancellationToken).ConfigureAwait(false);
        var now = time.GetUtcNow();
        var actionContext = JournalActionContext.Create(scope.ActorUserId, Guid.NewGuid(), command.Reason, now);

        var transaction = await OwnedDatabaseTransaction.BeginAsync(context, IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            var journalRow = await context.JournalEntries
                .FromSqlInterpolated($"SELECT * FROM [ledger].[JournalEntries] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {journalId}")
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false)
                ?? throw new ResourceNotFoundException("journal");

            VerifyOwnership(journalRow, scope);
            VerifyVersion(journalRow, command.ExpectedVersion);

            if (journalRow.State != (byte)JournalEntryState.Validated)
            {
                throw RuleViolation.Create(RuleViolation.JournalStateInvalid, $"Only validated journals can be approved. Current state is {(JournalEntryState)journalRow.State}.");
            }

            var lines = await context.JournalLines
                .Where(l => l.JournalId == journalId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var existingAudits = await context.JournalAudit
                .Where(a => a.JournalId == journalId)
                .OrderBy(a => a.Revision)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var domain = BuildDomainEntry(journalRow, lines, existingAudits);
            var approved = domain.Approve(actionContext);

            journalRow.State = (byte)approved.State;
            journalRow.UpdatedAtUtc = now;

            var nextRevision = existingAudits.Count > 0 ? existingAudits.Max(a => a.Revision) + 1 : 1;
            context.JournalAudit.Add(new JournalAuditRow
            {
                EventId = Guid.NewGuid(),
                CompanyId = journalRow.CompanyId,
                BranchId = journalRow.BranchId,
                JournalId = journalId,
                Revision = nextRevision,
                Action = (byte)JournalAuditAction.Approved,
                PreviousState = (byte)JournalEntryState.Validated,
                NewState = (byte)JournalEntryState.Approved,
                ActorUserId = scope.ActorUserId,
                CorrelationId = actionContext.CorrelationId,
                Reason = actionContext.Reason,
                OccurredAtUtc = actionContext.OccurredAtUtc,
                CreatedAtUtc = now,
            });

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        return await GetJournalAsync(scope, journalId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<JournalDetailsDto> ReturnToDraftAsync(
        JournalEntryScope scope,
        Guid journalId,
        JournalDraftActionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(command);

        var (_, _) = await LoadScopeAsync(scope, cancellationToken).ConfigureAwait(false);
        var now = time.GetUtcNow();
        var actionContext = JournalActionContext.Create(scope.ActorUserId, Guid.NewGuid(), command.Reason, now);

        var transaction = await OwnedDatabaseTransaction.BeginAsync(context, IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            var journalRow = await context.JournalEntries
                .FromSqlInterpolated($"SELECT * FROM [ledger].[JournalEntries] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {journalId}")
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false)
                ?? throw new ResourceNotFoundException("journal");

            VerifyOwnership(journalRow, scope);
            VerifyVersion(journalRow, command.ExpectedVersion);

            if (journalRow.State is not ((byte)JournalEntryState.Validated or (byte)JournalEntryState.Approved))
            {
                throw RuleViolation.Create(RuleViolation.JournalStateInvalid, $"Only validated or approved journals may return to Draft. Current state is {(JournalEntryState)journalRow.State}.");
            }

            var lines = await context.JournalLines
                .Where(l => l.JournalId == journalId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var existingAudits = await context.JournalAudit
                .Where(a => a.JournalId == journalId)
                .OrderBy(a => a.Revision)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var domain = BuildDomainEntry(journalRow, lines, existingAudits);
            var previousState = (JournalEntryState)journalRow.State;
            var draft = domain.ReturnToDraft(actionContext);

            journalRow.State = (byte)draft.State;
            journalRow.UpdatedAtUtc = now;

            var nextRevision = existingAudits.Count > 0 ? existingAudits.Max(a => a.Revision) + 1 : 1;
            context.JournalAudit.Add(new JournalAuditRow
            {
                EventId = Guid.NewGuid(),
                CompanyId = journalRow.CompanyId,
                BranchId = journalRow.BranchId,
                JournalId = journalId,
                Revision = nextRevision,
                Action = (byte)JournalAuditAction.ReturnedToDraft,
                PreviousState = (byte)previousState,
                NewState = (byte)JournalEntryState.Draft,
                ActorUserId = scope.ActorUserId,
                CorrelationId = actionContext.CorrelationId,
                Reason = actionContext.Reason,
                OccurredAtUtc = actionContext.OccurredAtUtc,
                CreatedAtUtc = now,
            });

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        return await GetJournalAsync(scope, journalId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<JournalDetailsDto> CreateReversalDraftAsync(
        JournalEntryScope scope,
        CreateReversalDraftCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(command);

        var (companyDomain, branchScope) = await LoadScopeAsync(scope, cancellationToken).ConfigureAwait(false);
        var now = time.GetUtcNow();
        var actionContext = JournalActionContext.Create(scope.ActorUserId, Guid.NewGuid(), command.Reason, now);

        var transaction = await OwnedDatabaseTransaction.BeginAsync(context, IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            var originalRow = await context.JournalEntries
                .FromSqlInterpolated($"SELECT * FROM [ledger].[JournalEntries] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {command.OriginalJournalId}")
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false)
                ?? throw new ResourceNotFoundException("original journal");

            VerifyOwnership(originalRow, scope);
            VerifyVersion(originalRow, command.OriginalExpectedVersion);

            if (originalRow.State != (byte)JournalEntryState.Posted)
            {
                throw RuleViolation.Create(RuleViolation.JournalStateInvalid, $"Original journal must be in Posted state to reverse. Current state is {(JournalEntryState)originalRow.State}.");
            }

            if (originalRow.ReversedByEntryId is not null)
            {
                throw RuleViolation.Create(RuleViolation.PostingConflict, $"Original journal '{originalRow.Id}' has already been reversed by '{originalRow.ReversedByEntryId}'.");
            }

            var existingDraft = await context.JournalEntries
                .AsNoTracking()
                .FirstOrDefaultAsync(j => j.CompanyId == scope.CompanyId && j.ReversesEntryId == command.OriginalJournalId && j.State != (byte)JournalEntryState.Reversed, cancellationToken)
                .ConfigureAwait(false);

            if (existingDraft is not null)
            {
                throw RuleViolation.Create(RuleViolation.PostingConflict, $"A reversal draft ('{existingDraft.Id}') already exists for journal '{command.OriginalJournalId}'.");
            }

            var originalLines = await context.JournalLines
                .Where(l => l.JournalId == originalRow.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var originalAudits = await context.JournalAudit
                .Where(a => a.JournalId == originalRow.Id)
                .OrderBy(a => a.Revision)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var originalDomain = BuildDomainEntry(originalRow, originalLines, originalAudits);
            var reversalDescription = $"Reversal of journal {(originalRow.PostingSequence.HasValue ? originalRow.PostingSequence.Value.ToString(CultureInfo.InvariantCulture) : originalRow.Id.ToString())}";
            var reversalDraft = originalDomain.CreateReversalDraft(branchScope, command.AccountingDate, reversalDescription, actionContext);

            var reversalRow = new JournalEntryRow
            {
                Id = reversalDraft.Id,
                CompanyId = reversalDraft.CompanyId,
                BranchId = reversalDraft.BranchId,
                AccountingDate = reversalDraft.AccountingDate,
                Description = reversalDraft.Description,
                State = (byte)reversalDraft.State,
                ReversesEntryId = originalRow.Id,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };
            context.JournalEntries.Add(reversalRow);

            foreach (var line in reversalDraft.Lines)
            {
                context.JournalLines.Add(MapLineRow(line, reversalDraft.Id, now));
            }

            context.JournalAudit.Add(new JournalAuditRow
            {
                EventId = Guid.NewGuid(),
                CompanyId = reversalDraft.CompanyId,
                BranchId = reversalDraft.BranchId,
                JournalId = reversalDraft.Id,
                Revision = 1,
                Action = (byte)JournalAuditAction.Created,
                PreviousState = null,
                NewState = (byte)reversalDraft.State,
                ActorUserId = scope.ActorUserId,
                CorrelationId = actionContext.CorrelationId,
                Reason = actionContext.Reason,
                OccurredAtUtc = actionContext.OccurredAtUtc,
                RelatedEntryId = originalRow.Id,
                CreatedAtUtc = now,
            });

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return await GetJournalAsync(scope, reversalDraft.Id, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task DeleteDraftAsync(
        JournalEntryScope scope,
        Guid journalId,
        ConcurrencyToken expectedVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(expectedVersion);

        var transaction = await OwnedDatabaseTransaction.BeginAsync(context, IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            var journalRow = await context.JournalEntries
                .FromSqlInterpolated($"SELECT * FROM [ledger].[JournalEntries] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {journalId}")
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false)
                ?? throw new ResourceNotFoundException("journal");

            VerifyOwnership(journalRow, scope);
            VerifyVersion(journalRow, expectedVersion);

            if (journalRow.State != (byte)JournalEntryState.Draft)
            {
                throw RuleViolation.Create(RuleViolation.JournalStateInvalid, $"Only unvalidated draft journals may be deleted. Current state is {(JournalEntryState)journalRow.State}.");
            }

            var lines = await context.JournalLines.Where(l => l.JournalId == journalId).ToListAsync(cancellationToken).ConfigureAwait(false);
            var audits = await context.JournalAudit.Where(a => a.JournalId == journalId).ToListAsync(cancellationToken).ConfigureAwait(false);

            var voucherHeaders = await context.JournalVoucherHeaders.Where(h => h.JournalId == journalId).ToListAsync(cancellationToken).ConfigureAwait(false);

            context.JournalVoucherHeaders.RemoveRange(voucherHeaders);
            context.JournalLines.RemoveRange(lines);
            context.JournalAudit.RemoveRange(audits);
            context.JournalEntries.Remove(journalRow);

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<JournalDetailsDto> GetJournalAsync(
        JournalEntryScope scope,
        Guid journalId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var journalRow = await context.JournalEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == journalId && j.CompanyId == scope.CompanyId && j.BranchId == scope.BranchId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("journal");

        var lineRows = await context.JournalLines
            .AsNoTracking()
            .Where(l => l.JournalId == journalId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var accountIds = lineRows.Select(l => l.AccountId).Distinct().ToList();
        var accounts = await context.Accounts
            .AsNoTracking()
            .Where(a => accountIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, cancellationToken)
            .ConfigureAwait(false);

        var costCenterIds = lineRows.Where(l => l.CostCenterId.HasValue).Select(l => l.CostCenterId!.Value).Distinct().ToList();
        var costCenters = costCenterIds.Count > 0
            ? await context.CostCenters
                .AsNoTracking()
                .Where(cc => costCenterIds.Contains(cc.Id))
                .ToDictionaryAsync(cc => cc.Id, cancellationToken)
                .ConfigureAwait(false)
            : [];

        var auditRows = await context.JournalAudit
            .AsNoTracking()
            .Where(a => a.JournalId == journalId)
            .OrderBy(a => a.Revision)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var lines = lineRows.Select(l =>
        {
            accounts.TryGetValue(l.AccountId, out var acc);
            CostCenterRow? cc = null;
            if (l.CostCenterId.HasValue) costCenters.TryGetValue(l.CostCenterId.Value, out cc);

            return new JournalLineDto
            {
                Id = l.Id,
                AccountId = l.AccountId,
                AccountCode = acc?.Code ?? string.Empty,
                AccountNameArabic = acc?.NameArabic ?? string.Empty,
                AccountNameEnglish = acc?.NameEnglish,
                CostCenterId = l.CostCenterId,
                CostCenterCode = cc?.Code,
                CostCenterNameArabic = cc?.NameArabic,
                CostCenterNameEnglish = cc?.NameEnglish,
                TransactionCurrencyCode = l.TransactionCurrencyCode,
                TransactionDebit = l.TransactionDebit,
                TransactionCredit = l.TransactionCredit,
                BaseCurrencyCode = l.BaseCurrencyCode,
                BaseDebit = l.BaseDebit,
                BaseCredit = l.BaseCredit,
                RateSourceCurrencyCode = l.RateSourceCurrencyCode,
                RateTargetCurrencyCode = l.RateTargetCurrencyCode,
                RateValue = l.RateValue,
                Rounding = l.Rounding,
                Description = l.Description,
                ReversesLineId = l.ReversesLineId,
            };
        }).ToList();

        var actorIds = auditRows.Select(a => a.ActorUserId).Distinct().ToArray();
        var actors = await context.ActorNamesAsync(actorIds, cancellationToken).ConfigureAwait(false);
        var audits = auditRows.Select(a => new JournalAuditDto
        {
            EventId = a.EventId,
            Revision = a.Revision,
            Action = a.Action,
            ActionName = ((JournalAuditAction)a.Action).ToString(),
            PreviousState = a.PreviousState,
            NewState = a.NewState,
            ActorUserId = a.ActorUserId,
            ActorNameArabic = actors.GetValueOrDefault(a.ActorUserId)?.DisplayNameArabic,
            ActorNameEnglish = actors.GetValueOrDefault(a.ActorUserId)?.DisplayNameEnglish,
            Reason = a.Reason,
            OccurredAtUtc = a.OccurredAtUtc,
            RelatedEntryId = a.RelatedEntryId,
        }).ToList();

        var totalDebit = lineRows.Sum(l => l.BaseDebit);
        var totalCredit = lineRows.Sum(l => l.BaseCredit);
        var baseCurrency = lineRows.FirstOrDefault()?.BaseCurrencyCode ?? string.Empty;

        return new JournalDetailsDto
        {
            Id = journalRow.Id,
            CompanyId = journalRow.CompanyId,
            BranchId = journalRow.BranchId,
            AccountingDate = journalRow.AccountingDate,
            Description = journalRow.Description,
            State = journalRow.State,
            StateName = ((JournalEntryState)journalRow.State).ToString(),
            Revision = audits.Count > 0 ? audits.Max(a => a.Revision) : 1,
            VersionBase64 = Convert.ToBase64String(journalRow.RowVersion),
            PostingSequence = journalRow.PostingSequence,
            PostingFiscalYearId = journalRow.PostingFiscalYearId,
            PostingPeriodId = journalRow.PostingPeriodId,
            ReversesEntryId = journalRow.ReversesEntryId,
            ReversedByEntryId = journalRow.ReversedByEntryId,
            CorrectsEntryId = journalRow.CorrectsEntryId,
            TotalDebit = totalDebit,
            TotalCredit = totalCredit,
            BaseCurrencyCode = baseCurrency,
            CreatedAtUtc = journalRow.CreatedAtUtc,
            UpdatedAtUtc = journalRow.UpdatedAtUtc,
            Lines = lines.AsReadOnly(),
            AuditTrail = audits.AsReadOnly(),
        };
    }

    public async Task<PagedResult<JournalSummaryDto>> SearchJournalsAsync(
        JournalEntryScope scope,
        DateOnly? fromDate,
        DateOnly? toDate,
        byte? state,
        string? search,
        PageRequest page,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(page);

        var query = context.JournalEntries
            .AsNoTracking()
            .Where(j => j.CompanyId == scope.CompanyId && j.BranchId == scope.BranchId);

        if (fromDate.HasValue) query = query.Where(j => j.AccountingDate >= fromDate.Value);
        if (toDate.HasValue) query = query.Where(j => j.AccountingDate <= toDate.Value);
        if (state.HasValue) query = query.Where(j => j.State == state.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var trimmed = search.Trim();
            if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seq))
            {
                query = query.Where(j => j.Description.Contains(trimmed) || j.PostingSequence == seq);
            }
            else
            {
                query = query.Where(j => j.Description.Contains(trimmed));
            }
        }

        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await query
            .OrderByDescending(j => j.AccountingDate)
            .ThenByDescending(j => j.CreatedAtUtc)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(j => new
            {
                j.Id,
                j.AccountingDate,
                j.Description,
                j.State,
                j.PostingSequence,
                j.RowVersion,
                LineCount = j.Lines.Count,
                TotalDebit = j.Lines.Sum(l => l.BaseDebit),
                TotalCredit = j.Lines.Sum(l => l.BaseCredit),
                BaseCurrency = j.Lines.Select(l => l.BaseCurrencyCode).FirstOrDefault() ?? string.Empty,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var items = rows.Select(r => new JournalSummaryDto
        {
            Id = r.Id,
            AccountingDate = r.AccountingDate,
            Description = r.Description,
            State = r.State,
            StateName = ((JournalEntryState)r.State).ToString(),
            PostingSequence = r.PostingSequence,
            TotalDebit = r.TotalDebit,
            TotalCredit = r.TotalCredit,
            BaseCurrencyCode = r.BaseCurrency,
            LineCount = r.LineCount,
            VersionBase64 = Convert.ToBase64String(r.RowVersion),
        }).ToList();

        return new PagedResult<JournalSummaryDto>(items.AsReadOnly(), page.Page, page.PageSize, total);
    }

    public async Task<IReadOnlyList<AccountLookupDto>> GetAccountsAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var accounts = await context.Accounts
            .AsNoTracking()
            .Where(a => a.CompanyId == companyId)
            .OrderBy(a => a.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return accounts.Select(a => new AccountLookupDto
        {
            Id = a.Id,
            CompanyId = a.CompanyId,
            Code = a.Code,
            NameArabic = a.NameArabic,
            NameEnglish = a.NameEnglish,
            AccountType = a.AccountType,
            AccountTypeName = ((AccountType)a.AccountType).ToString(),
            PostingAllowed = a.PostingAllowed,
            CostCenterPolicy = a.CostCenterPolicy,
            CurrencyMode = a.CurrencyMode,
            FixedCurrencyCode = a.FixedCurrencyCode,
            ParentId = a.ParentId,
            IsActive = a.IsActive,
        }).ToList().AsReadOnly();
    }

    public async Task<IReadOnlyList<CostCenterLookupDto>> GetCostCentersAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var centers = await context.CostCenters
            .AsNoTracking()
            .Where(c => c.CompanyId == companyId)
            .OrderBy(c => c.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return centers.Select(c => new CostCenterLookupDto
        {
            Id = c.Id,
            CompanyId = c.CompanyId,
            Code = c.Code,
            NameArabic = c.NameArabic,
            NameEnglish = c.NameEnglish,
            IsActive = c.IsActive,
        }).ToList().AsReadOnly();
    }

    public async Task<JournalDraftValidationResult> ValidateJournalLinesAsync(
        JournalEntryScope scope,
        DateOnly accountingDate,
        IReadOnlyList<JournalDraftLineInput> lines,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(lines);

        var errors = new List<string>();
        decimal totalDebit = 0m;
        decimal totalCredit = 0m;

        if (lines.Count == 0)
        {
            errors.Add("Journal must have at least one line.");
            return new JournalDraftValidationResult
            {
                IsValid = false,
                Errors = errors.AsReadOnly(),
                TotalDebit = 0m,
                TotalCredit = 0m,
                Difference = 0m,
                IsBalanced = true,
            };
        }

        var (companyDomain, _) = await LoadScopeAsync(scope, cancellationToken).ConfigureAwait(false);

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.TransactionDebit < 0 || line.TransactionCredit < 0)
            {
                errors.Add($"Line {i + 1}: Amounts cannot be negative.");
            }
            if (line.TransactionDebit == 0 && line.TransactionCredit == 0)
            {
                errors.Add($"Line {i + 1}: Line must have a nonzero debit or credit amount.");
            }
            if (line.TransactionDebit > 0 && line.TransactionCredit > 0)
            {
                errors.Add($"Line {i + 1}: Line cannot have both debit and credit amounts.");
            }
            if (line.RateValue <= 0)
            {
                errors.Add($"Line {i + 1}: Exchange rate must be strictly positive.");
            }

            var baseDebit = line.BaseDebit ?? decimal.Round(line.TransactionDebit * line.RateValue, FinancialPrecision.MoneyScale, line.Rounding);
            var baseCredit = line.BaseCredit ?? decimal.Round(line.TransactionCredit * line.RateValue, FinancialPrecision.MoneyScale, line.Rounding);

            totalDebit += baseDebit;
            totalCredit += baseCredit;
        }

        var diff = Math.Abs(totalDebit - totalCredit);
        if (diff != 0m)
        {
            errors.Add($"Journal is not balanced. Total debit ({totalDebit}) does not equal total credit ({totalCredit}). Difference: {diff}.");
        }

        var accountIds = lines.Select(l => l.AccountId).Distinct().ToList();
        var accounts = await context.Accounts
            .AsNoTracking()
            .Where(a => a.CompanyId == scope.CompanyId && accountIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, cancellationToken)
            .ConfigureAwait(false);

        foreach (var id in accountIds)
        {
            if (!accounts.TryGetValue(id, out var acc))
            {
                errors.Add($"Account '{id}' does not exist in this company.");
                continue;
            }

            if (!acc.IsActive)
            {
                errors.Add($"Account '{acc.Code}' is deactivated.");
            }

            if (!acc.PostingAllowed)
            {
                errors.Add($"Posting is not allowed directly to parent/control account '{acc.Code}'.");
            }
        }

        var fiscalYearRow = await context.FiscalYears
            .AsNoTracking()
            .FirstOrDefaultAsync(y => y.CompanyId == scope.CompanyId && y.RangeStart <= accountingDate && y.RangeEnd >= accountingDate, cancellationToken)
            .ConfigureAwait(false);

        if (fiscalYearRow is null)
        {
            errors.Add($"No fiscal year covers accounting date {accountingDate:yyyy-MM-dd}.");
        }
        else
        {
            var periodRow = await context.AccountingPeriods
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.FiscalYearId == fiscalYearRow.Id && p.RangeStart <= accountingDate && p.RangeEnd >= accountingDate, cancellationToken)
                .ConfigureAwait(false);

            if (periodRow is null)
            {
                errors.Add($"No accounting period covers accounting date {accountingDate:yyyy-MM-dd}.");
            }
        }

        return new JournalDraftValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors.AsReadOnly(),
            TotalDebit = totalDebit,
            TotalCredit = totalCredit,
            Difference = diff,
            IsBalanced = diff == 0m,
        };
    }

    private async Task<(Company Company, BranchScope Scope)> LoadScopeAsync(JournalEntryScope scope, CancellationToken cancellationToken)
    {
        var companyRow = await context.Companies
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == scope.CompanyId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("company");

        if (!companyRow.IsActive)
        {
            throw RuleViolation.Create(RuleViolation.ScopeInactive, "Company is inactive.");
        }

        var branchRow = await context.Branches
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == scope.BranchId && b.CompanyId == scope.CompanyId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("branch");

        if (!branchRow.IsActive)
        {
            throw RuleViolation.Create(RuleViolation.ScopeInactive, "Branch is inactive.");
        }

        var additionalCurrencies = await context.CompanyAdditionalCurrencies
            .AsNoTracking()
            .Where(c => c.CompanyId == scope.CompanyId)
            .Select(c => c.CurrencyCode)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var companyDomain = Company.Reconstitute(
            companyRow.Id,
            EntityCode.Create(companyRow.Code),
            BilingualName.Create(companyRow.NameArabic, companyRow.NameEnglish),
            CurrencyCode.Create(companyRow.BaseCurrencyCode),
            additionalCurrencies.Select(CurrencyCode.Create),
            companyRow.IsActive);

        var branchDomain = Branch.Reconstitute(
            branchRow.Id,
            branchRow.CompanyId,
            EntityCode.Create(branchRow.Code),
            BilingualName.Create(branchRow.NameArabic, branchRow.NameEnglish),
            branchRow.IsActive);

        var branchScope = BranchScope.Create(companyDomain, branchDomain);
        return (companyDomain, branchScope);
    }

    private static ReadOnlyCollection<JournalLine> BuildDomainLines(Company company, IReadOnlyList<JournalDraftLineInput> inputs)
    {
        var lines = new List<JournalLine>(inputs.Count);
        foreach (var input in inputs)
        {
            var txCurrency = CurrencyCode.Create(input.TransactionCurrencyCode);
            var baseCurrency = company.BaseCurrency;

            var rate = ExchangeRate.Create(
                CurrencyCode.Create(input.RateSourceCurrencyCode ?? txCurrency.Value),
                CurrencyCode.Create(input.RateTargetCurrencyCode ?? baseCurrency.Value),
                input.RateValue);

            var txAmounts = JournalLineAmounts.Create(
                Money.Create(input.TransactionDebit, txCurrency),
                Money.Create(input.TransactionCredit, txCurrency));

            var line = JournalLine.Create(
                company,
                input.AccountId,
                input.CostCenterId,
                txAmounts,
                rate,
                input.Rounding,
                input.Description);

            lines.Add(line);
        }

        return lines.AsReadOnly();
    }

    private static JournalLineRow MapLineRow(JournalLine line, Guid journalId, DateTimeOffset now) => new()
    {
        Id = line.Id,
        CompanyId = line.CompanyId,
        JournalId = journalId,
        AccountId = line.AccountId,
        CostCenterId = line.CostCenterId,
        TransactionCurrencyCode = line.TransactionAmounts.Debit.Currency.Value,
        TransactionDebit = line.TransactionAmounts.Debit.Amount,
        TransactionCredit = line.TransactionAmounts.Credit.Amount,
        BaseCurrencyCode = line.BaseAmounts.Debit.Currency.Value,
        BaseDebit = line.BaseAmounts.Debit.Amount,
        BaseCredit = line.BaseAmounts.Credit.Amount,
        RateSourceCurrencyCode = line.Rate.SourceCurrency.Value,
        RateTargetCurrencyCode = line.Rate.TargetCurrency.Value,
        RateValue = line.Rate.Value,
        Rounding = (byte)line.Rounding,
        Description = line.Description,
        ReversesLineId = line.ReversesLineId,
        CreatedAtUtc = now,
        UpdatedAtUtc = now,
    };

    private async Task<JournalValidationContext> BuildValidationContextAsync(
        Company company,
        BranchScope branchScope,
        DateOnly accountingDate,
        IReadOnlyList<JournalLineRow> lines,
        CancellationToken cancellationToken)
    {
        var fiscalYearRow = await context.FiscalYears
            .AsNoTracking()
            .FirstOrDefaultAsync(y => y.CompanyId == company.Id && y.RangeStart <= accountingDate && y.RangeEnd >= accountingDate, cancellationToken)
            .ConfigureAwait(false)
            ?? throw RuleViolation.Create(RuleViolation.PeriodMissing, $"No fiscal year covers accounting date {accountingDate:yyyy-MM-dd}.");

        var periodRows = await context.AccountingPeriods
            .AsNoTracking()
            .Where(p => p.FiscalYearId == fiscalYearRow.Id)
            .OrderBy(p => p.RangeStart)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var fiscalYearDomain = FiscalYear.Reconstitute(
            fiscalYearRow.Id,
            fiscalYearRow.CompanyId,
            EntityCode.Create(fiscalYearRow.Code),
            DateRange.Create(fiscalYearRow.RangeStart, fiscalYearRow.RangeEnd),
            periodRows.Select(p => AccountingPeriod.Reconstitute(p.Id, p.CompanyId, p.FiscalYearId, EntityCode.Create(p.Code), DateRange.Create(p.RangeStart, p.RangeEnd), (AccountingPeriodState)p.State)));

        var accountIds = lines.Select(l => l.AccountId).Distinct().ToList();
        var accountRows = await context.Accounts
            .AsNoTracking()
            .Where(a => accountIds.Contains(a.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var accountDomains = accountRows.Select(acc => Account.Reconstitute(
            acc.Id, acc.CompanyId, EntityCode.Create(acc.Code),
            BilingualName.Create(acc.NameArabic, acc.NameEnglish),
            (AccountType)acc.AccountType, acc.PostingAllowed,
            (CostCenterPolicy)acc.CostCenterPolicy,
            acc.FixedCurrencyCode is not null
                ? AccountCurrencyPolicy.Fixed(CurrencyCode.Create(acc.FixedCurrencyCode))
                : (acc.CurrencyMode == 1 ? AccountCurrencyPolicy.BaseOnly() : AccountCurrencyPolicy.AnyEnabled()),
            acc.ParentId, acc.IsActive)).ToList();

        var centerIds = lines.Where(l => l.CostCenterId.HasValue).Select(l => l.CostCenterId!.Value).Distinct().ToList();
        var centerRows = centerIds.Count > 0
            ? await context.CostCenters
                .AsNoTracking()
                .Where(cc => centerIds.Contains(cc.Id))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false)
            : [];

        var centerDomains = centerRows.Select(cc => CostCenter.Reconstitute(
            cc.Id, cc.CompanyId, EntityCode.Create(cc.Code),
            BilingualName.Create(cc.NameArabic, cc.NameEnglish),
            cc.IsActive)).ToList();

        return JournalValidationContext.Create(
            company,
            branchScope,
            fiscalYearDomain,
            accountDomains,
            centerDomains);
    }

    private static JournalEntry BuildDomainEntry(
        JournalEntryRow journalRow,
        IReadOnlyList<JournalLineRow> lines,
        IReadOnlyList<JournalAuditRow> audits)
    {
        var domainLines = lines.Select(l => JournalLine.Reconstitute(
            l.Id, l.CompanyId, l.AccountId, l.CostCenterId,
            JournalLineAmounts.Create(
                Money.Create(l.TransactionDebit, CurrencyCode.Create(l.TransactionCurrencyCode)),
                Money.Create(l.TransactionCredit, CurrencyCode.Create(l.TransactionCurrencyCode))),
            JournalLineAmounts.Create(
                Money.Create(l.BaseDebit, CurrencyCode.Create(l.BaseCurrencyCode)),
                Money.Create(l.BaseCredit, CurrencyCode.Create(l.BaseCurrencyCode))),
            ExchangeRate.Create(
                CurrencyCode.Create(l.RateSourceCurrencyCode),
                CurrencyCode.Create(l.RateTargetCurrencyCode),
                l.RateValue),
            (MidpointRounding)l.Rounding,
            l.Description,
            l.ReversesLineId,
            l.JournalId)).ToList();

        var domainAudits = audits.Select(a => JournalAuditEvent.Reconstitute(
            a.EventId, a.CompanyId, a.BranchId, a.JournalId, a.Revision,
            (JournalAuditAction)a.Action,
            a.PreviousState.HasValue ? (JournalEntryState)a.PreviousState.Value : null,
            (JournalEntryState)a.NewState,
            JournalActionContext.Create(a.ActorUserId, a.CorrelationId, a.Reason, a.OccurredAtUtc),
            a.RelatedEntryId)).ToList();

        return JournalEntry.Reconstitute(
            journalRow.Id, journalRow.CompanyId, journalRow.BranchId,
            journalRow.AccountingDate, journalRow.Description,
            domainLines, (JournalEntryState)journalRow.State,
            domainAudits,
            journalRow.PostingSequence.HasValue
                ? JournalPostingNumber.Reconstitute(journalRow.CompanyId, journalRow.BranchId, journalRow.PostingFiscalYearId!.Value, journalRow.PostingSequence.Value)
                : null,
            journalRow.PostingPeriodId, journalRow.ReversesEntryId,
            journalRow.ReversedByEntryId, journalRow.CorrectsEntryId);
    }

    private static void VerifyOwnership(JournalEntryRow row, JournalEntryScope scope)
    {
        if (row.CompanyId != scope.CompanyId || row.BranchId != scope.BranchId)
        {
            // Do not reveal records of another company/branch: report them exactly like missing records.
            throw new ResourceNotFoundException("journal");
        }
    }

    private static void VerifyVersion(JournalEntryRow row, ConcurrencyToken expectedVersion)
    {
        if (!expectedVersion.Matches(row.RowVersion))
        {
            throw new ConcurrencyConflictException("The journal entry has been modified by another user.");
        }
    }
}
