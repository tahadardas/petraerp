using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Security;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Inventory;

internal sealed class InventoryDocumentConfiguration : IEntityTypeConfiguration<InventoryDocumentRow>, IEntityTypeConfiguration<InventoryDocumentLineRow>
{
    public void Configure(EntityTypeBuilder<InventoryDocumentRow> b)
    {
        b.ToTable("Documents", "inventory", t =>
        {
            t.HasCheckConstraint("CK_InventoryDocuments_Kind", "[Kind] IN (1,2,3)");
            t.HasCheckConstraint("CK_InventoryDocuments_State", "[State] IN (0,1,2)");
            t.HasCheckConstraint("CK_InventoryDocuments_Posted", "[State]=0 OR ([Number] IS NOT NULL AND [PostKey] IS NOT NULL AND [PostedBy] IS NOT NULL AND [PostedAtUtc] IS NOT NULL)");
            t.HasCheckConstraint("CK_InventoryDocuments_Reversed", "[State]<>2 OR ([ReverseKey] IS NOT NULL AND [ReversedBy] IS NOT NULL AND [ReversedAtUtc] IS NOT NULL)");
            t.HasCheckConstraint("CK_InventoryDocuments_Reason", "LEN(LTRIM(RTRIM([Reason])))>0");
            t.HasCheckConstraint("CK_InventoryDocuments_Utc", "DATEPART(TZOFFSET,[DateUtc])=0");
            t.HasCheckConstraint("CK_InventoryDocuments_Target", "([Kind]=1 AND [TargetWarehouseId] IS NOT NULL AND [TargetWarehouseId]<>[WarehouseId]) OR ([Kind]<>1 AND [TargetWarehouseId] IS NULL)");
            t.HasCheckConstraint("CK_InventoryDocuments_Mappings", "([Kind]=1 AND [InventoryAccountId] IS NULL AND [OffsetAccountId] IS NULL) OR ([Kind]<>1 AND [InventoryAccountId] IS NOT NULL AND [OffsetAccountId] IS NOT NULL AND [InventoryAccountId]<>[OffsetAccountId])");
            t.HasCheckConstraint("CK_InventoryDocuments_Center", "[Kind]<>3 OR [CostCenterId] IS NOT NULL");
        });
        b.HasKey(r => r.Id); b.Property(r => r.Id).ValueGeneratedNever();
        b.Property(r => r.Reason).HasMaxLength(500).IsRequired(); b.Property(r => r.Number).HasMaxLength(64);
        b.Property(r => r.PostKey).HasMaxLength(128); b.Property(r => r.ReverseKey).HasMaxLength(128);
        b.Property(r => r.CreationFingerprint).HasMaxLength(64);
        b.Property(r => r.CreationKey).HasMaxLength(128).UseCollation(StorageConventions.OrdinalCollation);
        b.HasIndex(r => new { r.CompanyId, r.BranchId, r.CreationKey }).IsUnique().HasFilter("[CreationKey] IS NOT NULL");
        b.Property(r => r.PostKey).UseCollation(StorageConventions.OrdinalCollation);
        b.Property(r => r.ReverseKey).UseCollation(StorageConventions.OrdinalCollation);
        b.Property(r => r.Number).UseCollation(StorageConventions.OrdinalCollation);
        b.Property(r => r.PostVersion).HasMaxLength(32); b.Property(r => r.ReverseVersion).HasMaxLength(32);
        b.Property(r => r.PostReason).HasMaxLength(500); b.Property(r => r.ReverseReason).HasMaxLength(500);
        b.Property(r => r.RowVersion).IsRowVersion();
        b.Property(r => r.DateUtc).HasPrecision(7); b.Property(r => r.CreatedAtUtc).HasPrecision(7); b.Property(r => r.UpdatedAtUtc).HasPrecision(7);
        b.Property(r => r.PostedAtUtc).HasPrecision(7); b.Property(r => r.ReversedAtUtc).HasPrecision(7);
        b.HasIndex(r => new { r.CompanyId, r.BranchId, r.PostKey }).IsUnique().HasFilter("[PostKey] IS NOT NULL");
        b.HasIndex(r => new { r.CompanyId, r.BranchId, r.ReverseKey }).IsUnique().HasFilter("[ReverseKey] IS NOT NULL");
        b.HasIndex(r => new { r.CompanyId, r.BranchId, r.Number }).IsUnique().HasFilter("[Number] IS NOT NULL");
        b.HasIndex(r => new { r.CompanyId, r.BranchId, r.DateUtc });
        b.HasOne<CompanyRow>().WithMany().HasForeignKey(r => r.CompanyId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<BranchRow>().WithMany().HasForeignKey(r => r.BranchId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<WarehouseRow>().WithMany().HasForeignKey(r => r.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<WarehouseRow>().WithMany().HasForeignKey(r => r.TargetWarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AccountRow>().WithMany().HasForeignKey(r => r.InventoryAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AccountRow>().WithMany().HasForeignKey(r => r.OffsetAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CostCenterRow>().WithMany().HasForeignKey(r => r.CostCenterId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<JournalEntryRow>().WithMany().HasForeignKey(r => r.JournalId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<JournalEntryRow>().WithMany().HasForeignKey(r => r.ReversalJournalId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserRow>().WithMany().HasForeignKey(r => r.CreatedBy).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserRow>().WithMany().HasForeignKey(r => r.PostedBy).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserRow>().WithMany().HasForeignKey(r => r.ReversedBy).OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<InventoryDocumentLineRow> b)
    {
        b.ToTable("DocumentLines", "inventory", t =>
        {
            t.HasCheckConstraint("CK_InventoryDocumentLines_Quantity", "[Quantity]>=0 AND [BaseQuantity]>=0 AND [Cost]>=0");
            t.HasCheckConstraint("CK_InventoryDocumentLines_OpeningCost", "[OpeningUnitCost] IS NULL OR [OpeningUnitCost]>=0");
        });
        b.HasKey(r => r.Id); b.Property(r => r.Id).ValueGeneratedNever();
        b.Property(r => r.Quantity).HasPrecision(19,4); b.Property(r => r.BaseQuantity).HasPrecision(19,4);
        b.Property(r => r.SnapshotQuantity).HasPrecision(19,4); b.Property(r => r.Cost).HasPrecision(19,4);
        b.Property(r => r.OpeningUnitCost).HasPrecision(19,4);
        b.HasIndex(r => new { r.DocumentId, r.ItemId }).IsUnique();
        b.HasOne(r => r.Document).WithMany(r => r.Lines).HasForeignKey(r => r.DocumentId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ItemRow>().WithMany().HasForeignKey(r => r.ItemId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UnitOfMeasureRow>().WithMany().HasForeignKey(r => r.UnitId).OnDelete(DeleteBehavior.Restrict);
    }
}
