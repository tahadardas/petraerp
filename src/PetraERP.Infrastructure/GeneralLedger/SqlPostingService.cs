using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.Posting;
using PetraERP.Application.Security;
using PetraERP.Domain.AccountingCalendars;
using PetraERP.Domain.FinancialValues;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Domain.Organizations;
using PetraERP.Domain.Primitives;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.GeneralLedger;

/// <summary>
/// SQL Server implementation of the authoritative durable posting service.
/// Adheres strictly to POSTING_TRANSACTION_CONTRACT.md:
/// 1. Short registration transaction for idempotency key binding and fingerprint comparison.
/// 2. Short explicit SERIALIZABLE financial transaction with strict lock ordering:
///    - Lock registered request row (recheck completion).
///    - Protect company and branch.
///    - Protect target fiscal period and year.
///    - Load journals for update in deterministic ID order and verify expected rowversion.
///    - Protect accounts and cost centers in deterministic ID order.
///    - Domain validation via JournalValidationContext.
///    - Atomic sequence increment via UPDATE with OUTPUT (UPDLOCK, HOLDLOCK).
///    - Update journal state(s), append immutable audit events, mark request completed.
///    - Commit before returning immutable PostingReceipt.
/// Sales composition is internal-only: the document owns a SERIALIZABLE transaction, request registration,
/// financial effects and receipt completion commit together, and the API acknowledges only after that outer commit.
/// </summary>
public sealed class SqlPostingService : IPostingService
{
    private readonly FinancialDbContext context;

    public SqlPostingService(FinancialDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        this.context = context;
    }

    public async Task<PostingReceipt> PostAsync(PostingRequest request, CancellationToken cancellationToken = default)
        => await PostCoreAsync(request, null, cancellationToken).ConfigureAwait(false);

    // Only the document orchestrator may compose posting; verify the journal association and Sales permission under lock.
    internal Task<PostingReceipt> PostSalesAsync(Guid invoiceId, PostingRequest request, CancellationToken cancellationToken)
        => PostCoreAsync(request, new PostingDocument(PostingDocumentKind.SalesInvoice, invoiceId), cancellationToken);

    // Treasury vouchers compose posting the same way: association and Treasury permission are verified under lock.
    internal Task<PostingReceipt> PostVoucherAsync(Guid voucherId, PostingRequest request, CancellationToken cancellationToken)
        => PostCoreAsync(request, new PostingDocument(PostingDocumentKind.TreasuryVoucher, voucherId), cancellationToken);

    // Purchase invoices and purchase returns: association plus Post (invoice) or Return (return document) permission under lock.
    internal Task<PostingReceipt> PostPurchaseAsync(Guid invoiceId, PostingRequest request, CancellationToken cancellationToken)
        => PostCoreAsync(request, new PostingDocument(PostingDocumentKind.PurchaseInvoice, invoiceId), cancellationToken);

    internal Task<PostingReceipt> PostInventoryAsync(Guid documentId, PostingRequest request, CancellationToken cancellationToken)
        => PostCoreAsync(request, new PostingDocument(PostingDocumentKind.InventoryDocument, documentId), cancellationToken);

    internal Task VerifyInventoryActorAsync(PetraERP.Application.Inventory.InventoryDocumentScope scope, string permission, CancellationToken token)
        => VerifyActorScopeAsync(PostingRequestScope.Create(scope.CompanyId, scope.BranchId, scope.UserId), permission, token);

    internal Task VerifySalesCreditOverrideActorAsync(PostingRequestScope scope, CancellationToken token)
        => VerifyActorScopeAsync(scope, PermissionCatalog.SalesCreditOverride, token);

    /// <summary>A secondary central grant for the authorized negative-issue exception; it is never a write operation of
    /// its own, so the posting service reports its absence as an actionable business rule rather than a raw 403.</summary>
    internal async Task VerifyInventoryNegativeActorAsync(PostingRequestScope scope, CancellationToken token)
    {
        try
        {
            await VerifyActorScopeAsync(scope, PermissionCatalog.InventoryNegativeApprove, token).ConfigureAwait(false);
        }
        catch (PostingAuthorizationException)
        {
            throw new BusinessRuleException("inventory.negative_override_denied",
                "لا يملك المستخدم صلاحية اعتماد الإخراج بالسالب في هذا الفرع؛ اطلب من مسؤول الصلاحيات منحها / The user lacks the negative-issue approval grant for this branch; ask a security administrator to grant it.");
        }
    }

    internal Task VerifyTreasuryActorAsync(Guid company, Guid branch, Guid actor, string permission, CancellationToken token)
        => VerifyActorScopeAsync(PostingRequestScope.Create(company, branch, actor), permission, token);

    internal Task<PostingReceipt> PostSettlementAsync(Guid batchId, PostingRequest request, CancellationToken token)
        => PostCoreAsync(request, new PostingDocument(PostingDocumentKind.Settlement, batchId), token);

