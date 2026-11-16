using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger;
using PetraERP.Application.GeneralLedger.Posting;
using PetraERP.Application.Security;
using PetraERP.Application.Treasury;
using PetraERP.Domain.Primitives;
using PetraERP.Domain.Treasury;
using PetraERP.Domain.FinancialValues;
using PetraERP.Infrastructure.Currencies;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Parties.OpenItems;

namespace PetraERP.Infrastructure.Treasury;

public sealed partial class SqlTreasuryVoucherService
{
    private async Task VerifyTreasuryAsync(Guid company,Guid branch,Guid actor,string permission,CancellationToken token)
    {
        var service=new SqlPostingService(context);
        await service.VerifyTreasuryActorAsync(company,branch,actor,PermissionCatalog.TreasuryVouchersView,token).ConfigureAwait(false);
        await service.VerifyTreasuryActorAsync(company,branch,actor,permission,token).ConfigureAwait(false);
    }

    public async Task<VoucherDetailResult> ApproveAsync(Guid company,Guid branch,Guid voucherId,Guid actor,VoucherActionCommand command,CancellationToken token)
    {
        var reason=AuditReason.Create(command.Reason);
        var tx=await CurrencyWriteTransaction.BeginAsync(context,company,token).ConfigureAwait(false);
        await using var lifetime=tx.ConfigureAwait(false);
        await VerifyTreasuryAsync(company,branch,actor,PermissionCatalog.TreasuryVouchersApprove,token).ConfigureAwait(false);
        var voucher=await LoadForUpdateAsync(company,branch,voucherId,token).ConfigureAwait(false);
        if(!command.ExpectedVersion.Matches(voucher.RowVersion)) throw new ConcurrencyConflictException("voucher");
        if(voucher.Status!=(byte)VoucherStatus.Draft) throw SettlementError("voucher_not_draft","يمكن اعتماد مسودة فقط","Only a draft can be approved.");
        await ValidateAsync(company,branch,ToCommand(voucher),token).ConfigureAwait(false);
        await RequireOpenPeriodAsync(company,DateOnly.FromDateTime(voucher.VoucherDateUtc.UtcDateTime),token).ConfigureAwait(false);
        PetraERP.Domain.Documents.DocumentLifecycle.EnsureTransition(PetraERP.Domain.Documents.AccountingDocumentKind.TreasuryVoucher, PetraERP.Domain.Documents.DocumentLifecycle.From((VoucherStatus)voucher.Status), PetraERP.Domain.Documents.DocumentLifecycleState.Approved);
        voucher.Status=(byte)VoucherStatus.Approved;voucher.ApprovedBy=actor;voucher.ApprovedAtUtc=timeProvider.GetUtcNow();voucher.ApprovalReason=reason.Value;
        await context.SaveChangesAsync(token).ConfigureAwait(false);await tx.CommitAsync(token).ConfigureAwait(false);
        return await GetAsync(company,branch,voucherId,token).ConfigureAwait(false);
    }

