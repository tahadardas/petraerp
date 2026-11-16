using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.Organizations;

namespace PetraERP.Infrastructure.Inventory;

internal sealed class InventoryDocumentSequence
{
    public Guid CompanyId { get; set; }
    public Guid BranchId { get; set; }
    public int Year { get; set; }
    public int Kind { get; set; }
    public long NextValue { get; set; }
}
internal sealed class InventoryDocumentSequenceConfiguration : IEntityTypeConfiguration<InventoryDocumentSequence>
{
    public void Configure(EntityTypeBuilder<InventoryDocumentSequence> b)
    {
        b.ToTable("DocumentSequences", "inventory", t => t.HasCheckConstraint("CK_InventorySequence_Value", "[NextValue]>=0"));
        b.HasKey(r => new { r.CompanyId, r.BranchId, r.Year, r.Kind });
        b.HasOne<CompanyRow>().WithMany().HasForeignKey(r => r.CompanyId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<BranchRow>().WithMany().HasForeignKey(r => r.BranchId).OnDelete(DeleteBehavior.Restrict);
    }
}