    private async Task<PostingReceipt> PostCoreAsync(PostingRequest request, PostingDocument? document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (document is not null && (context.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable
            || context.ChangeTracker.HasChanges()))
            throw new InvalidOperationException("Sales posting requires persisted changes inside the document's SERIALIZABLE transaction.");
        if (document is null && context.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("Standalone ledger posting owns its financial transaction.");

        // Authorize before registration too: durable request rows must not be inserted by an unauthorized actor.
        await VerifyActorAuthorizationAsync(request, cancellationToken, document).ConfigureAwait(false);
        if (context is AccountingDbContext annual)
        {
            var preparation = await OwnedDatabaseTransaction.BeginAsync(context, IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
            await using (preparation.ConfigureAwait(false))
            {
                await annual.PrepareActorAsync(cancellationToken).ConfigureAwait(false);
                await preparation.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        // Standalone requests register durably first. Document registration belongs to its outer transaction.
        await RegisterOrVerifyRequestAsync(request, cancellationToken).ConfigureAwait(false);

        // Step 2: Check if already completed (replay).
        var existingRequest = await context.PostingRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.CompanyId == request.Scope.CompanyId
                                   && r.BranchId == request.Scope.BranchId
                                   && r.Operation == (byte)request.Operation
                                   && r.IdempotencyKey == request.IdempotencyKey, cancellationToken)
            .ConfigureAwait(false);

        if (existingRequest?.CompletedAtUtc is not null && existingRequest.OutcomeJournalId is not null)
        {
            await VerifyActorAuthorizationAsync(request, cancellationToken, document).ConfigureAwait(false);
            return await LoadCompletedReceiptAsync(request, existingRequest.OutcomeJournalId.Value, cancellationToken)
                .ConfigureAwait(false);
        }

        // Step 3: Financial transaction under SERIALIZABLE isolation.
        return await ExecuteFinancialTransactionAsync(request, document, cancellationToken).ConfigureAwait(false);
    }

    private async Task RegisterOrVerifyRequestAsync(PostingRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var expectedBase64 = request.ExpectedVersion.ToBase64();
            var originalExpectedBase64 = request.OriginalExpectedVersion?.ToBase64();

            await context.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO [ledger].[PostingRequests] (
                    [IdempotencyKey], [CompanyId], [BranchId], [ActorUserId], [Operation],
                    [JournalId], [ExpectedRevision], [ExpectedVersionBase64],
                    [OriginalJournalId], [OriginalExpectedRevision], [OriginalVersionBase64],
                    [Reason], [FingerprintHex], [RegisteredAtUtc]
                ) VALUES (
                    {request.IdempotencyKey}, {request.Scope.CompanyId}, {request.Scope.BranchId}, {request.Scope.ActorUserId}, {(byte)request.Operation},
                    {request.JournalId}, {1}, {expectedBase64},
                    {request.OriginalJournalId}, {(request.OriginalExpectedVersion is not null ? 1 : (long?)null)}, {originalExpectedBase64},
                    {request.Reason.Value}, {request.Fingerprint.Hex}, SYSUTCDATETIME()
                )", cancellationToken).ConfigureAwait(false);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            // Primary key already exists; verify fingerprint match.
            var existing = await context.PostingRequests
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.CompanyId == request.Scope.CompanyId
                                       && r.BranchId == request.Scope.BranchId
                                       && r.Operation == (byte)request.Operation
                                       && r.IdempotencyKey == request.IdempotencyKey, cancellationToken)
                .ConfigureAwait(false);

            if (existing is null)
            {
                throw;
            }

            if (!string.Equals(existing.FingerprintHex, request.Fingerprint.Hex, StringComparison.Ordinal))
            {
                throw new PostingIdempotencyConflictException(request.IdempotencyKey,
                    $"Idempotency key '{request.IdempotencyKey}' was previously registered with a different semantic fingerprint.");
            }
        }
    }

    private async Task<PostingReceipt> ExecuteFinancialTransactionAsync(
        PostingRequest request, PostingDocument? document, CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        var tx = await OwnedDatabaseTransaction
            .BeginAsync(context, IsolationLevel.Serializable, cancellationToken)
            .ConfigureAwait(false);

        await using (tx.ConfigureAwait(false))
        {
            // 1. Lock the registered request row and recheck if already completed concurrently.
            var requestStatus = await context.Database
                .SqlQuery<RequestStatusDto>($@"SELECT [CompletedAtUtc], [OutcomeJournalId]
                    FROM [ledger].[PostingRequests] WITH (UPDLOCK, HOLDLOCK)
                    WHERE [CompanyId] = {request.Scope.CompanyId}
                      AND [BranchId] = {request.Scope.BranchId}
                      AND [Operation] = {(byte)request.Operation}
                      AND [IdempotencyKey] = {request.IdempotencyKey}")
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (requestStatus?.CompletedAtUtc is not null && requestStatus.OutcomeJournalId is not null)
            {
                await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
                await VerifyActorAuthorizationAsync(request, cancellationToken, document).ConfigureAwait(false);
                return await LoadCompletedReceiptAsync(request, requestStatus.OutcomeJournalId.Value, cancellationToken)
                    .ConfigureAwait(false);
            }

            // 2. Protect security administration and verify actor authorization under lock.
            await context.Database.ExecuteSqlRawAsync(
                """
                DECLARE @lockResult int;
                EXEC @lockResult = sp_getapplock @Resource = N'PetraERP.Security.Administration', @LockMode = N'Shared',
                    @LockOwner = N'Transaction', @LockTimeout = 15000;
                IF @lockResult < 0 THROW 51010, N'The security administration lock could not be acquired.', 1;
                """, cancellationToken).ConfigureAwait(false);

            await VerifyActorAuthorizationAsync(request, cancellationToken, document).ConfigureAwait(false);

            // 3. Resolve and protect Company and Branch.
            var companyRow = await context.Companies
                .FromSqlInterpolated($"SELECT * FROM [system].[Companies] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {request.Scope.CompanyId}")
                .AsNoTracking()
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Company '{request.Scope.CompanyId}' does not exist.");

            if (!companyRow.IsActive)
            {
                throw RuleViolation.Create(RuleViolation.ScopeInactive, $"Company '{companyRow.Code}' is inactive.");
            }

            var branchRow = await context.Branches
                .FromSqlInterpolated($"SELECT * FROM [system].[Branches] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {request.Scope.BranchId} AND [CompanyId] = {request.Scope.CompanyId}")
                .AsNoTracking()
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Branch '{request.Scope.BranchId}' does not exist for company '{companyRow.Code}'.");

            if (!branchRow.IsActive)
            {
                throw RuleViolation.Create(RuleViolation.ScopeInactive, $"Branch '{branchRow.Code}' is inactive.");
            }

            var additionalCurrencies = await context.CompanyAdditionalCurrencies
                .AsNoTracking()
                .Where(c => c.CompanyId == request.Scope.CompanyId)
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

            // 3. Load affected journals for update in deterministic ID order.
            JournalEntryRow journalRow;
            JournalEntryRow? originalRow = null;

            if (request.Operation == PostingOperation.PostJournal)
            {
                journalRow = await context.JournalEntries
                    .FromSqlInterpolated($"SELECT * FROM [ledger].[JournalEntries] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {request.JournalId}")
                    .AsNoTracking()
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"Journal entry '{request.JournalId}' does not exist.");

                VerifyJournalOwnershipAndVersion(journalRow, request.Scope, request.ExpectedVersion);

                if (journalRow.State != (byte)JournalEntryState.Approved)
                {
                    throw RuleViolation.Create(RuleViolation.JournalStateInvalid, $"Journal entry must be in Approved state to post. Current state is {(JournalEntryState)journalRow.State}.");
                }
            }
            else
            {
                var reversalId = request.JournalId;
                var originalId = request.OriginalJournalId!.Value;

                var id1 = reversalId.CompareTo(originalId) < 0 ? reversalId : originalId;
                var id2 = reversalId.CompareTo(originalId) < 0 ? originalId : reversalId;

                var j1 = await context.JournalEntries
                    .FromSqlInterpolated($"SELECT * FROM [ledger].[JournalEntries] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id1}")
                    .AsNoTracking()
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                var j2 = await context.JournalEntries
                    .FromSqlInterpolated($"SELECT * FROM [ledger].[JournalEntries] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id2}")
                    .AsNoTracking()
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

                journalRow = (j1?.Id == reversalId ? j1 : j2)
                    ?? throw new InvalidOperationException($"Reversal journal '{reversalId}' does not exist.");
                originalRow = (j1?.Id == originalId ? j1 : j2)
                    ?? throw new InvalidOperationException($"Original journal '{originalId}' does not exist.");

                VerifyJournalOwnershipAndVersion(journalRow, request.Scope, request.ExpectedVersion);
                VerifyJournalOwnershipAndVersion(originalRow, request.Scope, request.OriginalExpectedVersion!);

                if (journalRow.State != (byte)JournalEntryState.Approved)
                {
                    throw RuleViolation.Create(RuleViolation.JournalStateInvalid, $"Reversal journal must be in Approved state to post. Current state is {(JournalEntryState)journalRow.State}.");
                }

                if (originalRow.State != (byte)JournalEntryState.Posted)
                {
                    throw RuleViolation.Create(RuleViolation.JournalStateInvalid, $"Original journal must be in Posted state to reverse. Current state is {(JournalEntryState)originalRow.State}.");
                }

                if (originalRow.ReversedByEntryId is not null)
                {
                    throw RuleViolation.Create(RuleViolation.PostingConflict, $"Original journal '{originalRow.Id}' has already been reversed by '{originalRow.ReversedByEntryId}'.");
                }
            }

            // 4. Resolve and protect the target fiscal period and year.
            var targetDate = journalRow.AccountingDate;
            var periodRow = await context.AccountingPeriods
                .FromSqlInterpolated($@"SELECT * FROM [ledger].[AccountingPeriods] WITH (UPDLOCK, HOLDLOCK)
                    WHERE [CompanyId] = {request.Scope.CompanyId}
                      AND [RangeStart] <= {targetDate}
                      AND [RangeEnd] >= {targetDate}")
                .AsNoTracking()
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false)
                ?? throw RuleViolation.Create(RuleViolation.PeriodMissing, $"No accounting period found for company '{companyRow.Code}' covering date {targetDate}.");

            if (periodRow.State != 0) // 0 = Open
            {
                throw RuleViolation.Create(RuleViolation.PeriodClosed, $"Accounting period '{periodRow.Code}' is not open for posting (State={(AccountingPeriodState)periodRow.State}).");
            }

            var fiscalYearRow = await context.FiscalYears
                .FromSqlInterpolated($"SELECT * FROM [ledger].[FiscalYears] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {periodRow.FiscalYearId}")
                .AsNoTracking()
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Fiscal year '{periodRow.FiscalYearId}' does not exist.");

            var allYearPeriods = await context.AccountingPeriods
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
                allYearPeriods.Select(p => AccountingPeriod.Reconstitute(
                    p.Id, p.CompanyId, p.FiscalYearId, EntityCode.Create(p.Code),
                    DateRange.Create(p.RangeStart, p.RangeEnd),
                    (AccountingPeriodState)p.State)));

            // 5. Load persisted lines and resolve accounts & cost centers in deterministic ID order.
            var journalLines = await context.JournalLines
                .AsNoTracking()
                .Where(l => l.JournalId == journalRow.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var companyPolicy = await PetraERP.Infrastructure.CompanyOptions.SqlCompanyOptionsService.EffectiveAsync(context,request.Scope.CompanyId,cancellationToken).ConfigureAwait(false);
            if(companyPolicy.RequireProfitLossCostCenter)
            {
                var missing = journalLines.Where(l=>l.CostCenterId is null).Select(l=>l.AccountId).Distinct().ToArray();
                if(await context.Accounts.AnyAsync(a=>missing.Contains(a.Id) && (a.AccountType == (byte)AccountType.Revenue || a.AccountType == (byte)AccountType.Expenses),cancellationToken).ConfigureAwait(false))
                    throw new PetraERP.Application.Common.BusinessRuleException("company.cost_center_required","Company policy requires a cost center on profit/loss journal lines.");
            }
            var accountIds = journalLines.Select(l => l.AccountId).Distinct().OrderBy(id => id).ToArray();
            var centerIds = journalLines.Where(l => l.CostCenterId.HasValue).Select(l => l.CostCenterId!.Value).Distinct().OrderBy(id => id).ToArray();

            var accountDomains = new List<Account>();
            foreach (var accountId in accountIds)
            {
                var accRow = await context.Accounts
                    .FromSqlInterpolated($"SELECT * FROM [ledger].[Accounts] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {accountId}")
                    .AsNoTracking()
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false)
                    ?? throw RuleViolation.Create(RuleViolation.AccountInvalid, $"Account '{accountId}' does not exist.");

                accountDomains.Add(Account.Reconstitute(
                    accRow.Id, accRow.CompanyId, EntityCode.Create(accRow.Code),
                    BilingualName.Create(accRow.NameArabic, accRow.NameEnglish),
                    (AccountType)accRow.AccountType, accRow.PostingAllowed,
                    (CostCenterPolicy)accRow.CostCenterPolicy,
                    accRow.FixedCurrencyCode is not null
                        ? AccountCurrencyPolicy.Fixed(CurrencyCode.Create(accRow.FixedCurrencyCode))
                        : (accRow.CurrencyMode == 1 ? AccountCurrencyPolicy.BaseOnly() : AccountCurrencyPolicy.AnyEnabled()),
                    accRow.ParentId, accRow.IsActive));
            }

            var centerDomains = new List<CostCenter>();
            foreach (var centerId in centerIds)
            {
                var ccRow = await context.CostCenters
                    .FromSqlInterpolated($"SELECT * FROM [ledger].[CostCenters] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {centerId}")
                    .AsNoTracking()
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false)
                    ?? throw RuleViolation.Create(RuleViolation.CostCenterInvalid, $"Cost center '{centerId}' does not exist.");

                centerDomains.Add(CostCenter.Reconstitute(
                    ccRow.Id, ccRow.CompanyId, EntityCode.Create(ccRow.Code),
                    BilingualName.Create(ccRow.NameArabic, ccRow.NameEnglish),
                    ccRow.IsActive));
            }

            // 6. Build domain validation context and execute domain posting logic.
            var validationContext = JournalValidationContext.Create(
                companyDomain,
                branchScope,
                fiscalYearDomain,
                accountDomains,
                centerDomains);

            var journalDomainLines = journalLines.Select(l => JournalLine.Reconstitute(
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
                (System.MidpointRounding)l.Rounding,
                l.Description,
                l.ReversesLineId,
                l.JournalId)).ToList();

            var journalAuditEvents = await context.JournalAudit
                .AsNoTracking()
                .Where(a => a.JournalId == journalRow.Id)
                .OrderBy(a => a.Revision)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var journalDomainAudits = journalAuditEvents.Select(a => JournalAuditEvent.Reconstitute(
                a.EventId, a.CompanyId, a.BranchId, a.JournalId, a.Revision,
                (JournalAuditAction)a.Action,
                a.PreviousState.HasValue ? (JournalEntryState)a.PreviousState.Value : null,
                (JournalEntryState)a.NewState,
                JournalActionContext.Create(a.ActorUserId, a.CorrelationId, a.Reason, a.OccurredAtUtc),
                a.RelatedEntryId)).ToList();

            var journalDomain = JournalEntry.Reconstitute(
                journalRow.Id, journalRow.CompanyId, journalRow.BranchId,
                journalRow.AccountingDate, journalRow.Description,
                journalDomainLines, (JournalEntryState)journalRow.State,
                journalDomainAudits,
                journalRow.PostingSequence.HasValue
                    ? JournalPostingNumber.Reconstitute(journalRow.CompanyId, journalRow.BranchId, journalRow.PostingFiscalYearId!.Value, journalRow.PostingSequence.Value)
                    : null,
                journalRow.PostingPeriodId, journalRow.ReversesEntryId,
                journalRow.ReversedByEntryId, journalRow.CorrectsEntryId);

            // 7. Atomic sequence allocation for company + branch + fiscal year.
            var allocatedSequence = await AllocateNextSequenceAsync(
                request.Scope.CompanyId, request.Scope.BranchId, fiscalYearRow.Id, cancellationToken)
                .ConfigureAwait(false);

            var postingNumber = JournalPostingNumber.Create(
                branchScope,
                fiscalYearDomain,
                allocatedSequence);

            var actionCorrelationId = Guid.NewGuid();
            var actionTimeUtc = DateTimeOffset.UtcNow;
            var actionContext = JournalActionContext.Create(
                request.Scope.ActorUserId, actionCorrelationId, request.Reason.Value, actionTimeUtc);

            PostingReceipt receipt;
            if (request.Operation == PostingOperation.PostJournal)
            {
                var postedEntry = journalDomain.Post(validationContext, postingNumber, actionContext);
                receipt = PostingReceipt.ForPost(request, postedEntry);

                // Persist journal state update.
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $@"UPDATE [ledger].[JournalEntries]
                        SET [State] = {(byte)JournalEntryState.Posted},
                            [PostingSequence] = {allocatedSequence},
                            [PostingFiscalYearId] = {fiscalYearRow.Id},
                            [PostingPeriodId] = {periodRow.Id},
                            [UpdatedAtUtc] = SYSUTCDATETIME()
                        WHERE [Id] = {journalRow.Id}", cancellationToken).ConfigureAwait(false);

                // Persist audit event.
                var lastAudit = postedEntry.Changes[^1];
                await InsertAuditEventAsync(lastAudit, actionCorrelationId, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // Reversal: load original domain entry.
                var originalLines = await context.JournalLines
                    .AsNoTracking()
                    .Where(l => l.JournalId == originalRow!.Id)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var originalAudits = await context.JournalAudit
                    .AsNoTracking()
                    .Where(a => a.JournalId == originalRow!.Id)
                    .OrderBy(a => a.Revision)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var originalDomainLines = originalLines.Select(l => JournalLine.Reconstitute(
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
                    (System.MidpointRounding)l.Rounding,
                    l.Description,
                    l.ReversesLineId,
                    l.JournalId)).ToList();

                var originalDomainAudits = originalAudits.Select(a => JournalAuditEvent.Reconstitute(
                    a.EventId, a.CompanyId, a.BranchId, a.JournalId, a.Revision,
                    (JournalAuditAction)a.Action,
                    a.PreviousState.HasValue ? (JournalEntryState)a.PreviousState.Value : null,
                    (JournalEntryState)a.NewState,
                    JournalActionContext.Create(a.ActorUserId, a.CorrelationId, a.Reason, a.OccurredAtUtc),
                    a.RelatedEntryId)).ToList();

                var originalDomain = JournalEntry.Reconstitute(
                    originalRow!.Id, originalRow.CompanyId, originalRow.BranchId,
                    originalRow.AccountingDate, originalRow.Description,
                    originalDomainLines, (JournalEntryState)originalRow.State,
                    originalDomainAudits,
                    JournalPostingNumber.Reconstitute(originalRow.CompanyId, originalRow.BranchId, originalRow.PostingFiscalYearId!.Value, originalRow.PostingSequence!.Value),
                    originalRow.PostingPeriodId, originalRow.ReversesEntryId,
                    originalRow.ReversedByEntryId, originalRow.CorrectsEntryId);

                var reversalResult = journalDomain.PostReversal(originalDomain, validationContext, postingNumber, actionContext);
                receipt = PostingReceipt.ForReversal(request, reversalResult);

                // Persist reversal journal update.
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $@"UPDATE [ledger].[JournalEntries]
                        SET [State] = {(byte)JournalEntryState.Posted},
                            [PostingSequence] = {allocatedSequence},
                            [PostingFiscalYearId] = {fiscalYearRow.Id},
                            [PostingPeriodId] = {periodRow.Id},
                            [ReversesEntryId] = {originalRow.Id},
                            [UpdatedAtUtc] = SYSUTCDATETIME()
                        WHERE [Id] = {journalRow.Id}", cancellationToken).ConfigureAwait(false);

                // Persist original journal update to Reversed.
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $@"UPDATE [ledger].[JournalEntries]
                        SET [State] = {(byte)JournalEntryState.Reversed},
                            [ReversedByEntryId] = {journalRow.Id},
                            [UpdatedAtUtc] = SYSUTCDATETIME()
                        WHERE [Id] = {originalRow.Id}", cancellationToken).ConfigureAwait(false);

                // Persist both audit events.
                var reversalAudit = reversalResult.ReversalEntry.Changes[^1];
                var originalAudit = reversalResult.OriginalEntry.Changes[^1];
                await InsertAuditEventAsync(reversalAudit, actionCorrelationId, cancellationToken).ConfigureAwait(false);
                await InsertAuditEventAsync(originalAudit, actionCorrelationId, cancellationToken).ConfigureAwait(false);
            }

            // 8. Mark posting request Completed.
            await context.Database.ExecuteSqlInterpolatedAsync(
                $@"UPDATE [ledger].[PostingRequests]
                    SET [CompletedAtUtc] = SYSUTCDATETIME(),
                        [OutcomeJournalId] = {journalRow.Id}
                    WHERE [CompanyId] = {request.Scope.CompanyId}
                      AND [BranchId] = {request.Scope.BranchId}
                      AND [Operation] = {(byte)request.Operation}
                      AND [IdempotencyKey] = {request.IdempotencyKey}", cancellationToken)
                .ConfigureAwait(false);

            // 9. Commit transaction before returning success acknowledgement.
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);

            return receipt;
        }
    }

