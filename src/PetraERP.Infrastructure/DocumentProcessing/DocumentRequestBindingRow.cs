using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Security;

namespace PetraERP.Infrastructure.DocumentProcessing;

/// <summary>
/// Immutable, durable binding of a client idempotency key to the SHA-256 of the complete canonical financial input that
/// first used it. Inserted before the document is created, so the unique key arbitrates concurrent identical requests and
/// survives a crash between "key accepted" and "document committed". The fiscal year is implicit: the table lives in the
/// company-year database. The row is never updated; a later retry is compared against <see cref="RequestHashHex"/>, never
/// against a representation rebuilt from the document, its settlement plans or its settlement batches.
/// </summary>
internal sealed class DocumentRequestBindingRow
{
    public Guid CompanyId { get; set; }
    public Guid BranchId { get; set; }
    /// <summary>e.g. SalesInvoice, SalesInvoice.Update, PurchaseInvoice, Voucher.Update.</summary>
    public required string DocumentKind { get; set; }
    public required string IdempotencyKey { get; set; }
    public Guid DocumentId { get; set; }
    public Guid ActorUserId { get; set; }
    public required string RequestHashHex { get; set; }
    public DateTimeOffset RegisteredAtUtc { get; set; }
}

internal sealed class DocumentRequestBindingConfiguration : IEntityTypeConfiguration<DocumentRequestBindingRow>
{
    public void Configure(EntityTypeBuilder<DocumentRequestBindingRow> builder)
    {
        builder.ToTable("DocumentRequestBindings", "audit", table =>
        {
            table.HasCheckConstraint("CK_DocumentRequestBindings_Hash", "LEN([RequestHashHex])=64");
            table.HasCheckConstraint("CK_DocumentRequestBindings_Key", "LEN(LTRIM(RTRIM([IdempotencyKey])))>0");
        });
        builder.HasKey(r => new { r.CompanyId, r.BranchId, r.DocumentKind, r.IdempotencyKey });
        builder.Property(r => r.DocumentKind).HasMaxLength(48).IsRequired();
        builder.Property(r => r.IdempotencyKey).HasMaxLength(128).IsRequired();
        builder.Property(r => r.RequestHashHex).HasColumnType("char(64)").IsRequired();
        builder.Property(r => r.RegisteredAtUtc).HasPrecision(7);
        builder.HasIndex(r => new { r.CompanyId, r.BranchId, r.DocumentId });
        builder.HasOne<CompanyRow>().WithMany().HasForeignKey(r => r.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BranchRow>().WithMany().HasForeignKey(r => r.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserRow>().WithMany().HasForeignKey(r => r.ActorUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
