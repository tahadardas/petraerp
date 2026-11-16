using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.CompanyOptions;
using PetraERP.Domain.Patterns;
using PetraERP.Domain.Primitives;
using PetraERP.Infrastructure.Inventory;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Persistence.Actors;

namespace PetraERP.Infrastructure.CompanyOptions;

public sealed class SqlCompanyOptionsService(FinancialDbContext context):ICompanyOptionsService
{
    internal static async Task<CompanyFinancialPolicy> EffectiveAsync(FinancialDbContext db,Guid company,CancellationToken token)
    {
        var row=await db.Set<CompanyPolicyRow>().AsNoTracking().SingleOrDefaultAsync(p=>p.CompanyId==company,token).ConfigureAwait(false);
        return row is null?new():Policy(row);
    }
    public async Task<CompanyPolicySnapshot> GetAsync(Guid companyId,CancellationToken cancellationToken)
    {
        var row=await context.Set<CompanyPolicyRow>().AsNoTracking().SingleOrDefaultAsync(p=>p.CompanyId==companyId,cancellationToken).ConfigureAwait(false);
        var policy=row is null?new():Policy(row);
        return new(policy,row is null?string.Empty:Convert.ToBase64String(row.RowVersion))
        {
            // Only the authorized-exception policy lets a permission holder post an issue that exceeds availability;
            // approval requests and their review never enable negative posting by themselves.
            NegativePostingAvailable=policy.NegativeIssuePolicy==NegativeIssuePolicy.AuthorizedException,
        };
    }
    public async Task<CompanyPolicySnapshot> SaveAsync(Guid companyId,Guid fiscalYearId,Guid actorId,CompanyFinancialPolicy policy,string expectedVersion,string reason,CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var auditReason=AuditReason.Create(reason).Value;
        if(!Enum.IsDefined(policy.NegativeIssuePolicy) || !Enum.IsDefined(policy.DocumentPostingMode) || policy.DefaultNumberingScope is <1 or >4 || policy.InitialNumberSequence is <1 or >999999999999L)
            throw new BusinessRuleException("company.policy_invalid","Choose supported policy values.");
        if (context is AccountingDbContext accContext && accContext.BoundOperation is null)
        {
            accContext.BindWriteScope(new AccountingWriteScope(companyId, fiscalYearId, actorId, AccountingOperation.ManageCompanyOptions));
        }
        var tx=await OwnedDatabaseTransaction.BeginAsync(context,IsolationLevel.Serializable,cancellationToken).ConfigureAwait(false);
        await using var lifetime=tx.ConfigureAwait(false);
        await InventoryWriteScope.LockCompanyAsync(context,companyId,cancellationToken).ConfigureAwait(false);
        if(!await context.FiscalYears.AnyAsync(y=>y.CompanyId==companyId && y.Id==fiscalYearId,cancellationToken).ConfigureAwait(false)) throw new ResourceNotFoundException("fiscal year");
        var defaults=new (Guid? Id,BusinessOperationKind Kind)[] {(policy.DefaultSalesPatternId,BusinessOperationKind.SalesInvoice),(policy.DefaultPurchasePatternId,BusinessOperationKind.PurchaseInvoice),(policy.DefaultReceiptPatternId,BusinessOperationKind.CashReceipt),(policy.DefaultPaymentPatternId,BusinessOperationKind.CashPayment)};
        foreach(var (id,kind) in defaults)
            if(id is {} value && !await context.DocumentPatterns.AnyAsync(p=>p.Id==value && p.CompanyId==companyId && p.OperationKind==(int)kind && p.IsActive && p.ActiveRevisionId!=null,cancellationToken).ConfigureAwait(false))
                throw new BusinessRuleException("company.default_pattern_invalid","Every default must identify an active published pattern of the matching operation in this company.");
        // A party-accounts parent is a heading of this company's chart: active, and not itself a posting account.
        foreach(var parentId in new[] {policy.CustomersParentAccountId,policy.SuppliersParentAccountId})
            if(parentId is {} parent && !await context.Accounts.AnyAsync(a=>a.Id==parent && a.CompanyId==companyId && a.IsActive && !a.PostingAllowed,cancellationToken).ConfigureAwait(false))
                throw new BusinessRuleException("company.party_parent_account_invalid","حساب الأب للزبائن أو الموردين يجب أن يكون حسابًا رئيسيًا فعالًا لا يقبل الترحيل في هذه الشركة / A party parent account must be an active non-posting account of this company.");
        var row=await context.Set<CompanyPolicyRow>().SingleOrDefaultAsync(p=>p.CompanyId==companyId,cancellationToken).ConfigureAwait(false);
        var before=row is null?null:Policy(row);
        if((row is null?string.Empty:Convert.ToBase64String(row.RowVersion))!=expectedVersion) throw new ConcurrencyConflictException("Company policies changed; reload before saving.");
        if(row is null)
        {
            row=new CompanyPolicyRow {CompanyId=companyId,FiscalYearId=fiscalYearId,CreatedByUserId=actorId};context.Add(row);
        }
        Apply(row,policy);
        row.FiscalYearId=fiscalYearId;
        Audit(companyId,fiscalYearId,companyId,actorId,"CompanyPolicyChanged",auditReason,before,policy);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new(policy,Convert.ToBase64String(row.RowVersion))
        {
            NegativePostingAvailable=Policy(row).NegativeIssuePolicy==NegativeIssuePolicy.AuthorizedException,
        };
    }
    public async Task<IReadOnlyList<NegativeIssueRequest>> ListRequestsAsync(Guid companyId,Guid branchId,CancellationToken cancellationToken)
    {
        var rows=await context.Set<NegativeIssueApprovalRow>().AsNoTracking().Where(r=>r.CompanyId==companyId && r.BranchId==branchId)
            .OrderByDescending(r=>r.CreatedAtUtc).Take(200).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(Request).ToArray();
    }
    public async Task<NegativeIssueRequest> RequestAsync(Guid companyId,Guid fiscalYearId,Guid branchId,Guid actorId,Guid itemId,Guid warehouseId,decimal quantity,string reason,Guid requestId,CancellationToken cancellationToken)
    {
        var auditReason=AuditReason.Create(reason).Value;
        if(requestId==Guid.Empty || quantity<=0 || quantity>999999999999999.9999m || decimal.Round(quantity,4)!=quantity) throw new BusinessRuleException("inventory.approval_quantity_invalid","Use a positive quantity with at most four decimal places.");
        if (context is AccountingDbContext accContext && accContext.BoundOperation is null)
        {
            accContext.BindWriteScope(new AccountingWriteScope(companyId, fiscalYearId, actorId, AccountingOperation.RequestNegativeIssue, branchId));
        }
        var tx=await OwnedDatabaseTransaction.BeginAsync(context,IsolationLevel.Serializable,cancellationToken).ConfigureAwait(false);
        await using var lifetime=tx.ConfigureAwait(false);
        await InventoryWriteScope.LockAsync(context,companyId,branchId,cancellationToken).ConfigureAwait(false);
        if((await EffectiveAsync(context,companyId,cancellationToken).ConfigureAwait(false)).NegativeIssuePolicy==NegativeIssuePolicy.Block)
            throw new BusinessRuleException("inventory.negative_request_disabled","Company policy blocks negative-stock requests.");
        await InventoryWriteScope.ValidateWarehouseAsync(context,companyId,branchId,warehouseId,cancellationToken).ConfigureAwait(false);
        if(!await context.Items.AnyAsync(i=>i.Id==itemId && i.CompanyId==companyId && i.IsActive,cancellationToken).ConfigureAwait(false)) throw new ResourceNotFoundException("item");
        var previous=await context.Set<NegativeIssueApprovalRow>().AsNoTracking().SingleOrDefaultAsync(r=>r.Id==requestId,cancellationToken).ConfigureAwait(false);
        if(previous is not null)
        {
            if(previous.CompanyId!=companyId || previous.BranchId!=branchId || previous.CreatedByUserId!=actorId || previous.ItemId!=itemId || previous.WarehouseId!=warehouseId || previous.Quantity!=quantity || previous.Reason!=auditReason)
                throw new BusinessRuleException("inventory.approval_key_conflict","The request identifier was already used with different content.");
            return Request(previous);
        }
        var row=new NegativeIssueApprovalRow {Id=requestId,CompanyId=companyId,FiscalYearId=fiscalYearId,BranchId=branchId,ItemId=itemId,WarehouseId=warehouseId,Quantity=quantity,Reason=auditReason,CreatedByUserId=actorId};
        context.Add(row);Audit(companyId,fiscalYearId,row.Id,actorId,"NegativeIssueRequested",auditReason,null,new {row.ItemId,row.WarehouseId,row.Quantity,row.Status,NegativePostingAvailable=false});
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);await tx.CommitAsync(cancellationToken).ConfigureAwait(false);return Request(row);
    }
    public async Task<NegativeIssueRequest> ReviewAsync(Guid companyId,Guid fiscalYearId,Guid branchId,Guid actorId,Guid requestId,bool approve,string expectedVersion,string reason,CancellationToken cancellationToken)
    {
        var auditReason=AuditReason.Create(reason).Value;
        if (context is AccountingDbContext accContext && accContext.BoundOperation is null)
        {
            accContext.BindWriteScope(new AccountingWriteScope(companyId, fiscalYearId, actorId, AccountingOperation.ReviewNegativeIssue, branchId));
        }
        var tx=await OwnedDatabaseTransaction.BeginAsync(context,IsolationLevel.Serializable,cancellationToken).ConfigureAwait(false);
        await using var lifetime=tx.ConfigureAwait(false);
        await InventoryWriteScope.LockAsync(context,companyId,branchId,cancellationToken).ConfigureAwait(false);
        var row=await context.Set<NegativeIssueApprovalRow>().SingleOrDefaultAsync(r=>r.Id==requestId && r.CompanyId==companyId && r.BranchId==branchId && r.FiscalYearId==fiscalYearId,cancellationToken).ConfigureAwait(false) ?? throw new ResourceNotFoundException("approval request");
        if(Convert.ToBase64String(row.RowVersion)!=expectedVersion) throw new ConcurrencyConflictException("The approval request changed.");
        if(row.Status!="Pending" || row.CreatedByUserId==actorId) throw new BusinessRuleException("inventory.approval_review_denied","Only a different authorized reviewer may decide a pending request.");
        var before=row.Status;row.Status=approve?"ApprovedPolicyOnly":"Rejected";row.ReviewedByUserId=actorId;
        Audit(companyId,fiscalYearId,row.Id,actorId,"NegativeIssueReviewed",auditReason,new {Status=before},new {row.Status,NegativePostingAvailable=false});
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);await tx.CommitAsync(cancellationToken).ConfigureAwait(false);return Request(row);
    }
    private void Audit(Guid company,Guid year,Guid entity,Guid actor,string type,string reason,object? before,object after) => context.Add(new CompanyPolicyAuditRow
    {Id=Guid.CreateVersion7(),CompanyId=company,FiscalYearId=year,EntityId=entity,ActorUserId=actor,EventType=type,Reason=reason,BeforeJson=before is null?null:JsonSerializer.Serialize(before),AfterJson=JsonSerializer.Serialize(after),OccurredAtUtc=DateTimeOffset.UtcNow});
    private static NegativeIssueRequest Request(NegativeIssueApprovalRow row)=>new(row.Id,row.BranchId,row.ItemId,row.WarehouseId,row.Quantity.ToString("0.####",System.Globalization.CultureInfo.InvariantCulture),row.Reason,row.Status,row.CreatedByUserId,row.ReviewedByUserId,Convert.ToBase64String(row.RowVersion));
    internal static CompanyFinancialPolicy Policy(CompanyPolicyRow r)=>new()
    {DocumentPostingMode=(DocumentPostingMode)r.DocumentPostingMode,AllowSaveAsDraft=r.AllowSaveAsDraft,NegativeIssuePolicy=(NegativeIssuePolicy)r.NegativeIssuePolicy,RequireDocumentApproval=r.RequireDocumentApproval,RequireProfitLossCostCenter=r.RequireProfitLossCostCenter,AllowCreditSales=r.AllowCreditSales,AllowCreditPurchases=r.AllowCreditPurchases,AllowManualNumbering=r.AllowManualNumbering,AllowPatternNumberingOverrides=r.AllowPatternNumberingOverrides,DefaultNumberingScope=r.DefaultNumberingScope,InitialNumberSequence=r.InitialNumberSequence,DefaultSalesPatternId=r.DefaultSalesPatternId,DefaultPurchasePatternId=r.DefaultPurchasePatternId,DefaultReceiptPatternId=r.DefaultReceiptPatternId,DefaultPaymentPatternId=r.DefaultPaymentPatternId,CustomersParentAccountId=r.CustomersParentAccountId,SuppliersParentAccountId=r.SuppliersParentAccountId};
    internal static void Apply(CompanyPolicyRow r,CompanyFinancialPolicy p)
    {r.DocumentPostingMode=(byte)p.DocumentPostingMode;r.AllowSaveAsDraft=p.AllowSaveAsDraft;r.NegativeIssuePolicy=(byte)p.NegativeIssuePolicy;r.RequireDocumentApproval=p.RequireDocumentApproval;r.RequireProfitLossCostCenter=p.RequireProfitLossCostCenter;r.AllowCreditSales=p.AllowCreditSales;r.AllowCreditPurchases=p.AllowCreditPurchases;r.AllowManualNumbering=p.AllowManualNumbering;r.AllowPatternNumberingOverrides=p.AllowPatternNumberingOverrides;r.DefaultNumberingScope=p.DefaultNumberingScope;r.InitialNumberSequence=p.InitialNumberSequence;r.DefaultSalesPatternId=p.DefaultSalesPatternId;r.DefaultPurchasePatternId=p.DefaultPurchasePatternId;r.DefaultReceiptPatternId=p.DefaultReceiptPatternId;r.DefaultPaymentPatternId=p.DefaultPaymentPatternId;r.CustomersParentAccountId=p.CustomersParentAccountId;r.SuppliersParentAccountId=p.SuppliersParentAccountId;}
    internal static async Task<Guid> ResolveDefaultRevisionAsync(FinancialDbContext db,Guid company,Guid requested,BusinessOperationKind operation,CancellationToken token)
    {
        if(requested!=Guid.Empty) return requested;
        var p=await EffectiveAsync(db,company,token).ConfigureAwait(false);
        var id=operation switch {BusinessOperationKind.SalesInvoice=>p.DefaultSalesPatternId,BusinessOperationKind.PurchaseInvoice=>p.DefaultPurchasePatternId,BusinessOperationKind.CashReceipt=>p.DefaultReceiptPatternId,BusinessOperationKind.CashPayment=>p.DefaultPaymentPatternId,_=>null};
        if(id is null) throw new BusinessRuleException("company.default_pattern_missing","Select a document pattern; no company default is configured.");
        return await db.DocumentPatterns.Where(r=>r.Id==id && r.CompanyId==company && r.IsActive).Select(r=>r.ActiveRevisionId).SingleOrDefaultAsync(token).ConfigureAwait(false)
            ?? throw new BusinessRuleException("company.default_pattern_invalid","The default pattern has no active revision.");
    }
}
