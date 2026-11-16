using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger;
using PetraERP.Application.GeneralLedger.OpeningBalances;
using PetraERP.Application.GeneralLedger.Posting;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Domain.Primitives;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.GeneralLedger.OpeningBalances;

public sealed class SqlOpeningBalanceService(FinancialDbContext context, TimeProvider timeProvider) : IOpeningBalanceService
{
    public async Task<OpeningBalanceBatchDto> CreateDraftAsync(Guid companyId, Guid fiscalYearId, Guid actorId, CreateOpeningBalanceBatchCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
            throw new RequestValidationException("opening_balances.idempotency_required", "IdempotencyKey is required.", nameof(command.IdempotencyKey));

        var existing = await context.OpeningBalanceBatches
            .Include(b => b.Lines)
            .SingleOrDefaultAsync(b => b.CompanyId == companyId && b.IdempotencyKey == command.IdempotencyKey, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
            return await MapBatchAsync(existing, cancellationToken).ConfigureAwait(false);

        var company = await context.Companies.AsNoTracking().SingleOrDefaultAsync(c => c.Id == companyId, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("Company");

        var fiscalYear = await context.FiscalYears.AsNoTracking().SingleOrDefaultAsync(y => y.Id == fiscalYearId && y.CompanyId == companyId, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("FiscalYear");

        if (command.BranchId.HasValue && !await context.Branches.AsNoTracking().AnyAsync(b => b.Id == command.BranchId.Value && b.CompanyId == companyId, cancellationToken).ConfigureAwait(false))
            throw new BusinessRuleException("opening_balances.branch_invalid", "Branch does not belong to company.");

        if (command.Lines.Count < 2)
            throw new BusinessRuleException("opening_balances.min_lines", "At least two lines are required for an opening balance batch.");

        var accountIds = command.Lines.Select(l => l.AccountId).Distinct().ToArray();
        var accounts = await context.Accounts.AsNoTracking().Where(a => accountIds.Contains(a.Id) && a.CompanyId == companyId).ToDictionaryAsync(a => a.Id, cancellationToken).ConfigureAwait(false);
        foreach (var id in accountIds)
        {
            if (!accounts.TryGetValue(id, out var acc) || !acc.IsActive || !acc.PostingAllowed)
                throw new BusinessRuleException("opening_balances.account_invalid", $"Account {id} is not an active posting account.");
        }

        var now = timeProvider.GetUtcNow();
        var batch = new OpeningBalanceBatchRow
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            FiscalYearId = fiscalYearId,
            BranchId = command.BranchId,
            OpeningDate = command.OpeningDate,
            SourceType = command.SourceType,
            SourceReference = command.SourceReference,
            SourceFingerprint = command.SourceFingerprint,
            OriginalCurrency = command.OriginalCurrency,
            BaseCurrency = command.BaseCurrency,
            ApprovalState = (byte)OpeningBalanceBatchState.Draft,
            ActorId = actorId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            IdempotencyKey = command.IdempotencyKey,
        };

        var order = 0;
        foreach (var l in command.Lines)
        {
            if (l.DebitOriginal < 0 || l.CreditOriginal < 0 || (l.DebitOriginal == 0 && l.CreditOriginal == 0) || (l.DebitOriginal > 0 && l.CreditOriginal > 0))
                throw new BusinessRuleException("opening_balances.line_amount_invalid", "Line must have either positive debit or positive credit.");
            if (l.ExchangeRate <= 0)
                throw new BusinessRuleException("opening_balances.rate_invalid", "Exchange rate must be positive.");

            var debitBase = l.DebitBase > 0 ? l.DebitBase : (l.DebitOriginal > 0 ? decimal.Round(l.DebitOriginal * l.ExchangeRate, 4, MidpointRounding.ToEven) : 0m);
            var creditBase = l.CreditBase > 0 ? l.CreditBase : (l.CreditOriginal > 0 ? decimal.Round(l.CreditOriginal * l.ExchangeRate, 4, MidpointRounding.ToEven) : 0m);

            batch.Lines.Add(new OpeningBalanceLineRow
            {
                Id = Guid.NewGuid(),
                BatchId = batch.Id,
                AccountId = l.AccountId,
                CostCenterId = l.CostCenterId,
                CurrencyCode = l.CurrencyCode,
                DebitOriginal = l.DebitOriginal,
                CreditOriginal = l.CreditOriginal,
                ExchangeRate = l.ExchangeRate,
                DebitBase = debitBase,
                CreditBase = creditBase,
                Description = l.Description,
                LineOrder = order++,
            });
        }

        var totalDebit = batch.Lines.Sum(l => l.DebitBase);
        var totalCredit = batch.Lines.Sum(l => l.CreditBase);
        if (totalDebit == totalCredit)
        {
            batch.ApprovalState = (byte)OpeningBalanceBatchState.Validated;

            var branchId = await BookingBranchAsync(batch, companyId, cancellationToken).ConfigureAwait(false);
            var scope = JournalEntryScope.Create(companyId, branchId, actorId);
            var journalService = new SqlJournalEntryService(context, timeProvider);
            var journalLines = batch.Lines.OrderBy(l => l.LineOrder).Select(l => new JournalDraftLineInput
            {
                AccountId = l.AccountId,
                CostCenterId = l.CostCenterId,
                TransactionCurrencyCode = l.CurrencyCode,
                RateSourceCurrencyCode = l.CurrencyCode,
                RateTargetCurrencyCode = batch.BaseCurrency,
                RateValue = l.ExchangeRate,
                TransactionDebit = l.DebitOriginal,
                TransactionCredit = l.CreditOriginal,
                Description = l.Description ?? $"رصيد افتتاحي / Opening balance — {batch.SourceType}",
            }).ToList();

            var journal = await journalService.CreateDraftAsync(scope, new CreateJournalDraftCommand
            {
                AccountingDate = batch.OpeningDate,
                Description = $"قيد افتتاحي / Opening balance — {batch.SourceType} — {batch.SourceReference ?? batch.IdempotencyKey}",
                Lines = journalLines,
            }, cancellationToken).ConfigureAwait(false);

            await journalService.ValidateDraftAsync(scope, journal.Id, new JournalDraftActionCommand
            {
                ExpectedVersion = ConcurrencyToken.FromBytes(Convert.FromBase64String(journal.VersionBase64)),
                Reason = "تأكيد صحة قيد الرصيد الافتتاحي",
            }, cancellationToken).ConfigureAwait(false);

            batch.PostedJournalId = journal.Id;
        }

        context.OpeningBalanceBatches.Add(batch);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await MapBatchAsync(batch, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OpeningBalanceBatchDto> ValidateAsync(Guid companyId, Guid batchId, CancellationToken cancellationToken = default)
    {
        var batch = await context.OpeningBalanceBatches
            .Include(b => b.Lines)
            .SingleOrDefaultAsync(b => b.Id == batchId && b.CompanyId == companyId, cancellationToken)
            .ConfigureAwait(false) ?? throw new ResourceNotFoundException("OpeningBalanceBatch");

        var totalDebit = batch.Lines.Sum(l => l.DebitBase);
        var totalCredit = batch.Lines.Sum(l => l.CreditBase);
        if (totalDebit != totalCredit)
            throw new BusinessRuleException("opening_balances.unbalanced", $"Opening balance batch is not balanced. Debit: {totalDebit}, Credit: {totalCredit}, Difference: {totalDebit - totalCredit}");

        batch.ApprovalState = (byte)OpeningBalanceBatchState.Validated;
        batch.UpdatedAtUtc = timeProvider.GetUtcNow();

        if (!batch.PostedJournalId.HasValue)
        {
            var branchId = await BookingBranchAsync(batch, companyId, cancellationToken).ConfigureAwait(false);
            var scope = JournalEntryScope.Create(companyId, branchId, batch.ActorId);
            var journalService = new SqlJournalEntryService(context, timeProvider);
            var journalLines = batch.Lines.OrderBy(l => l.LineOrder).Select(l => new JournalDraftLineInput
            {
                AccountId = l.AccountId,
                CostCenterId = l.CostCenterId,
                TransactionCurrencyCode = l.CurrencyCode,
                RateSourceCurrencyCode = l.CurrencyCode,
                RateTargetCurrencyCode = batch.BaseCurrency,
                RateValue = l.ExchangeRate,
                TransactionDebit = l.DebitOriginal,
                TransactionCredit = l.CreditOriginal,
                Description = l.Description ?? $"رصيد افتتاحي / Opening balance — {batch.SourceType}",
            }).ToList();

            var journal = await journalService.CreateDraftAsync(scope, new CreateJournalDraftCommand
            {
                AccountingDate = batch.OpeningDate,
                Description = $"قيد افتتاحي / Opening balance — {batch.SourceType} — {batch.SourceReference ?? batch.IdempotencyKey}",
                Lines = journalLines,
            }, cancellationToken).ConfigureAwait(false);

            await journalService.ValidateDraftAsync(scope, journal.Id, new JournalDraftActionCommand
            {
                ExpectedVersion = ConcurrencyToken.FromBytes(Convert.FromBase64String(journal.VersionBase64)),
                Reason = "تأكيد صحة قيد الرصيد الافتتاحي",
            }, cancellationToken).ConfigureAwait(false);

            batch.PostedJournalId = journal.Id;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await MapBatchAsync(batch, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OpeningBalanceBatchDto> ApproveAsync(Guid companyId, Guid batchId, Guid actorId, string reason, CancellationToken cancellationToken = default)
    {
        var batch = await context.OpeningBalanceBatches
            .Include(b => b.Lines)
            .SingleOrDefaultAsync(b => b.Id == batchId && b.CompanyId == companyId, cancellationToken)
            .ConfigureAwait(false) ?? throw new ResourceNotFoundException("OpeningBalanceBatch");

        if (batch.ApprovalState != (byte)OpeningBalanceBatchState.Validated)
        {
            var totalDebit = batch.Lines.Sum(l => l.DebitBase);
            var totalCredit = batch.Lines.Sum(l => l.CreditBase);
            if (totalDebit != totalCredit)
                throw new BusinessRuleException("opening_balances.unbalanced", "Cannot approve an unbalanced batch.");
            batch.ApprovalState = (byte)OpeningBalanceBatchState.Validated;
        }

        if (batch.PostedJournalId.HasValue)
        {
            var branchId = await BookingBranchAsync(batch, companyId, cancellationToken).ConfigureAwait(false);
            var scope = JournalEntryScope.Create(companyId, branchId, actorId);
            var journalService = new SqlJournalEntryService(context, timeProvider);
            var journal = await journalService.GetJournalAsync(scope, batch.PostedJournalId.Value, cancellationToken).ConfigureAwait(false);
            if (journal.State == (byte)JournalEntryState.Validated)
            {
                await journalService.ApproveDraftAsync(scope, journal.Id, new JournalDraftActionCommand
                {
                    ExpectedVersion = ConcurrencyToken.FromBytes(Convert.FromBase64String(journal.VersionBase64)),
                    Reason = string.IsNullOrWhiteSpace(reason) ? "اعتماد رصيد افتتاحي" : reason,
                }, cancellationToken).ConfigureAwait(false);
            }
        }

        var now = timeProvider.GetUtcNow();
        batch.ApprovalState = (byte)OpeningBalanceBatchState.Approved;
        batch.ApprovedByActorId = actorId;
        batch.ApprovedAtUtc = now;
        batch.UpdatedAtUtc = now;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await MapBatchAsync(batch, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OpeningBalanceBatchDto> PostAsync(Guid companyId, Guid batchId, Guid actorId, string reason, CancellationToken cancellationToken = default)
    {
        var batch = await context.OpeningBalanceBatches
            .Include(b => b.Lines)
            .SingleOrDefaultAsync(b => b.Id == batchId && b.CompanyId == companyId, cancellationToken)
            .ConfigureAwait(false) ?? throw new ResourceNotFoundException("OpeningBalanceBatch");

        if (batch.ApprovalState == (byte)OpeningBalanceBatchState.Posted && batch.PostedJournalId.HasValue)
            return await MapBatchAsync(batch, cancellationToken).ConfigureAwait(false);

        if (!batch.PostedJournalId.HasValue)
            throw new BusinessRuleException("opening_balances.not_approved", "Opening balance batch must be validated and approved before posting.");

        var branchId = await BookingBranchAsync(batch, companyId, cancellationToken).ConfigureAwait(false);
        var scope = JournalEntryScope.Create(companyId, branchId, actorId);
        var journalService = new SqlJournalEntryService(context, timeProvider);
        var journal = await journalService.GetJournalAsync(scope, batch.PostedJournalId.Value, cancellationToken).ConfigureAwait(false);

        var postingService = new SqlPostingService(context);
        var postingRequest = PostingRequest.Post(
            PostingRequestScope.Create(companyId, branchId, actorId),
            new Guid(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"OpeningBalance:{batch.Id:N}"))[..16]),
            journal.Id,
            ExpectedJournalVersion.FromBase64(journal.VersionBase64),
            AuditReason.Create(string.IsNullOrWhiteSpace(reason) ? "ترحيل رصيد افتتاحي" : reason));

        await postingService.PostAsync(postingRequest, cancellationToken).ConfigureAwait(false);

        batch = await context.OpeningBalanceBatches
            .Include(b => b.Lines)
            .SingleAsync(b => b.Id == batchId && b.CompanyId == companyId, cancellationToken)
            .ConfigureAwait(false);

        var now = timeProvider.GetUtcNow();
        batch.ApprovalState = (byte)OpeningBalanceBatchState.Posted;
        batch.UpdatedAtUtc = now;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await MapBatchAsync(batch, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OpeningBalanceBatchDto> ReverseAsync(Guid companyId, Guid batchId, Guid actorId, string reason, CancellationToken cancellationToken = default)
    {
        var batch = await context.OpeningBalanceBatches
            .Include(b => b.Lines)
            .SingleOrDefaultAsync(b => b.Id == batchId && b.CompanyId == companyId, cancellationToken)
            .ConfigureAwait(false) ?? throw new ResourceNotFoundException("OpeningBalanceBatch");

        if (batch.ApprovalState != (byte)OpeningBalanceBatchState.Posted || !batch.PostedJournalId.HasValue)
            throw new BusinessRuleException("opening_balances.not_posted", "Only a posted opening balance batch can be reversed.");

        var journalService = new SqlJournalEntryService(context, timeProvider);
        var branchId = await BookingBranchAsync(batch, companyId, cancellationToken).ConfigureAwait(false);
        var scope = JournalEntryScope.Create(companyId, branchId, actorId);

        var original = await journalService.GetJournalAsync(scope, batch.PostedJournalId.Value, cancellationToken).ConfigureAwait(false);
        var reversalDraft = await journalService.CreateReversalDraftAsync(scope, new CreateReversalDraftCommand
        {
            OriginalJournalId = original.Id,
            OriginalExpectedVersion = ConcurrencyToken.FromBytes(Convert.FromBase64String(original.VersionBase64)),
            AccountingDate = batch.OpeningDate,
            Reason = string.IsNullOrWhiteSpace(reason) ? "عكس قيد افتتاحي" : reason,
        }, cancellationToken).ConfigureAwait(false);

        var validatedReversal = await journalService.ValidateDraftAsync(scope, reversalDraft.Id, new JournalDraftActionCommand
        {
            ExpectedVersion = ConcurrencyToken.FromBytes(Convert.FromBase64String(reversalDraft.VersionBase64)),
            Reason = string.IsNullOrWhiteSpace(reason) ? "عكس قيد افتتاحي" : reason,
        }, cancellationToken).ConfigureAwait(false);

        reversalDraft = await journalService.ApproveDraftAsync(scope, reversalDraft.Id, new JournalDraftActionCommand
        {
            ExpectedVersion = ConcurrencyToken.FromBytes(Convert.FromBase64String(validatedReversal.VersionBase64)),
            Reason = string.IsNullOrWhiteSpace(reason) ? "اعتماد عكس قيد افتتاحي" : reason,
        }, cancellationToken).ConfigureAwait(false);

        var postingService = new SqlPostingService(context);
        var reversalRequest = PostingRequest.Reverse(
            PostingRequestScope.Create(companyId, branchId, actorId),
            new Guid(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"OpeningBalanceReversal:{batch.Id:N}"))[..16]),
            reversalDraft.Id,
            ExpectedJournalVersion.FromBase64(reversalDraft.VersionBase64),
            original.Id,
            ExpectedJournalVersion.FromBase64(original.VersionBase64),
            AuditReason.Create(string.IsNullOrWhiteSpace(reason) ? "عكس قيد افتتاحي" : reason));

        await postingService.PostAsync(reversalRequest, cancellationToken).ConfigureAwait(false);

        batch = await context.OpeningBalanceBatches
            .Include(b => b.Lines)
            .SingleAsync(b => b.Id == batchId && b.CompanyId == companyId, cancellationToken)
            .ConfigureAwait(false);

        var now = timeProvider.GetUtcNow();
        batch.ReversalJournalId = reversalDraft.Id;
        batch.ApprovalState = (byte)OpeningBalanceBatchState.Reversed;
        batch.UpdatedAtUtc = now;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await MapBatchAsync(batch, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OpeningBalanceBatchDto> GetAsync(Guid companyId, Guid batchId, CancellationToken cancellationToken = default)
    {
        var batch = await context.OpeningBalanceBatches
            .Include(b => b.Lines)
            .AsNoTracking()
            .SingleOrDefaultAsync(b => b.Id == batchId && b.CompanyId == companyId, cancellationToken)
            .ConfigureAwait(false) ?? throw new ResourceNotFoundException("OpeningBalanceBatch");

        return await MapBatchAsync(batch, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OpeningBalanceBatchDto>> ListAsync(Guid companyId, Guid fiscalYearId, CancellationToken cancellationToken = default)
    {
        var batches = await context.OpeningBalanceBatches
            .Include(b => b.Lines)
            .AsNoTracking()
            .Where(b => b.CompanyId == companyId && b.FiscalYearId == fiscalYearId)
            .OrderByDescending(b => b.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var dtos = new List<OpeningBalanceBatchDto>(batches.Count);
        foreach (var b in batches)
            dtos.Add(await MapBatchAsync(b, cancellationToken).ConfigureAwait(false));

        return dtos;
    }

    /// <summary>
    /// The branch a company-wide opening batch books into: the batch's own branch when it has one, otherwise the root branch chosen by
    /// code. An unordered first row would move the same batch's journal between branches from one run to the next.
    /// </summary>
    private async Task<Guid> BookingBranchAsync(OpeningBalanceBatchRow batch, Guid companyId, CancellationToken cancellationToken)
    {
        if (batch.BranchId is { } branch) return branch;
        var root = await context.Branches.AsNoTracking().Where(b => b.CompanyId == companyId)
            .OrderBy(b => b.ParentBranchId != null).ThenBy(b => b.Code)
            .Select(b => (Guid?)b.Id).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return root ?? throw new BusinessRuleException("opening_balances.no_branch",
            "لا فرع للشركة؛ أنشئ فرعاً قبل ترحيل الأرصدة الافتتاحية. / The company has no branch to book opening balances to.");
    }

    private async Task<OpeningBalanceBatchDto> MapBatchAsync(OpeningBalanceBatchRow batch, CancellationToken cancellationToken)
    {
        var accountIds = batch.Lines.Select(l => l.AccountId).Distinct().ToArray();
        var accounts = await context.Accounts.AsNoTracking()
            .Where(a => accountIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, cancellationToken)
            .ConfigureAwait(false);

        var costCenterIds = batch.Lines.Where(l => l.CostCenterId.HasValue).Select(l => l.CostCenterId!.Value).Distinct().ToArray();
        var costCenters = await context.CostCenters.AsNoTracking()
            .Where(c => costCenterIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, cancellationToken)
            .ConfigureAwait(false);

        var lines = batch.Lines.OrderBy(l => l.LineOrder).Select(l => new OpeningBalanceLineDto(
            l.Id,
            l.BatchId,
            l.AccountId,
            accounts.TryGetValue(l.AccountId, out var acc) ? acc.Code : string.Empty,
            accounts.TryGetValue(l.AccountId, out var acc2) ? acc2.NameArabic : string.Empty,
            l.CostCenterId,
            l.CostCenterId.HasValue && costCenters.TryGetValue(l.CostCenterId.Value, out var cc) ? cc.Code : null,
            l.CurrencyCode,
            l.DebitOriginal,
            l.CreditOriginal,
            l.ExchangeRate,
            l.DebitBase,
            l.CreditBase,
            l.Description,
            l.LineOrder)).ToList();

        var totalDebit = batch.Lines.Sum(l => l.DebitBase);
        var totalCredit = batch.Lines.Sum(l => l.CreditBase);

        return new OpeningBalanceBatchDto(
            batch.Id,
            batch.CompanyId,
            batch.FiscalYearId,
            batch.BranchId,
            batch.OpeningDate,
            batch.SourceType,
            batch.SourceReference,
            batch.SourceFingerprint,
            batch.OriginalCurrency,
            batch.BaseCurrency,
            batch.ApprovalState,
            batch.ActorId,
            batch.ApprovedByActorId,
            batch.CreatedAtUtc,
            batch.UpdatedAtUtc,
            batch.ApprovedAtUtc,
            batch.PostedJournalId,
            batch.ReversalJournalId,
            batch.IdempotencyKey,
            totalDebit,
            totalCredit,
            totalDebit == totalCredit,
            Convert.ToBase64String(batch.RowVersion),
            lines);
    }
}
