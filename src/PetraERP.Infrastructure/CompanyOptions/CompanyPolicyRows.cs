using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.AccountingCalendars;
using PetraERP.Infrastructure.Inventory;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Security;

namespace PetraERP.Infrastructure.CompanyOptions;

internal sealed class CompanyPolicyRow:ITrackedStorageRow
{
    public Guid CompanyId { get; set; }
    public Guid FiscalYearId { get; set; }
    public byte DocumentPostingMode { get; set; }
    public bool AllowSaveAsDraft { get; set; }=true;
    public byte NegativeIssuePolicy { get; set; }
    public bool RequireDocumentApproval { get; set; }
    public bool RequireProfitLossCostCenter { get; set; }
    public bool AllowCreditSales { get; set; }=true;
    public bool AllowCreditPurchases { get; set; }=true;
    public bool AllowManualNumbering { get; set; }
    public bool AllowPatternNumberingOverrides { get; set; }=true;
    public int DefaultNumberingScope { get; set; }=4;
    public long InitialNumberSequence { get; set; }=1;
    public Guid? DefaultSalesPatternId { get; set; }
    public Guid? DefaultPurchasePatternId { get; set; }
    public Guid? DefaultReceiptPatternId { get; set; }
    public Guid? DefaultPaymentPatternId { get; set; }
    public Guid? CustomersParentAccountId { get; set; }
    public Guid? SuppliersParentAccountId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; }=[];
}
internal sealed class CompanyPolicyAuditRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid FiscalYearId { get; set; }
    public Guid EntityId { get; set; }
    public string EventType { get; set; }=string.Empty;
    public Guid ActorUserId { get; set; }
    public string Reason { get; set; }=string.Empty;
    public string? BeforeJson { get; set; }
    public string AfterJson { get; set; }=string.Empty;
    public DateTimeOffset OccurredAtUtc { get; set; }
}
internal sealed class NegativeIssueApprovalRow:ITrackedStorageRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid FiscalYearId { get; set; }
    public Guid BranchId { get; set; }
    public Guid ItemId { get; set; }
    public Guid WarehouseId { get; set; }
    public decimal Quantity { get; set; }
    public string Reason { get; set; }=string.Empty;
    public string Status { get; set; }="Pending";
    public Guid CreatedByUserId { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; }=[];
}
internal sealed class CompanyPolicyConfiguration:IEntityTypeConfiguration<CompanyPolicyRow>
{
    public void Configure(EntityTypeBuilder<CompanyPolicyRow> b)
    {
        b.ToTable("CompanyPolicies","policy",t=>
        {
            t.HasCheckConstraint("CK_CompanyPolicies_NegativeMode","[NegativeIssuePolicy] IN (0,1,2)");
            t.HasCheckConstraint("CK_CompanyPolicies_PostingMode","[DocumentPostingMode] IN (0,1)");
            t.HasCheckConstraint("CK_CompanyPolicies_Numbering","[DefaultNumberingScope] IN (1,2,3,4) AND [InitialNumberSequence] BETWEEN 1 AND 999999999999");
            StorageConventions.UtcCheck(t,"CK_CompanyPolicies_Utc");
        });
        b.HasKey(r=>r.CompanyId);b.Property(r=>r.CompanyId).ValueGeneratedNever();StorageConventions.Tracking(b);
        b.HasOne<CompanyRow>().WithMany().HasForeignKey(r=>r.CompanyId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<FiscalYearRow>().WithMany().HasForeignKey(r=>r.FiscalYearId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserRow>().WithMany().HasForeignKey(r=>r.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PetraERP.Infrastructure.Patterns.DocumentPatternRow>().WithMany().HasForeignKey(r=>r.DefaultSalesPatternId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PetraERP.Infrastructure.Patterns.DocumentPatternRow>().WithMany().HasForeignKey(r=>r.DefaultPurchasePatternId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PetraERP.Infrastructure.Patterns.DocumentPatternRow>().WithMany().HasForeignKey(r=>r.DefaultReceiptPatternId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PetraERP.Infrastructure.Patterns.DocumentPatternRow>().WithMany().HasForeignKey(r=>r.DefaultPaymentPatternId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PetraERP.Infrastructure.GeneralLedger.AccountRow>().WithMany().HasForeignKey(r=>r.CustomersParentAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PetraERP.Infrastructure.GeneralLedger.AccountRow>().WithMany().HasForeignKey(r=>r.SuppliersParentAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}
internal sealed class CompanyPolicyAuditConfiguration:IEntityTypeConfiguration<CompanyPolicyAuditRow>
{
    public void Configure(EntityTypeBuilder<CompanyPolicyAuditRow> b)
    {
        b.ToTable("CompanyPolicyAudit","audit",t=>
        {
            t.HasCheckConstraint("CK_CompanyPolicyAudit_Id",StorageConventions.NonemptyId);
            t.HasCheckConstraint("CK_CompanyPolicyAudit_Json","ISJSON([AfterJson])=1 AND ([BeforeJson] IS NULL OR ISJSON([BeforeJson])=1)");
            t.HasCheckConstraint("CK_CompanyPolicyAudit_Utc","DATEPART(TZOFFSET,[OccurredAtUtc])=0");
        });
        b.HasKey(r=>r.Id);b.Property(r=>r.Id).ValueGeneratedNever();
        b.Property(r=>r.EventType).HasMaxLength(64).IsRequired();b.Property(r=>r.Reason).HasMaxLength(500).IsRequired();
        b.Property(r=>r.AfterJson).HasMaxLength(8000).IsRequired();b.Property(r=>r.BeforeJson).HasMaxLength(8000);
        b.Property(r=>r.OccurredAtUtc).HasPrecision(7);
        b.HasIndex(r=>new {r.CompanyId,r.OccurredAtUtc});
        b.HasOne<CompanyRow>().WithMany().HasForeignKey(r=>r.CompanyId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<FiscalYearRow>().WithMany().HasForeignKey(r=>r.FiscalYearId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserRow>().WithMany().HasForeignKey(r=>r.ActorUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
internal sealed class NegativeIssueApprovalConfiguration:IEntityTypeConfiguration<NegativeIssueApprovalRow>
{
    public void Configure(EntityTypeBuilder<NegativeIssueApprovalRow> b)
    {
        b.ToTable("NegativeIssueApprovals","policy",t=>
        {
            t.HasCheckConstraint("CK_NegativeIssueApprovals_Id",StorageConventions.NonemptyId);
            t.HasCheckConstraint("CK_NegativeIssueApprovals_Quantity","[Quantity]>0");
            t.HasCheckConstraint("CK_NegativeIssueApprovals_Status","[Status] IN (N'Pending',N'ApprovedPolicyOnly',N'Rejected')");
            StorageConventions.UtcCheck(t,"CK_NegativeIssueApprovals_Utc");
        });
        b.HasKey(r=>r.Id);b.Property(r=>r.Id).ValueGeneratedNever();b.Property(r=>r.Quantity).HasPrecision(19,4);
        b.Property(r=>r.Reason).HasMaxLength(500).IsRequired();b.Property(r=>r.Status).HasMaxLength(32).IsRequired();StorageConventions.Tracking(b);
        b.HasIndex(r=>new {r.CompanyId,r.BranchId,r.Status});
        b.HasOne<CompanyRow>().WithMany().HasForeignKey(r=>r.CompanyId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<BranchRow>().WithMany().HasForeignKey(r=>r.BranchId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<FiscalYearRow>().WithMany().HasForeignKey(r=>r.FiscalYearId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ItemRow>().WithMany().HasForeignKey(r=>r.ItemId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<WarehouseRow>().WithMany().HasForeignKey(r=>r.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserRow>().WithMany().HasForeignKey(r=>r.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserRow>().WithMany().HasForeignKey(r=>r.ReviewedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
