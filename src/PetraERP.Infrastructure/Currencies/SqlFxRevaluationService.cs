using System.Data;
using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Currencies;
using PetraERP.Application.GeneralLedger;
using PetraERP.Application.GeneralLedger.Posting;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Domain.Primitives;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Currencies;

public sealed partial class SqlFxRevaluationService(
    FinancialDbContext context,
    ICurrencyService currencyService,
    IJournalEntryService journalService) : IFxRevaluationService
{
    public async Task<FxRevaluationPreviewResult> PreviewRevaluationAsync(
        Guid companyId,
        Guid branchId,
        FxRevaluationPreviewCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var company = await context.Companies.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == companyId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new BusinessRuleException("company.not_found", $"Company {companyId} not found.");

        if (command.OverrideClosingRate is <= 0m)
            throw new RequestValidationException("fx.override_rate_invalid", "The override closing rate must be positive.", "overrideClosingRate");
        var baseCurrency = company.BaseCurrencyCode;
        var asOfDate = command.AsOfDate;

        // Branch-scoped revaluation: every balance read is restricted to the selected company AND branch, so FX effects
        // from other branches never contribute to this branch's adjustment or journal (Task C).
        if (!await context.Branches.AsNoTracking().AnyAsync(b => b.Id == branchId && b.CompanyId == companyId, cancellationToken).ConfigureAwait(false))
            throw new BusinessRuleException("fx.branch_invalid", $"Branch {branchId} does not belong to company {companyId}.");

        var filterCode = string.IsNullOrWhiteSpace(command.CurrencyCode) ? null : command.CurrencyCode.Trim().ToUpperInvariant();

        // Outstanding foreign-currency exposure per account: the transaction-currency balance of foreign lines only.
        var rawForeignGroups = (await (
                from line in context.JournalLines.AsNoTracking()
                join entry in context.JournalEntries.AsNoTracking() on line.JournalId equals entry.Id
                join account in context.Accounts.AsNoTracking() on line.AccountId equals account.Id
                where entry.CompanyId == companyId && entry.BranchId == branchId
                      && (entry.State == (byte)JournalEntryState.Posted || entry.State == (byte)JournalEntryState.Reversed)
                      && entry.AccountingDate <= asOfDate
                      && line.TransactionCurrencyCode != baseCurrency
                      && (filterCode == null || line.TransactionCurrencyCode == filterCode)
                group new { line.TransactionDebit, line.TransactionCredit }
                    by new { line.AccountId, account.Code, account.NameArabic, account.FxMonetaryEligibility, line.TransactionCurrencyCode } into g
                select new
                {
                    g.Key.AccountId, g.Key.Code, g.Key.NameArabic, g.Key.FxMonetaryEligibility, g.Key.TransactionCurrencyCode,
                    ForeignBalance = g.Sum(x => x.TransactionDebit - x.TransactionCredit)
                }).ToListAsync(cancellationToken).ConfigureAwait(false))
            .ToList();

        var requiresReview = rawForeignGroups.FirstOrDefault(g => g.FxMonetaryEligibility == (byte)FxMonetaryClassification.RequiresReview);
        if (requiresReview is not null)
            throw new BusinessRuleException("fx.account_requires_review",
                $"Account '{requiresReview.Code}' has FX classification set to RequiresReview; configure its policy before revaluation.");

        var foreignGroups = rawForeignGroups
            .Where(g => g.FxMonetaryEligibility == (byte)FxMonetaryClassification.MonetaryEligible)
            .ToList();

        // Current base-currency carrying value per account: EVERY posted base effect for this company/branch — original
        // transactions, settlements AND prior revaluation adjustments (posted in base currency). Using the account's full
        // base balance (not only foreign-line base amounts) prevents re-recognizing an FX gain/loss a previous
        // revaluation already posted (Task B).
        var accountIds = foreignGroups.Select(g => g.AccountId).Distinct().ToList();
        // A base-currency adjustment has no currency allocation in the legacy journal-line model. It is safe to
        // attribute it only when this account's history contains one foreign currency. Never reuse the same account
        // carrying value for multiple currencies, even when the caller filters the preview down to one currency.
        var currenciesPerAccount = await (
                from line in context.JournalLines.AsNoTracking()
                join entry in context.JournalEntries.AsNoTracking() on line.JournalId equals entry.Id
                where entry.CompanyId == companyId && entry.BranchId == branchId
                    && (entry.State == (byte)JournalEntryState.Posted || entry.State == (byte)JournalEntryState.Reversed)
                    && entry.AccountingDate <= asOfDate && accountIds.Contains(line.AccountId)
                    && line.TransactionCurrencyCode != baseCurrency
                select new { line.AccountId, line.TransactionCurrencyCode })
            .Distinct().ToListAsync(cancellationToken).ConfigureAwait(false);
        if (currenciesPerAccount.GroupBy(x => x.AccountId).Any(g => g.Count() > 1))
            throw new BusinessRuleException("fx.carrying_value_ambiguous",
                "The account contains multiple foreign currencies without a reliable historical adjustment allocation. Use separately identified monetary currency accounts or complete the approved allocation linkage.");

        var unattributedBaseEffects = await (
                from line in context.JournalLines.AsNoTracking()
                join entry in context.JournalEntries.AsNoTracking() on line.JournalId equals entry.Id
                where entry.CompanyId == companyId && entry.BranchId == branchId
                    && (entry.State == (byte)JournalEntryState.Posted || entry.State == (byte)JournalEntryState.Reversed)
                    && entry.AccountingDate <= asOfDate && accountIds.Contains(line.AccountId)
                    && line.TransactionCurrencyCode == baseCurrency
                    && !context.FxRevaluationRequests.Any(r => r.CompanyId == companyId && r.BranchId == branchId
                        && (r.DraftJournalId == entry.Id || r.OutcomeJournalId == entry.Id
                            || (entry.ReversesEntryId != null
                                && (r.DraftJournalId == entry.ReversesEntryId || r.OutcomeJournalId == entry.ReversesEntryId))))
                group new { line.BaseDebit, line.BaseCredit } by line.AccountId into g
                select g.Sum(x => x.BaseDebit - x.BaseCredit)).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (unattributedBaseEffects.Any(v => v != 0m))
            throw new BusinessRuleException("fx.carrying_value_ambiguous",
                "Base-currency movements on the monetary account cannot be reliably allocated to its foreign exposure. Reconcile the historical currency allocation before revaluation.");

        var carryingByAccount = await (
                from line in context.JournalLines.AsNoTracking()
                join entry in context.JournalEntries.AsNoTracking() on line.JournalId equals entry.Id
                where entry.CompanyId == companyId && entry.BranchId == branchId
                      && (entry.State == (byte)JournalEntryState.Posted || entry.State == (byte)JournalEntryState.Reversed)
                      && entry.AccountingDate <= asOfDate
                      && accountIds.Contains(line.AccountId)
                group new { line.BaseDebit, line.BaseCredit } by line.AccountId into g
                select new { AccountId = g.Key, BaseBalance = g.Sum(x => x.BaseDebit - x.BaseCredit) })
            .ToDictionaryAsync(x => x.AccountId, x => x.BaseBalance, cancellationToken).ConfigureAwait(false);

        var grouped = foreignGroups.Select(g => new
            {
                g.AccountId, g.Code, g.NameArabic, g.TransactionCurrencyCode, g.ForeignBalance,
                BookBaseBalance = carryingByAccount.GetValueOrDefault(g.AccountId, 0m)
            }).ToList();

        var overrideRate = command.OverrideClosingRate is > 0m ? command.OverrideClosingRate : null;

        var items = new List<FxAccountRevaluationItemResult>();
        decimal totalGains = 0m;
        decimal totalLosses = 0m;

        foreach (var group in grouped)
        {
            // A genuine foreign-currency balance (the query excludes the base currency) must be revalued at a real closing
            // rate. A missing, zero or negative rate is a structured validation error — never a silent rate of 1.
            decimal rate;
            if (overrideRate.HasValue)
            {
                rate = overrideRate.Value;
            }
            else
            {
                var effective = await currencyService.GetEffectiveRateAsync(companyId, group.TransactionCurrencyCode, asOfDate, cancellationToken).ConfigureAwait(false);
                if (!effective.Found || effective.Rate is not { } resolved || resolved <= 0m)
                    throw new BusinessRuleException("fx.closing_rate_unavailable",
                        $"لا يوجد سعر إقفال صالح لتحويل {group.TransactionCurrencyCode} إلى {baseCurrency} بتاريخ {asOfDate:yyyy-MM-dd}. / No valid closing rate for {group.TransactionCurrencyCode}->{baseCurrency} as of {asOfDate:yyyy-MM-dd}.");
                rate = resolved;
            }

            var revaluedBase = Math.Round(group.ForeignBalance * rate, 4, MidpointRounding.AwayFromZero);
            var diff = revaluedBase - group.BookBaseBalance;

            if (diff == 0m) continue;

            var isGain = diff > 0m;
            if (isGain)
            {
                totalGains += diff;
            }
            else
            {
                totalLosses += Math.Abs(diff);
            }

            items.Add(new FxAccountRevaluationItemResult(
                group.AccountId,
                group.Code,
                group.NameArabic,
                group.TransactionCurrencyCode,
                group.ForeignBalance,
                group.BookBaseBalance,
                rate,
                revaluedBase,
                diff,
                isGain));
        }

        var netDiff = totalGains - totalLosses;

        return new FxRevaluationPreviewResult(
            asOfDate,
            baseCurrency,
            items.OrderBy(i => i.AccountCode, StringComparer.Ordinal).ToList(),
            totalGains,
            totalLosses,
            netDiff);
    }

    public async Task<FxRevaluationExecutionResult> ExecuteRevaluationAsync(
        Guid companyId,
        Guid branchId,
        Guid userId,
        ExecuteFxRevaluationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.GainAccountId == Guid.Empty || command.LossAccountId == Guid.Empty)
            throw new BusinessRuleException("fx.accounts_required", "حساب أرباح وحساب خسائر فروقات الصرف مطلوبان لإتمام إعادة التقييم.");

        if (command.OverrideClosingRate is <= 0m)
            throw new RequestValidationException("fx.override_rate_invalid", "The override closing rate must be positive.", "overrideClosingRate");
        if (command.GainAccountId == command.LossAccountId)
            throw new BusinessRuleException("fx.accounts_invalid", "Gain and loss accounts must be distinct.");
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey) || command.IdempotencyKey.Trim().Length > 128)
            throw new RequestValidationException("fx.idempotency_key_required", "An idempotency key is required to execute FX revaluation.", "idempotencyKey");

        if (context.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("FX execution owns its resumable workflow and cannot join an unrelated transaction.");
        await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var resource = $"PetraERP.FxRevaluation:{companyId:N}:{branchId:N}";
        var lockHeld = false;
        try
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                DECLARE @result int;
                EXEC @result=sys.sp_getapplock @Resource={resource}, @LockMode=N'Exclusive', @LockOwner=N'Session', @LockTimeout=30000;
                IF @result<0 THROW 51030, N'Cannot acquire FX workflow lock.', 1;
                """, cancellationToken).ConfigureAwait(false);
            lockHeld = true;
            await EnsureActorReferenceAsync(cancellationToken).ConfigureAwait(false);
            return await ExecuteLockedAsync(companyId, branchId, userId, command, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                if (lockHeld)
                    await context.Database.ExecuteSqlInterpolatedAsync($"EXEC sys.sp_releaseapplock @Resource={resource}, @LockOwner=N'Session'", CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await context.Database.CloseConnectionAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task<FxRevaluationExecutionResult> ExecuteLockedAsync(Guid companyId, Guid branchId, Guid userId,
        ExecuteFxRevaluationCommand command, CancellationToken cancellationToken)
    {
        var idempotencyKey = command.IdempotencyKey.Trim();

        // Canonical content fingerprint over the economically-meaningful request inputs. Reusing one idempotency key with a
        // different canonical request is rejected (fx.idempotency_conflict); reusing it with the same request replays the
        // original journal or resumes an interrupted attempt (TASK D).
        var fingerprintHex = ComputeRequestFingerprint(companyId, branchId, command, legacy: false);
        var legacyFingerprintHex = ComputeRequestFingerprint(companyId, branchId, command, legacy: true);

        // Stable post key derived from the caller key. The single reused draft journal plus this key mean a retry (a lost
        // HTTP response, or a crash mid-sequence) can never post a second journal (TASK E).
        var postingKey = DerivePostingKey(companyId, branchId, idempotencyKey);

        var existing = await LoadRequestRowAsync(companyId, branchId, idempotencyKey, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            EnsureFingerprintMatches(existing, fingerprintHex, legacyFingerprintHex, idempotencyKey);
            if (existing.Status == FxRequestCompleted && existing.OutcomeJournalId is Guid already)
                return await BuildResultFromJournalAsync(already, command, cancellationToken).ConfigureAwait(false);
        }

        Guid draftId;
        if (existing?.DraftJournalId is Guid resumeDraft)
        {
            // An interrupted attempt already created its draft: drive the SAME draft forward and post under the SAME key.
            draftId = resumeDraft;
        }
        else
        {
            // Persist the reservation and its draft together. A failed create cannot leave an unlinked reservation
            // or draft. Later validation/approval/posting stages remain an explicitly recoverable durable workflow.
            var preparation = await OwnedDatabaseTransaction.BeginAsync(context, IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
            await using var preparationLifetime = preparation.ConfigureAwait(false);
            await PetraERP.Infrastructure.Inventory.InventoryWriteScope.LockCompanyAsync(context, companyId, cancellationToken).ConfigureAwait(false);
            if (existing is null)
                await TryReserveRequestRowAsync(companyId, branchId, idempotencyKey, fingerprintHex, userId, cancellationToken).ConfigureAwait(false);

            // Re-load the authoritative row (ours, or a concurrent winner's) and branch on its persisted state.
            var row = await LoadRequestRowAsync(companyId, branchId, idempotencyKey, cancellationToken).ConfigureAwait(false)
                ?? throw new BusinessRuleException("fx.reservation_failed", "تعذّر حجز طلب إعادة التقييم.");
            EnsureFingerprintMatches(row, fingerprintHex, legacyFingerprintHex, idempotencyKey);
            if (row.Status == FxRequestCompleted && row.OutcomeJournalId is Guid raced)
                return await BuildResultFromJournalAsync(raced, command, cancellationToken).ConfigureAwait(false);

            if (row.DraftJournalId is Guid recordedDraft)
            {
                draftId = recordedDraft;
            }
            else
            {
                // Price the revaluation and build the single balanced draft. Pricing happens once per key; retries reuse it.
                var preview = await PreviewRevaluationAsync(companyId, branchId, new FxRevaluationPreviewCommand(
                    command.AsOfDate, command.CurrencyCode, command.OverrideClosingRate), cancellationToken).ConfigureAwait(false);

                var candidateItems = preview.Items;
                if (command.SelectedAccountIds is { Count: > 0 } filterIds)
                    candidateItems = candidateItems.Where(i => filterIds.Contains(i.AccountId)).ToList();

                if (candidateItems.Count == 0)
                    throw new BusinessRuleException("fx.no_items", "لا توجد فروقات أسعار صرف قابلة للتقييم في التاريخ المحدد.");

                var description = string.IsNullOrWhiteSpace(command.Description)
                    ? $"قيد إعادة تقييم أرصدة العملات وفروقات الصرف بتاريخ {command.AsOfDate:yyyy-MM-dd}"
                    : command.Description.Trim();

                var journalLines = BuildJournalLines(candidateItems, preview.BaseCurrencyCode, description, command.GainAccountId, command.LossAccountId);

                var journalScope = JournalEntryScope.Create(companyId, branchId, userId);
                var draft = await journalService.CreateDraftAsync(journalScope, new CreateJournalDraftCommand
                {
                    AccountingDate = command.AsOfDate,
                    Description = description,
                    Lines = journalLines
                }, cancellationToken).ConfigureAwait(false);

                // The session lock gives this branch a single workflow owner; this association commits with creation.
                await RecordDraftJournalIfAbsentAsync(companyId, branchId, idempotencyKey, draft.Id, cancellationToken).ConfigureAwait(false);
                var authoritative = await LoadRequestRowAsync(companyId, branchId, idempotencyKey, cancellationToken).ConfigureAwait(false);
                draftId = authoritative?.DraftJournalId ?? throw new BusinessRuleException("fx.reservation_failed", "The FX draft was not linked to its request.");
            }
            await preparation.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        var outcomeJournalId = await DriveDraftAndPostAsync(companyId, branchId, userId, draftId, postingKey, cancellationToken).ConfigureAwait(false);
        await CompleteRequestRowAsync(companyId, branchId, idempotencyKey, outcomeJournalId, cancellationToken).ConfigureAwait(false);
        return await BuildResultFromJournalAsync(outcomeJournalId, command, cancellationToken).ConfigureAwait(false);
    }

    private const byte FxRequestPending = 1;
    private const byte FxRequestCompleted = 2;

    // Drives a draft to Approved from whatever state it is in (idempotent by state) and posts it under the stable key. An
    // already-posted draft (a prior attempt that posted but crashed before completing the request) is returned as-is; the
    // durable posting request guarantees the journal is never posted twice.
    private async Task<Guid> DriveDraftAndPostAsync(Guid companyId, Guid branchId, Guid userId, Guid draftId, Guid postingKey, CancellationToken cancellationToken)
    {
        var scope = JournalEntryScope.Create(companyId, branchId, userId);
        var detail = await journalService.GetJournalAsync(scope, draftId, cancellationToken).ConfigureAwait(false);

        if (detail.State is (byte)JournalEntryState.Posted)
            return draftId;
        if (detail.State is (byte)JournalEntryState.Reversed)
            throw new BusinessRuleException("fx.draft_not_postable", "قيد إعادة التقييم في حالة لا تسمح بالترحيل.");

        var reason = detail.Description;

        if (detail.State is (byte)JournalEntryState.Draft)
        {
            detail = await journalService.ValidateDraftAsync(scope, draftId, new JournalDraftActionCommand
            {
                ExpectedVersion = ConcurrencyToken.FromBytes(Convert.FromBase64String(detail.VersionBase64)),
                Reason = reason
            }, cancellationToken).ConfigureAwait(false);
        }

        if (detail.State is (byte)JournalEntryState.Validated)
        {
            detail = await journalService.ApproveDraftAsync(scope, draftId, new JournalDraftActionCommand
            {
                ExpectedVersion = ConcurrencyToken.FromBytes(Convert.FromBase64String(detail.VersionBase64)),
                Reason = reason
            }, cancellationToken).ConfigureAwait(false);
        }

        var postingRequest = PostingRequest.Post(
            PostingRequestScope.Create(companyId, branchId, userId),
            postingKey,
            draftId,
            ExpectedJournalVersion.FromBase64(detail.VersionBase64),
            AuditReason.Create(reason));

        var postingService = new SqlPostingService(context);
        await postingService.PostAsync(postingRequest, cancellationToken).ConfigureAwait(false);
        return draftId;
    }

    // Builds the balanced revaluation lines: one line per revalued account, offset by the aggregate gain/loss control lines.
    private static List<JournalDraftLineInput> BuildJournalLines(
        IReadOnlyList<FxAccountRevaluationItemResult> items,
        string baseCurrency,
        string description,
        Guid gainAccountId,
        Guid lossAccountId)
    {
        var lines = new List<JournalDraftLineInput>();
        decimal totalGain = 0m;
        decimal totalLoss = 0m;

        foreach (var item in items)
        {
            var diff = item.UnrealizedDifference;
            if (diff == 0m) continue;

            if (diff > 0m)
            {
                totalGain += diff;
                lines.Add(new JournalDraftLineInput
                {
                    AccountId = item.AccountId,
                    TransactionCurrencyCode = baseCurrency,
                    TransactionDebit = diff,
                    TransactionCredit = 0m,
                    RateValue = 1.0m,
                    BaseDebit = diff,
                    BaseCredit = 0m,
                    Description = $"{description} - {item.AccountCode} ({item.CurrencyCode})"
                });
            }
            else
            {
                var absLoss = Math.Abs(diff);
                totalLoss += absLoss;
                lines.Add(new JournalDraftLineInput
                {
                    AccountId = item.AccountId,
                    TransactionCurrencyCode = baseCurrency,
                    TransactionDebit = 0m,
                    TransactionCredit = absLoss,
                    RateValue = 1.0m,
                    BaseDebit = 0m,
                    BaseCredit = absLoss,
                    Description = $"{description} - {item.AccountCode} ({item.CurrencyCode})"
                });
            }
        }

        if (totalGain > 0m)
        {
            lines.Add(new JournalDraftLineInput
            {
                AccountId = gainAccountId,
                TransactionCurrencyCode = baseCurrency,
                TransactionDebit = 0m,
                TransactionCredit = totalGain,
                RateValue = 1.0m,
                BaseDebit = 0m,
                BaseCredit = totalGain,
                Description = $"{description} - أرباح فروقات أسعار الصرف غير المحققة"
            });
        }

        if (totalLoss > 0m)
        {
            lines.Add(new JournalDraftLineInput
            {
                AccountId = lossAccountId,
                TransactionCurrencyCode = baseCurrency,
                TransactionDebit = totalLoss,
                TransactionCredit = 0m,
                RateValue = 1.0m,
                BaseDebit = totalLoss,
                BaseCredit = 0m,
                Description = $"{description} - خسائر فروقات أسعار الصرف غير المحققة"
            });
        }

        return lines;
    }

    // Prepares the centrally-verified local actor reference inside its own short SERIALIZABLE transaction, exactly as the
    // posting service does before durable request registration. Idempotent: a no-op once the actor reference exists.
    private async Task EnsureActorReferenceAsync(CancellationToken cancellationToken)
    {
        if (context is AccountingDbContext annual)
        {
            var preparation = await OwnedDatabaseTransaction.BeginAsync(context, IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
            await using (preparation.ConfigureAwait(false))
            {
                await annual.PrepareActorAsync(cancellationToken).ConfigureAwait(false);
                await preparation.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<FxRevaluationRequestRow?> LoadRequestRowAsync(Guid companyId, Guid branchId, string idempotencyKey, CancellationToken cancellationToken)
        => await context.FxRevaluationRequests.AsNoTracking()
            .FirstOrDefaultAsync(r => r.CompanyId == companyId && r.BranchId == branchId && r.IdempotencyKey == idempotencyKey, cancellationToken)
            .ConfigureAwait(false);

    // Durable reservation via a raw INSERT so the unique index (CompanyId, BranchId, IdempotencyKey) decides concurrency:
    // a duplicate key (2601/2627) means another caller reserved first and we fall onto the resume path. Returns true if we
    // inserted the reservation.
    private async Task<bool> TryReserveRequestRowAsync(Guid companyId, Guid branchId, string idempotencyKey, string fingerprintHex, Guid userId, CancellationToken cancellationToken)
    {
        // The reservation row carries an actor-reference FK; ensure the centrally-verified actor exists in this annual
        // database before inserting it (mirrors the posting service's actor preparation).
        await EnsureActorReferenceAsync(cancellationToken).ConfigureAwait(false);

        var fiscalYearId = await context.FiscalYears.AsNoTracking()
            .Where(y => y.CompanyId == companyId).Select(y => y.Id)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (fiscalYearId == Guid.Empty)
            throw new BusinessRuleException("fx.fiscal_year_unavailable", "تعذّر تحديد السنة المالية للمنشأة لإعادة التقييم.");

        try
        {
            await context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO [ledger].[FxRevaluationRequests]
                    ([Id], [CompanyId], [BranchId], [FiscalYearId], [IdempotencyKey], [FingerprintHex], [ActorUserId], [Status], [DraftJournalId], [OutcomeJournalId], [RegisteredAtUtc], [CompletedAtUtc])
                VALUES
                    ({Guid.NewGuid()}, {companyId}, {branchId}, {fiscalYearId}, {idempotencyKey}, {fingerprintHex}, {userId}, {FxRequestPending}, {(Guid?)null}, {(Guid?)null}, SYSUTCDATETIME(), {(DateTimeOffset?)null})",
                cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            return false;
        }
    }

    private async Task RecordDraftJournalIfAbsentAsync(Guid companyId, Guid branchId, string idempotencyKey, Guid draftJournalId, CancellationToken cancellationToken)
        => await context.Database.ExecuteSqlInterpolatedAsync($@"
                UPDATE [ledger].[FxRevaluationRequests]
                SET [DraftJournalId] = {draftJournalId}
                WHERE [CompanyId] = {companyId} AND [BranchId] = {branchId} AND [IdempotencyKey] = {idempotencyKey} AND [DraftJournalId] IS NULL",
            cancellationToken).ConfigureAwait(false);

    private async Task CompleteRequestRowAsync(Guid companyId, Guid branchId, string idempotencyKey, Guid outcomeJournalId, CancellationToken cancellationToken)
        => await context.Database.ExecuteSqlInterpolatedAsync($@"
                UPDATE [ledger].[FxRevaluationRequests]
                SET [Status] = {FxRequestCompleted}, [OutcomeJournalId] = {outcomeJournalId}, [CompletedAtUtc] = SYSUTCDATETIME()
                WHERE [CompanyId] = {companyId} AND [BranchId] = {branchId} AND [IdempotencyKey] = {idempotencyKey} AND [Status] = {FxRequestPending}",
            cancellationToken).ConfigureAwait(false);

    private static void EnsureFingerprintMatches(FxRevaluationRequestRow row, string fingerprintHex, string legacyFingerprintHex, string idempotencyKey)
    {
        // Preserve exact-content retries of receipts written before decimal/set normalization.
        if (!string.Equals(row.FingerprintHex, fingerprintHex, StringComparison.Ordinal)
            && !string.Equals(row.FingerprintHex, legacyFingerprintHex, StringComparison.Ordinal))
            throw new BusinessRuleException("fx.idempotency_conflict",
                $"مفتاح العملية '{idempotencyKey}' مُستخدم سابقاً بمحتوى مختلف لإعادة التقييم. / Idempotency key '{idempotencyKey}' was already used with a different FX revaluation request.");
    }

    private static Guid DerivePostingKey(Guid companyId, Guid branchId, string idempotencyKey)
        => new(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"PetraERP.Currencies.FxRevaluation:{companyId:N}:{branchId:N}:{idempotencyKey}"))[..16]);

    private static string ComputeRequestFingerprint(Guid companyId, Guid branchId, ExecuteFxRevaluationCommand command, bool legacy)
    {
        var selected = command.SelectedAccountIds is { Count: > 0 } ids
            ? string.Join(",", (legacy ? ids : ids.Distinct()).Select(i => i.ToString("D")).OrderBy(s => s, StringComparer.Ordinal))
            : string.Empty;

        string[] fields =
        [
            "PetraERP.FxRevaluation.v1",
            companyId.ToString("D"),
            branchId.ToString("D"),
            command.AsOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            (command.CurrencyCode ?? string.Empty).Trim().ToUpperInvariant(),
            command.OverrideClosingRate?.ToString(legacy ? null : "G29", CultureInfo.InvariantCulture) ?? string.Empty,
            command.GainAccountId.ToString("D"),
            command.LossAccountId.ToString("D"),
            selected,
            (command.Description ?? string.Empty).Trim(),
        ];

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[sizeof(int)];
        foreach (var field in fields)
        {
            var bytes = Encoding.UTF8.GetBytes(field);
            BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
            hash.AppendData(length);
            hash.AppendData(bytes);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    // Builds the execution result from the FX journal itself, so first execution, replay and resume all report identical
    // totals derived from the single source of truth — the posted journal lines.
    private async Task<FxRevaluationExecutionResult> BuildResultFromJournalAsync(Guid journalId, ExecuteFxRevaluationCommand command, CancellationToken cancellationToken)
    {
        var lines = await context.JournalLines.AsNoTracking().Where(l => l.JournalId == journalId)
            .Select(l => new { l.AccountId, l.BaseDebit, l.BaseCredit }).ToListAsync(cancellationToken).ConfigureAwait(false);
        var gain = lines.Where(l => l.AccountId == command.GainAccountId).Sum(l => l.BaseCredit);
        var loss = lines.Where(l => l.AccountId == command.LossAccountId).Sum(l => l.BaseDebit);
        var count = lines.Where(l => l.AccountId != command.GainAccountId && l.AccountId != command.LossAccountId)
            .Select(l => l.AccountId).Distinct().Count();
        return new FxRevaluationExecutionResult(journalId, journalId.ToString("N")[..8].ToUpperInvariant(), count, gain, loss, gain - loss);
    }
}