    public async Task<VoucherDetailResult> ApplyAdvanceAsync(Guid company,Guid branch,Guid voucherId,Guid actor,ApplyAdvanceCommand command,CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(command);
        var reason=AuditReason.Create(command.Reason);
        if(string.IsNullOrWhiteSpace(command.IdempotencyKey)||command.IdempotencyKey.Length>128||command.Lines is null||command.Lines.Count==0||command.Lines.Any(l=>l is null)
            ||command.DateUtc>timeProvider.GetUtcNow().AddMinutes(5)) throw SettlementError("request","راجع مفتاح الطلب والتاريخ والأسطر","Supply a request key, valid date and allocations.");
        var fingerprint=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        { voucherId,actor,Version=command.ExpectedVersion.ToBase64(),reason.Value,Date=command.DateUtc.ToUniversalTime(),Lines=command.Lines.OrderBy(l=>l.InvoiceId).ToArray() })));
        var tx=await CurrencyWriteTransaction.BeginAsync(context,company,token).ConfigureAwait(false);
        await using var lifetime=tx.ConfigureAwait(false);
        await VerifyTreasuryAsync(company,branch,actor,PermissionCatalog.TreasuryVouchersAllocate,token).ConfigureAwait(false);
        var replay=await context.SettlementBatches.AsNoTracking().SingleOrDefaultAsync(b=>b.CompanyId==company&&b.BranchId==branch&&b.RequestKey==command.IdempotencyKey,token).ConfigureAwait(false);
        if(replay is not null)
        {
            if(replay.VoucherId!=voucherId||replay.ActorId!=actor||replay.Fingerprint!=fingerprint) throw SettlementError("idempotency_conflict","المفتاح مرتبط بطلب مختلف","The request key belongs to another intent.");
            await tx.CommitAsync(token).ConfigureAwait(false);return await GetAsync(company,branch,voucherId,token).ConfigureAwait(false);
        }
        var voucher=await LoadForUpdateAsync(company,branch,voucherId,token).ConfigureAwait(false);
        if(!command.ExpectedVersion.Matches(voucher.RowVersion)) throw new ConcurrencyConflictException("voucher");
        if(voucher.Status!=(byte)VoucherStatus.Posted||voucher.PartyId is null||voucher.Lines.Count!=0)
            throw SettlementError("advance_invalid","اختر سند طرف مرحلاً دون توزيع مباشر","Choose a posted party voucher without direct distribution.");
        if(command.DateUtc.UtcDateTime.Date<voucher.VoucherDateUtc.UtcDateTime.Date) throw SettlementError("allocation_date","التسوية تسبق الدفعة","Application date precedes the advance.");
        await RequireOpenPeriodAsync(company,DateOnly.FromDateTime(command.DateUtc.UtcDateTime),token).ConfigureAwait(false);
        var existing=await context.SettlementApplications.AsNoTracking().Where(a=>a.VoucherId==voucherId&&a.Batch!.State==1)
            .Select(a=>new { a.SettlementAmount,a.SettlementBaseAmount }).ToListAsync(token).ConfigureAwait(false);
        var legacy=voucher.Allocations.Sum(a=>a.Amount);
        var amount=voucher.Amount-legacy-existing.Sum(a=>a.SettlementAmount);
        var availableBase=PetraERP.Domain.FinancialValues.DocumentCurrencyPolicy.ToBase(voucher.Amount-legacy,voucher.ExchangeRate)-existing.Sum(a=>a.SettlementBaseAmount);
        if(amount<=0||availableBase<=0) throw SettlementError("over_allocation","لا توجد دفعة غير مخصصة متاحة","No unapplied advance remains.");
        // Validate saved header and mappings without counting its initial draft instructions again.
        var validation=await ValidateAsync(company,branch,ToCommand(voucher) with { Allocations=[],InvoiceAllocations=[] },token).ConfigureAwait(false);
        var prepared=await PrepareAsync(company,branch,(VoucherKind)voucher.Kind,voucher.PartyId.Value,voucher.CurrencyCode,voucher.ExchangeRate,amount,availableBase,
            command.DateUtc,command.Lines,token).ConfigureAwait(false);
        var sourceAccount=await AdvanceAccountAsync(voucher,token).ConfigureAwait(false);
        var batch=NewBatch(voucher,actor,command.IdempotencyKey,fingerprint,reason.Value,command.DateUtc,false,prepared);
        var journal=await new SqlJournalEntryService(context,timeProvider).CreateDraftAsync(JournalEntryScope.Create(company,branch,actor),new CreateJournalDraftCommand
        { AccountingDate=DateOnly.FromDateTime(command.DateUtc.UtcDateTime),Description="تسوية دفعة مقدمة / Apply advance "+voucher.VoucherNumber,Lines=BuildAdvanceJournal(voucher,validation,prepared,sourceAccount) },token).ConfigureAwait(false);
        journal=await ApproveJournalAsync(new SqlJournalEntryService(context,timeProvider),JournalEntryScope.Create(company,branch,actor),journal,reason.Value,token).ConfigureAwait(false);
        batch.JournalId=journal.Id;voucher.UpdatedAtUtc=timeProvider.GetUtcNow();
        await context.SaveChangesAsync(token).ConfigureAwait(false);
        await new SqlPostingService(context).PostSettlementAsync(batch.Id,PostingRequest.Post(PostingRequestScope.Create(company,branch,actor),LedgerKey("apply",batch.Id),
            journal.Id,ExpectedJournalVersion.FromBase64(journal.VersionBase64),reason),token).ConfigureAwait(false);
        await tx.CommitAsync(token).ConfigureAwait(false);
        return await GetAsync(company,branch,voucherId,token).ConfigureAwait(false);
    }

    public async Task<VoucherDetailResult> ReverseSettlementAsync(Guid company,Guid branch,Guid voucherId,Guid batchId,Guid actor,VoucherActionCommand command,CancellationToken token)
    {
        var reason=AuditReason.Create(command.Reason);
        var tx=await CurrencyWriteTransaction.BeginAsync(context,company,token).ConfigureAwait(false);await using var lifetime=tx.ConfigureAwait(false);
        await VerifyTreasuryAsync(company,branch,actor,PermissionCatalog.TreasuryVouchersReverse,token).ConfigureAwait(false);
        var batch=await context.SettlementBatches.SingleOrDefaultAsync(b=>b.Id==batchId&&b.VoucherId==voucherId&&b.CompanyId==company&&b.BranchId==branch,token).ConfigureAwait(false)
            ??throw new ResourceNotFoundException("SettlementBatch");
        if(batch.IsInitial)throw SettlementError("initial_allocation","اعكس السند الأصلي لعكس تخصيصه الأول","Reverse the original voucher to reverse initial allocations.");
        if(batch.State==2&&batch.ReversedBy==actor&&batch.ReversalReason==reason.Value)
        {await tx.CommitAsync(token).ConfigureAwait(false);return await GetAsync(company,branch,voucherId,token).ConfigureAwait(false);}
        if(!command.ExpectedVersion.Matches(batch.RowVersion))throw new ConcurrencyConflictException("settlement");
        await ReverseBatchAsync(batch,actor,reason.Value,token).ConfigureAwait(false);
        await tx.CommitAsync(token).ConfigureAwait(false);return await GetAsync(company,branch,voucherId,token).ConfigureAwait(false);
    }

    private async Task ReverseLaterBatchesAsync(VoucherRow voucher,Guid actor,string reason,CancellationToken token)
    {
        var ids=await context.SettlementBatches.AsNoTracking().Where(b=>b.VoucherId==voucher.Id&&!b.IsInitial&&b.State==1)
            .OrderByDescending(b=>b.RecordedAtUtc).Select(b=>b.Id).ToListAsync(token).ConfigureAwait(false);
        foreach(var id in ids)
        {
            var batch=await context.SettlementBatches.SingleAsync(b=>b.Id==id,token).ConfigureAwait(false);
            await ReverseBatchAsync(batch,actor,reason,token).ConfigureAwait(false);
        }
    }

    private async Task ReverseBatchAsync(SettlementBatchRow batch,Guid actor,string reason,CancellationToken token)
    {
        if(batch.State!=1||batch.JournalId is null)throw SettlementError("settlement_state","التسوية غير قابلة للعكس","Settlement cannot be reversed.");
        var now=timeProvider.GetUtcNow();await RequireOpenPeriodAsync(batch.CompanyId,DateOnly.FromDateTime(now.UtcDateTime),token).ConfigureAwait(false);
        var service=new SqlJournalEntryService(context,timeProvider);var scope=JournalEntryScope.Create(batch.CompanyId,batch.BranchId,actor);
        var original=await service.GetJournalAsync(scope,batch.JournalId.Value,token).ConfigureAwait(false);
        var journal=await service.CreateReversalDraftAsync(scope,new CreateReversalDraftCommand
        {OriginalJournalId=original.Id,OriginalExpectedVersion=ConcurrencyToken.FromBytes(Convert.FromBase64String(original.VersionBase64)),AccountingDate=DateOnly.FromDateTime(now.UtcDateTime),Reason=reason},token).ConfigureAwait(false);
        journal=await ApproveJournalAsync(service,scope,journal,reason,token).ConfigureAwait(false);
        batch.State=2;batch.ReversalJournalId=journal.Id;batch.ReversedAtUtc=now;batch.ReversedBy=actor;batch.ReversalReason=reason;
        var apps = await context.SettlementApplications.Where(a => a.BatchId == batch.Id && a.OpenItemId != null).ToListAsync(token).ConfigureAwait(false);
        foreach (var app in apps)
        {
            var item = await context.OpenItems.SingleOrDefaultAsync(o => o.Id == app.OpenItemId!.Value, token).ConfigureAwait(false);
            if (item is not null)
            {
                item.SettledAmount = Math.Max(0m, item.SettledAmount - app.InvoiceAmount);
                item.OutstandingAmount = Math.Min(item.OriginalAmount, item.OriginalAmount - item.SettledAmount);
                item.CurrentBaseCarryingAmount = DocumentCurrencyPolicy.ToBase(item.OutstandingAmount, item.ExchangeRate);
                item.State = item.SettledAmount == 0m ? (byte)OpenItemState.Open : (byte)OpenItemState.PartiallySettled;
                item.UpdatedAtUtc = now;
            }
        }
        var voucher=await context.Vouchers.SingleAsync(v=>v.Id==batch.VoucherId,token).ConfigureAwait(false);voucher.UpdatedAtUtc=now;
        await context.SaveChangesAsync(token).ConfigureAwait(false);
        await new SqlPostingService(context).PostSettlementAsync(batch.Id,PostingRequest.Reverse(PostingRequestScope.Create(batch.CompanyId,batch.BranchId,actor),LedgerKey("unapply",batch.Id),
            journal.Id,ExpectedJournalVersion.FromBase64(journal.VersionBase64),original.Id,ExpectedJournalVersion.FromBase64(original.VersionBase64),AuditReason.Create(reason)),token).ConfigureAwait(false);
    }

    private async Task<Guid> AdvanceAccountAsync(VoucherRow voucher,CancellationToken token)
    {
        if(voucher.PartyControlAccountId is { } id)return id;
        var ids=await context.JournalLines.AsNoTracking().Where(l=>l.JournalId==voucher.JournalEntryId
            &&(l.Description=="دفعة مقدمة من الزبون"||l.Description=="دفعة مقدمة / Party advance")).Select(l=>l.AccountId).Distinct().ToListAsync(token).ConfigureAwait(false);
        return ids.Count==1?ids[0]:throw SettlementError("control_provenance","حساب الدفعة الأصلية يحتاج مراجعة؛ لا يستبدل بالحساب الحالي","Original advance control account needs review; today's mapping cannot replace it.");
    }
}
