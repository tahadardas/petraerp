using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger;
using PetraERP.Application.GeneralLedger.Posting;
using PetraERP.Application.Patterns;
using PetraERP.Application.Treasury;
using PetraERP.Domain.AccountingCalendars;
using PetraERP.Domain.FinancialValues;
using PetraERP.Domain.Patterns;
using PetraERP.Domain.Primitives;
using PetraERP.Domain.Treasury;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Parties.OpenItems;

namespace PetraERP.Infrastructure.Treasury;

public sealed partial class SqlTreasuryVoucherService
{
    public async Task<VoucherDetailResult> PostAsync(Guid companyId, Guid branchId, Guid voucherId, Guid userId, PostVoucherCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey) || command.IdempotencyKey.Length > 128)
            throw new RequestValidationException("treasury.idempotency_key_invalid", "A request key of at most 128 characters is required.", "idempotencyKey");
        var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            try
            {
                await LockScopeAsync(companyId, branchId, cancellationToken).ConfigureAwait(false);
                var voucher = await LoadForUpdateAsync(companyId, branchId, voucherId, cancellationToken).ConfigureAwait(false);
                await VerifyTreasuryAsync(companyId,branchId,userId,PetraERP.Application.Security.PermissionCatalog.TreasuryVouchersPost,cancellationToken).ConfigureAwait(false);
                if (voucher.Status is not (0 or 1))
                {
                    // Replay: the same key returns the completed result; any other key cannot post twice.
                    if (string.Equals(voucher.PostingKey, command.IdempotencyKey, StringComparison.Ordinal) && voucher.PostedByUserId==userId)
                    {
                        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                        return await GetAsync(companyId, branchId, voucherId, cancellationToken).ConfigureAwait(false);
                    }
                    throw new BusinessRuleException("treasury.voucher_not_draft", "The voucher is already posted.");
                }
                if (!command.ExpectedVersion.Matches(voucher.RowVersion)) throw new ConcurrencyConflictException("voucher");

                // Lock every allocated invoice so concurrent receipts and returns serialize; outstanding is recomputed under the locks.
                foreach (var invoiceId in voucher.Allocations.Select(a => a.SalesInvoiceId).Order())
                    await context.Database.ExecuteSqlInterpolatedAsync(
                        $"SELECT 1 FROM [sales].[Invoices] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {invoiceId}", cancellationToken).ConfigureAwait(false);
                var save = ToCommand(voucher);
                var validation = await ValidateAsync(companyId, branchId, save, cancellationToken).ConfigureAwait(false);
                var date = DateOnly.FromDateTime(voucher.VoucherDateUtc.UtcDateTime.Date);
                EnforceCutoff(validation.Configuration, voucher.VoucherDateUtc);
                var fiscalYear = await RequireOpenPeriodAsync(companyId, date, cancellationToken).ConfigureAwait(false);
                if(validation.Prepared.Count>0)
                    await VerifyTreasuryAsync(companyId,branchId,userId,PetraERP.Application.Security.PermissionCatalog.TreasuryVouchersAllocate,cancellationToken).ConfigureAwait(false);
                if(VoucherPatternRules.ApprovalRequired(validation.Configuration, voucher.Amount) && voucher.Status!=(byte)VoucherStatus.Approved)
                    throw SettlementError("approval_required","النمط يتطلب اعتماد السند قبل ترحيله","This pattern requires voucher approval before posting.");

                var revision = await context.PatternRevisions.AsNoTracking().SingleAsync(r => r.Id == voucher.PatternRevisionId, cancellationToken).ConfigureAwait(false);
                var number = await patternService.GenerateNextNumberAsync(PatternScope.Create(companyId, branchId, userId),
                    new GetNextPatternNumberCommand { PatternId = revision.PatternId, FiscalYearId = fiscalYear, RevisionId = revision.Id }, cancellationToken).ConfigureAwait(false);
                var party = voucher.PartyId is { } partyId
                    ? await context.Parties.AsNoTracking().Where(p => p.Id == partyId).Select(p => p.NameArabic).SingleAsync(cancellationToken).ConfigureAwait(false)
                    : null;
                var journalService = new SqlJournalEntryService(context, timeProvider);
                var journalScope = JournalEntryScope.Create(companyId, branchId, userId);
                var kindText = voucher.Kind == (byte)VoucherKind.Receipt ? "سند قبض" : "سند دفع";
                var journal = await journalService.CreateDraftAsync(journalScope, new CreateJournalDraftCommand
                {
                    AccountingDate = date,
                    Description = party is null ? $"{kindText} {number.FormattedNumber}" : $"{kindText} {number.FormattedNumber} — {party}",
                    Lines = BuildVoucherJournal(voucher, validation)
                }, cancellationToken).ConfigureAwait(false);
                var reason = AuditReason.Create(voucher.ApprovalReason ?? $"ترحيل {kindText}");
                journal = await ApproveJournalAsync(journalService, journalScope, journal, reason.Value, cancellationToken).ConfigureAwait(false);

                voucher = await LoadForUpdateAsync(companyId, branchId, voucherId, cancellationToken).ConfigureAwait(false);
                var now = timeProvider.GetUtcNow();
                voucher.VoucherNumber = number.FormattedNumber;
                voucher.JournalEntryId = journal.Id;
                voucher.PostingKey = command.IdempotencyKey;
                PetraERP.Domain.Documents.DocumentLifecycle.EnsureTransition(PetraERP.Domain.Documents.AccountingDocumentKind.TreasuryVoucher, PetraERP.Domain.Documents.DocumentLifecycle.From((VoucherStatus)voucher.Status), PetraERP.Domain.Documents.DocumentLifecycleState.Posted);
                voucher.Status = (byte)VoucherStatus.Posted;
                voucher.PostedByUserId = userId; voucher.PostedAtUtc = now; voucher.UpdatedAtUtc = now;
                voucher.PartyControlAccountId=validation.ReceivableAccountId;
                if(validation.ReceivableAccountId.HasValue)
                {
                    // Legacy rows on a never-posted draft become the new application payload once, not two consumptions.
                    context.VoucherAllocations.RemoveRange(voucher.Allocations); voucher.Allocations.Clear();
                    var fingerprint=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(command.IdempotencyKey)));
                    var batch=NewBatch(voucher,userId,"initial/"+voucher.Id.ToString("N"),fingerprint,reason.Value,voucher.VoucherDateUtc,true,validation.Prepared);
                    batch.JournalId=journal.Id;
                    foreach (var p in validation.Prepared.Where(p => p.Invoice.IsOpenItem))
                    {
                        var openItem = await context.OpenItems.SingleOrDefaultAsync(o => o.Id == p.Invoice.Id, cancellationToken).ConfigureAwait(false);
                        if (openItem is not null)
                        {
                            openItem.SettledAmount += p.Input.InvoiceAmount;
                            openItem.OutstandingAmount = Math.Max(0m, openItem.OriginalAmount - openItem.SettledAmount);
                            openItem.CurrentBaseCarryingAmount = Math.Max(0m, openItem.OriginalBaseCarryingAmount - DocumentCurrencyPolicy.ToBase(openItem.SettledAmount, openItem.ExchangeRate));
                            openItem.State = openItem.OutstandingAmount == 0m ? (byte)OpenItemState.FullySettled : (byte)OpenItemState.PartiallySettled;
                            openItem.UpdatedAtUtc = now;
                        }
                    }
                }
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                var request = PostingRequest.Post(PostingRequestScope.Create(companyId, branchId, userId), LedgerKey("post", voucherId),
                    journal.Id, ExpectedJournalVersion.FromBase64(journal.VersionBase64), reason);
                await new SqlPostingService(context).PostVoucherAsync(voucherId, request, cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch { context.ChangeTracker.Clear(); throw; }
        }
        return await GetAsync(companyId, branchId, voucherId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<VoucherDetailResult> ReverseAsync(Guid companyId, Guid branchId, Guid voucherId, Guid userId, ReverseVoucherCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var reason = AuditReason.Create(command.Reason);
        var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            try
            {
                await LockScopeAsync(companyId, branchId, cancellationToken).ConfigureAwait(false);
                var voucher = await LoadForUpdateAsync(companyId, branchId, voucherId, cancellationToken).ConfigureAwait(false);
                await VerifyTreasuryAsync(companyId,branchId,userId,PetraERP.Application.Security.PermissionCatalog.TreasuryVouchersReverse,cancellationToken).ConfigureAwait(false);
                if (voucher.Status == (byte)VoucherStatus.Reversed && voucher.ReversalReason == reason.Value && voucher.ReversedByUserId==userId)
                {
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return await GetAsync(companyId, branchId, voucherId, cancellationToken).ConfigureAwait(false);
                }
                if (voucher.Status != (byte)VoucherStatus.Posted || voucher.JournalEntryId is null)
                    throw new BusinessRuleException("treasury.voucher_not_posted", "Only a posted voucher can be reversed.");
                if (!command.ExpectedVersion.Matches(voucher.RowVersion)) throw new ConcurrencyConflictException("voucher");
                await EnforceStoredCutoffAsync(voucher, cancellationToken).ConfigureAwait(false);
                var now = timeProvider.GetUtcNow(); var date = DateOnly.FromDateTime(now.UtcDateTime.Date);
                await RequireOpenPeriodAsync(companyId, date, cancellationToken).ConfigureAwait(false);

                await ReverseLaterBatchesAsync(voucher,userId,reason.Value,cancellationToken).ConfigureAwait(false);
                var journalService = new SqlJournalEntryService(context, timeProvider);
                var journalScope = JournalEntryScope.Create(companyId, branchId, userId);
                var original = await journalService.GetJournalAsync(journalScope, voucher.JournalEntryId.Value, cancellationToken).ConfigureAwait(false);
                var journal = await journalService.CreateReversalDraftAsync(journalScope, new CreateReversalDraftCommand
                {
                    OriginalJournalId = original.Id,
                    OriginalExpectedVersion = ConcurrencyToken.FromBytes(Convert.FromBase64String(original.VersionBase64)),
                    AccountingDate = date,
                    Reason = reason.Value
                }, cancellationToken).ConfigureAwait(false);
                journal = await ApproveJournalAsync(journalService, journalScope, journal, reason.Value, cancellationToken).ConfigureAwait(false);

                voucher = await LoadForUpdateAsync(companyId, branchId, voucherId, cancellationToken).ConfigureAwait(false);
                PetraERP.Domain.Documents.DocumentLifecycle.EnsureTransition(PetraERP.Domain.Documents.AccountingDocumentKind.TreasuryVoucher, PetraERP.Domain.Documents.DocumentLifecycle.From((VoucherStatus)voucher.Status), PetraERP.Domain.Documents.DocumentLifecycleState.Reversed);
                voucher.Status = (byte)VoucherStatus.Reversed;
                var initialBatches=await context.SettlementBatches.Include(b => b.Applications).Where(b=>b.VoucherId==voucherId && b.IsInitial && b.State==1).ToListAsync(cancellationToken).ConfigureAwait(false);
                foreach(var b in initialBatches)
                {
                    b.State=2;b.ReversedAtUtc=now;b.ReversedBy=userId;b.ReversalReason=reason.Value;b.ReversalJournalId=journal.Id;
                    foreach (var app in b.Applications.Where(a => a.OpenItemId != null))
                    {
                        var openItem = await context.OpenItems.SingleOrDefaultAsync(o => o.Id == app.OpenItemId!.Value, cancellationToken).ConfigureAwait(false);
                        if (openItem is not null)
                        {
                            openItem.SettledAmount = Math.Max(0m, openItem.SettledAmount - app.InvoiceAmount);
                            openItem.OutstandingAmount = Math.Min(openItem.OriginalAmount, openItem.OriginalAmount - openItem.SettledAmount);
                            openItem.CurrentBaseCarryingAmount = DocumentCurrencyPolicy.ToBase(openItem.OutstandingAmount, openItem.ExchangeRate);
                            openItem.State = openItem.SettledAmount == 0m ? (byte)OpenItemState.Open : (byte)OpenItemState.PartiallySettled;
                            openItem.UpdatedAtUtc = now;
                        }
                    }
                }
                voucher.ReversalJournalEntryId = journal.Id;
                voucher.ReversalReason = reason.Value;
                voucher.ReversedByUserId = userId; voucher.ReversedAtUtc = now; voucher.UpdatedAtUtc = now;
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                var request = PostingRequest.Reverse(PostingRequestScope.Create(companyId, branchId, userId), LedgerKey("reverse", voucherId),
                    journal.Id, ExpectedJournalVersion.FromBase64(journal.VersionBase64), original.Id,
                    ExpectedJournalVersion.FromBase64(original.VersionBase64), reason);
                await new SqlPostingService(context).PostVoucherAsync(voucherId, request, cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch { context.ChangeTracker.Clear(); throw; }
        }
        return await GetAsync(companyId, branchId, voucherId, cancellationToken).ConfigureAwait(false);
    }

    private static SaveVoucherCommand ToCommand(VoucherRow v) => new((VoucherKind)v.Kind, v.PatternRevisionId, v.PartyId,
        (SettlementMethod)v.SettlementMethod, v.VoucherDateUtc, v.CurrencyCode, v.ExchangeRate, v.Amount, v.Notes,
        v.Allocations.Select(a => new VoucherAllocationCommand(a.SalesInvoiceId, a.Amount)).ToList(),
        v.Lines.OrderBy(l => l.DisplayOrder).Select(l => new VoucherLineCommand(l.PartyId, l.AccountId, l.Amount, l.CostCenterId, l.Description)).ToList())
        { InvoiceAllocations=v.AllocationPlans.Select(a=>new InvoiceSettlementCommand(a.SalesInvoiceId??a.PurchaseInvoiceId??a.OpenItemId!.Value,a.InvoiceAmount,a.SettlementAmount,a.CrossRate)).ToArray() };

    private static Guid LedgerKey(string operation, Guid voucherId) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes($"PetraERP.Treasury.{operation}:{voucherId:N}"))[..16]);

    private async Task LockScopeAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        await PetraERP.Infrastructure.Inventory.InventoryWriteScope.LockAsync(context, companyId, branchId, cancellationToken).ConfigureAwait(false);
        var company = await context.Companies.FromSqlInterpolated($"SELECT * FROM [system].[Companies] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={companyId}")
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var branch = await context.Branches.FromSqlInterpolated($"SELECT * FROM [system].[Branches] WITH (UPDLOCK,HOLDLOCK) WHERE [CompanyId]={companyId} AND [Id]={branchId}")
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (company is null || branch is null) throw new ResourceNotFoundException("Company/Branch");
        if (!company.IsActive || !branch.IsActive) throw new BusinessRuleException("treasury.scope_inactive", "The company and branch must be active.");
    }

    private async Task<VoucherRow> LoadForUpdateAsync(Guid companyId, Guid branchId, Guid voucherId, CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        return await context.Vouchers.FromSqlInterpolated($"SELECT * FROM [treasury].[Vouchers] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={voucherId} AND [CompanyId]={companyId} AND [BranchId]={branchId}")
            .Include(v => v.Allocations).Include(v => v.Lines).Include(v => v.AllocationPlans).AsSplitQuery().SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("Voucher");
    }

    private async Task<Guid> RequireOpenPeriodAsync(Guid companyId, DateOnly date, CancellationToken cancellationToken)
    {
        var year = await context.FiscalYears.AsNoTracking().SingleOrDefaultAsync(y => y.CompanyId == companyId && y.RangeStart <= date && y.RangeEnd >= date, cancellationToken).ConfigureAwait(false)
            ?? throw new BusinessRuleException("treasury.no_fiscal_year", "No fiscal year covers the voucher date.");
        var period = await context.AccountingPeriods.AsNoTracking().SingleOrDefaultAsync(p => p.FiscalYearId == year.Id && p.RangeStart <= date && p.RangeEnd >= date, cancellationToken).ConfigureAwait(false);
        if (period is null || period.State != (byte)AccountingPeriodState.Open) throw new BusinessRuleException("treasury.period_closed", "The accounting period is not open.");
        return year.Id;
    }

    private static async Task<JournalDetailsDto> ApproveJournalAsync(SqlJournalEntryService service, JournalEntryScope scope,
        JournalDetailsDto journal, string reason, CancellationToken cancellationToken)
    {
        var validated = await service.ValidateDraftAsync(scope, journal.Id, new JournalDraftActionCommand
        { ExpectedVersion = ConcurrencyToken.FromBytes(Convert.FromBase64String(journal.VersionBase64)), Reason = reason }, cancellationToken).ConfigureAwait(false);
        return await service.ApproveDraftAsync(scope, journal.Id, new JournalDraftActionCommand
        { ExpectedVersion = ConcurrencyToken.FromBytes(Convert.FromBase64String(validated.VersionBase64)), Reason = reason }, cancellationToken).ConfigureAwait(false);
    }
}