    private async Task<long> AllocateNextSequenceAsync(
        Guid companyId, Guid branchId, Guid fiscalYearId, CancellationToken cancellationToken)
    {
        // Atomic increment of existing sequence row.
        var updated = await context.Database
            .SqlQuery<long>($@"UPDATE [ledger].[PostingSequences] WITH (UPDLOCK, HOLDLOCK)
                SET [LastSequence] = [LastSequence] + 1, [UpdatedAtUtc] = SYSUTCDATETIME()
                OUTPUT INSERTED.[LastSequence] AS [Value]
                WHERE [CompanyId] = {companyId} AND [BranchId] = {branchId} AND [FiscalYearId] = {fiscalYearId}")
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (updated.Count > 0)
        {
            return updated[0];
        }

        // Row doesn't exist yet: insert initial row with sequence 1.
        try
        {
            var inserted = await context.Database
                .SqlQuery<long>($@"INSERT INTO [ledger].[PostingSequences]
                    ([CompanyId], [BranchId], [FiscalYearId], [LastSequence], [CreatedAtUtc], [UpdatedAtUtc])
                    OUTPUT INSERTED.[LastSequence] AS [Value]
                    VALUES ({companyId}, {branchId}, {fiscalYearId}, 1, SYSUTCDATETIME(), SYSUTCDATETIME())")
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return inserted[0];
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            // Another thread inserted concurrently; increment it now.
            var retry = await context.Database
                .SqlQuery<long>($@"UPDATE [ledger].[PostingSequences] WITH (UPDLOCK, HOLDLOCK)
                    SET [LastSequence] = [LastSequence] + 1, [UpdatedAtUtc] = SYSUTCDATETIME()
                    OUTPUT INSERTED.[LastSequence] AS [Value]
                    WHERE [CompanyId] = {companyId} AND [BranchId] = {branchId} AND [FiscalYearId] = {fiscalYearId}")
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return retry[0];
        }
    }

    private async Task InsertAuditEventAsync(JournalAuditEvent audit, Guid correlationId, CancellationToken cancellationToken)
    {
        await context.Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT INTO [ledger].[JournalAudit] (
                [EventId], [CompanyId], [BranchId], [JournalId], [Revision],
                [Action], [PreviousState], [NewState], [ActorUserId], [CorrelationId],
                [Reason], [OccurredAtUtc], [RelatedEntryId], [CreatedAtUtc]
            ) VALUES (
                {audit.EventId}, {audit.CompanyId}, {audit.BranchId}, {audit.JournalId}, {audit.Revision},
                {(byte)audit.Action}, {(audit.PreviousState.HasValue ? (byte)audit.PreviousState.Value : (byte?)null)}, {(byte)audit.NewState},
                {audit.Context.ActorUserId}, {correlationId},
                {audit.Context.Reason}, {audit.Context.OccurredAtUtc}, {audit.RelatedEntryId}, SYSUTCDATETIME()
            )", cancellationToken).ConfigureAwait(false);
    }

    private async Task<PostingReceipt> LoadCompletedReceiptAsync(
        PostingRequest request, Guid outcomeJournalId, CancellationToken cancellationToken)
    {
        var journalRow = await context.JournalEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == outcomeJournalId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Outcome journal '{outcomeJournalId}' could not be loaded.");

        var lines = await context.JournalLines
            .AsNoTracking()
            .Where(l => l.JournalId == outcomeJournalId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var audits = await context.JournalAudit
            .AsNoTracking()
            .Where(a => a.JournalId == outcomeJournalId)
            .OrderBy(a => a.Revision)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

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
            (System.MidpointRounding)l.Rounding,
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

        var postedEntry = JournalEntry.Reconstitute(
            journalRow.Id, journalRow.CompanyId, journalRow.BranchId,
            journalRow.AccountingDate, journalRow.Description,
            domainLines, (JournalEntryState)journalRow.State,
            domainAudits,
            JournalPostingNumber.Reconstitute(journalRow.CompanyId, journalRow.BranchId, journalRow.PostingFiscalYearId!.Value, journalRow.PostingSequence!.Value),
            journalRow.PostingPeriodId, journalRow.ReversesEntryId,
            journalRow.ReversedByEntryId, journalRow.CorrectsEntryId);

        if (request.Operation == PostingOperation.PostJournal)
        {
            return PostingReceipt.ForPost(request, postedEntry);
        }

        // Reversal receipt: load original
        var originalId = request.OriginalJournalId!.Value;
        var originalRow = await context.JournalEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == originalId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Original journal '{originalId}' could not be loaded.");

        var originalLines = await context.JournalLines
            .AsNoTracking()
            .Where(l => l.JournalId == originalId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var originalAudits = await context.JournalAudit
            .AsNoTracking()
            .Where(a => a.JournalId == originalId)
            .OrderBy(a => a.Revision)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var origDomainLines = originalLines.Select(l => JournalLine.Reconstitute(
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
            (System.MidpointRounding)l.Rounding,
            l.Description,
            l.ReversesLineId,
            l.JournalId)).ToList();

        var origDomainAudits = originalAudits.Select(a => JournalAuditEvent.Reconstitute(
            a.EventId, a.CompanyId, a.BranchId, a.JournalId, a.Revision,
            (JournalAuditAction)a.Action,
            a.PreviousState.HasValue ? (JournalEntryState)a.PreviousState.Value : null,
            (JournalEntryState)a.NewState,
            JournalActionContext.Create(a.ActorUserId, a.CorrelationId, a.Reason, a.OccurredAtUtc),
            a.RelatedEntryId)).ToList();

        var originalDomain = JournalEntry.Reconstitute(
            originalRow.Id, originalRow.CompanyId, originalRow.BranchId,
            originalRow.AccountingDate, originalRow.Description,
            origDomainLines, (JournalEntryState)originalRow.State,
            origDomainAudits,
            JournalPostingNumber.Reconstitute(originalRow.CompanyId, originalRow.BranchId, originalRow.PostingFiscalYearId!.Value, originalRow.PostingSequence!.Value),
            originalRow.PostingPeriodId, originalRow.ReversesEntryId,
            originalRow.ReversedByEntryId, originalRow.CorrectsEntryId);

        return PostingReceipt.ForReversal(request, JournalReversal.Reconstitute(originalDomain, postedEntry));
    }

    private async Task VerifyActorAuthorizationAsync(PostingRequest request, CancellationToken cancellationToken, PostingDocument? document = null)
    {
        var requiredPermission = request.Operation == PostingOperation.PostJournal
            ? PermissionCatalog.JournalsPost
            : PermissionCatalog.JournalsReverse;

        if (document is { Kind: PostingDocumentKind.SalesInvoice, Id: var salesId })
        {
            var linkedJournal = request.Operation == PostingOperation.PostJournal ? request.JournalId : request.OriginalJournalId;
            if (!await context.SalesInvoices.AsNoTracking().AnyAsync(i => i.Id == salesId
                && i.CompanyId == request.Scope.CompanyId && i.BranchId == request.Scope.BranchId
                && i.JournalEntryId == linkedJournal, cancellationToken).ConfigureAwait(false))
                throw new PostingAuthorizationException("The journal is not associated with this sales invoice and scope.");
            requiredPermission = request.Operation == PostingOperation.PostJournal
                ? PermissionCatalog.SalesInvoicesPost : PermissionCatalog.SalesInvoicesReverse;
        }
        else if (document is { Kind: PostingDocumentKind.TreasuryVoucher, Id: var voucherId })
        {
            // Posting links the voucher's journal; reversal links the original journal and the pending reversal journal.
            var associated = request.Operation == PostingOperation.PostJournal
                ? await context.Vouchers.AsNoTracking().AnyAsync(v => v.Id == voucherId && v.CompanyId == request.Scope.CompanyId
                    && v.BranchId == request.Scope.BranchId && v.JournalEntryId == request.JournalId, cancellationToken).ConfigureAwait(false)
                : await context.Vouchers.AsNoTracking().AnyAsync(v => v.Id == voucherId && v.CompanyId == request.Scope.CompanyId
                    && v.BranchId == request.Scope.BranchId && v.JournalEntryId == request.OriginalJournalId
                    && v.ReversalJournalEntryId == request.JournalId, cancellationToken).ConfigureAwait(false);
            if (!associated)
                throw new PostingAuthorizationException("The journal is not associated with this voucher and scope.");
            requiredPermission = request.Operation == PostingOperation.PostJournal
                ? PermissionCatalog.TreasuryVouchersPost : PermissionCatalog.TreasuryVouchersReverse;
        }
        else if (document is { Kind: PostingDocumentKind.Settlement, Id: var settlementId })
        {
            var associated = await context.SettlementBatches.AsNoTracking().AnyAsync(b => b.Id == settlementId && !b.IsInitial
                && b.CompanyId == request.Scope.CompanyId && b.BranchId == request.Scope.BranchId
                && (request.Operation == PostingOperation.PostJournal ? b.JournalId == request.JournalId
                    : b.JournalId == request.OriginalJournalId && b.ReversalJournalId == request.JournalId), cancellationToken).ConfigureAwait(false);
            if (!associated) throw new PostingAuthorizationException("The journal is not associated with this settlement and scope.");
            requiredPermission = request.Operation == PostingOperation.PostJournal
                ? PermissionCatalog.TreasuryVouchersAllocate : PermissionCatalog.TreasuryVouchersReverse;
        }
        else if (document is { Kind: PostingDocumentKind.PurchaseInvoice, Id: var purchaseId })
        {
            // A purchase reversal posts the mirrored journal of the original document; the association is checked in both directions so
            // neither the original's journal nor the reversal journal can be swapped for another document's.
            var purchase = request.Operation == PostingOperation.PostJournal
                ? await context.PurchaseInvoices.AsNoTracking().Where(i => i.Id == purchaseId && i.CompanyId == request.Scope.CompanyId
                    && i.BranchId == request.Scope.BranchId && i.JournalEntryId == request.JournalId)
                    .Select(i => new { i.ReturnOfInvoiceId }).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)
                : await context.PurchaseInvoices.AsNoTracking().Where(i => i.Id == purchaseId && i.CompanyId == request.Scope.CompanyId
                    && i.BranchId == request.Scope.BranchId && i.JournalEntryId == request.OriginalJournalId
                    && context.PurchaseInvoices.Any(r => r.ReversesInvoiceId == i.Id && r.JournalEntryId == request.JournalId))
                    .Select(i => new { i.ReturnOfInvoiceId }).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (purchase is null)
                throw new PostingAuthorizationException("The journal is not associated with this purchase document and scope.");
            // Reversal is a posting act on the purchase ledger; the same grant that posted the original is what reverses it. A dedicated
            // Purchases.Invoices.Reverse grant would need permission-seed migrations in every schema and is tracked separately.
            requiredPermission = request.Operation == PostingOperation.PostJournal
                ? purchase.ReturnOfInvoiceId is null ? PermissionCatalog.PurchaseInvoicesPost : PermissionCatalog.PurchaseInvoicesReturn
                : PermissionCatalog.PurchaseInvoicesPost;
        }

        if (document is { Kind: PostingDocumentKind.InventoryDocument, Id: var inventoryId })
        {
            var linked = request.Operation == PostingOperation.PostJournal ? request.JournalId : request.OriginalJournalId;
            var stock = await context.InventoryDocuments.AsNoTracking().Where(d => d.Id == inventoryId && d.CompanyId == request.Scope.CompanyId
                && d.BranchId == request.Scope.BranchId && d.JournalId == linked
                && (request.Operation == PostingOperation.PostJournal || d.ReversalJournalId == request.JournalId))
                .Select(d => new { d.Kind }).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (stock is null) throw new PostingAuthorizationException("The journal is not associated with this inventory document and scope.");
            requiredPermission = stock.Kind == (int)PetraERP.Domain.Inventory.InventoryDocumentKind.Transfer
                ? PermissionCatalog.InventoryTransfersManage : PermissionCatalog.InventoryAdjustmentsManage;
        }
        // A composed FX revaluation posts a standalone journal (no linked document); its authorized permission is the
        // dedicated FX execute permission, so a dedicated FX operator needs neither Journals.Post nor Accounts.Manage.
        if (document is null && context is AccountingDbContext fxAnnual
            && fxAnnual.BoundOperation == PetraERP.Infrastructure.Persistence.Actors.AccountingOperation.ExecuteFxRevaluation)
            requiredPermission = PermissionCatalog.FxRevaluationExecute;
        await VerifyActorScopeAsync(request.Scope, requiredPermission, cancellationToken).ConfigureAwait(false);
    }

    internal Task VerifySalesActorAsync(PostingRequestScope scope, PostingOperation operation, CancellationToken cancellationToken)
        => VerifyActorScopeAsync(scope, operation == PostingOperation.PostJournal ? PermissionCatalog.SalesInvoicesPost : PermissionCatalog.SalesInvoicesReverse, cancellationToken);

    private async Task VerifyActorScopeAsync(PostingRequestScope scope, string requiredPermission, CancellationToken cancellationToken)
    {
        if (context is AccountingDbContext annual) annual.ValidateRequiredPermission(requiredPermission);
        var actorFacts = await context.Identity.Users
            .AsNoTracking()
            .Where(u => u.Id == scope.ActorUserId)
            .Select(u => new
            {
                u.IsActive,
                u.MustChangePassword,
                HasPermission = context.Identity.UserRoles.Any(ur => ur.UserId == u.Id
                    && context.Identity.RolePermissions.Any(rp => rp.RoleId == ur.RoleId && rp.PermissionCode == requiredPermission)),
                HasCompanyScope = context.Identity.UserCompanyScopes.Any(cs => cs.UserId == u.Id && cs.CompanyId == scope.CompanyId),
                HasBranchScope = context.Identity.UserBranchScopes.Any(bs => bs.UserId == u.Id && bs.CompanyId == scope.CompanyId && bs.BranchId == scope.BranchId),
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (actorFacts is null || !actorFacts.IsActive || actorFacts.MustChangePassword)
        {
            throw new PostingAuthorizationException("Actor user is inactive or does not exist.");
        }

        if (!actorFacts.HasPermission)
        {
            throw new PostingAuthorizationException($"Actor user lacks required permission '{requiredPermission}'.");
        }

        if (!actorFacts.HasCompanyScope)
        {
            throw new PostingAuthorizationException($"Actor user lacks scope grant for company '{scope.CompanyId}'.");
        }

        if (!actorFacts.HasBranchScope)
        {
            throw new PostingAuthorizationException($"Actor user lacks scope grant for branch '{scope.BranchId}'.");
        }
    }

    private static void VerifyJournalOwnershipAndVersion(
        JournalEntryRow row, PostingRequestScope scope, ExpectedJournalVersion expectedVersion)
    {
        if (row.CompanyId != scope.CompanyId || row.BranchId != scope.BranchId)
        {
            throw new ArgumentException($"Journal '{row.Id}' does not belong to the requested company/branch scope.");
        }

        var currentVersionBase64 = Convert.ToBase64String(row.RowVersion);
        if (!string.Equals(currentVersionBase64, expectedVersion.ToBase64(), StringComparison.Ordinal))
        {
            throw new PostingConcurrencyException(row.Id,
                $"Journal entry '{row.Id}' version '{currentVersionBase64}' does not match expected version '{expectedVersion.ToBase64()}'.");
        }
    }

    private sealed class RequestStatusDto
    {
        public DateTimeOffset? CompletedAtUtc { get; set; }
        public Guid? OutcomeJournalId { get; set; }
    }
}

internal enum PostingDocumentKind
{
    SalesInvoice = 1,
    TreasuryVoucher = 2,
    PurchaseInvoice = 3,
    InventoryDocument = 4,
    Settlement = 5,
}

/// <summary>The business document that composes a canonical posting inside its own SERIALIZABLE transaction.</summary>
internal sealed record PostingDocument(PostingDocumentKind Kind, Guid Id);
