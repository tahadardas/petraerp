using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Security;

namespace PetraERP.Infrastructure.DocumentNumbers;

/// <summary>A request to change the printed number of one document. Applied only by a different approver.</summary>
internal sealed class DocumentNumberChangeRow : ITrackedStorageRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid BranchId { get; set; }
    public byte Family { get; set; }
    public Guid DocumentId { get; set; }
    public string? OldNumber { get; set; }
    public required string NewNumber { get; set; }
    public required string Reason { get; set; }
    public byte State { get; set; }
    public Guid RequestedByUserId { get; set; }
    public DateTimeOffset RequestedAtUtc { get; set; }
    public Guid? DecidedByUserId { get; set; }
    public DateTimeOffset? DecidedAtUtc { get; set; }
    public string? DecisionReason { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>
/// Append-only, hash-chained record of applied number changes. Each entry's hash covers its content and the previous hash,
/// so removing or editing any entry breaks the chain that <c>VerifyAuditChainAsync</c> recomputes. This is tamper-evidence,
/// not a keyed signature.
/// </summary>
internal sealed class DocumentNumberAuditRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid BranchId { get; set; }
    public long Sequence { get; set; }
    public Guid RequestId { get; set; }
    public byte Family { get; set; }
    public Guid DocumentId { get; set; }
    public string? OldNumber { get; set; }
    public required string NewNumber { get; set; }
    public Guid RequestedByUserId { get; set; }
    public Guid ApprovedByUserId { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public required string PreviousHash { get; set; }
    public required string Hash { get; set; }
}

internal sealed class DocumentNumberChangeConfiguration : IEntityTypeConfiguration<DocumentNumberChangeRow>
{
    public void Configure(EntityTypeBuilder<DocumentNumberChangeRow> builder)
    {
        builder.ToTable("DocumentNumberChanges", "system", table =>
        {
            table.HasCheckConstraint("CK_DocumentNumberChanges_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_DocumentNumberChanges_Family", "[Family] BETWEEN 1 AND 3");
            table.HasCheckConstraint("CK_DocumentNumberChanges_State", "[State] BETWEEN 1 AND 4");
            table.HasCheckConstraint("CK_DocumentNumberChanges_NewNumber", StorageConventions.NormalizedRequired("[NewNumber]"));
            table.HasCheckConstraint("CK_DocumentNumberChanges_Reason", StorageConventions.NormalizedRequired("[Reason]"));
            table.HasCheckConstraint("CK_DocumentNumberChanges_Different", "[OldNumber] IS NULL OR [OldNumber] <> [NewNumber]");
            // Two-person rule at the database: whoever applies a change is never the person who asked for it.
            table.HasCheckConstraint("CK_DocumentNumberChanges_TwoPerson", "[State] <> 2 OR ([DecidedByUserId] IS NOT NULL AND [DecidedByUserId] <> [RequestedByUserId])");
            table.HasCheckConstraint("CK_DocumentNumberChanges_Decision", "([State] = 1 AND [DecidedByUserId] IS NULL AND [DecidedAtUtc] IS NULL) OR ([State] <> 1 AND [DecidedByUserId] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL)");
            StorageConventions.UtcCheck(table, "CK_DocumentNumberChanges_Utc");
        });
        builder.HasKey(static r => r.Id);
        builder.Property(static r => r.Id).ValueGeneratedNever();
        builder.Property(static r => r.Family).HasColumnType("tinyint");
        builder.Property(static r => r.State).HasColumnType("tinyint");
        builder.Property(static r => r.OldNumber).HasMaxLength(64).UseCollation(StorageConventions.OrdinalCollation);
        builder.Property(static r => r.NewNumber).HasMaxLength(64).UseCollation(StorageConventions.OrdinalCollation);
        builder.Property(static r => r.Reason).HasMaxLength(500);
        builder.Property(static r => r.DecisionReason).HasMaxLength(500);
        builder.Property(static r => r.RequestedAtUtc).HasPrecision(7);
        builder.Property(static r => r.DecidedAtUtc).HasPrecision(7);
        StorageConventions.Tracking(builder);
        // One open request per document, and one open claim per proposed number within a family and branch.
        builder.HasIndex(static r => new { r.CompanyId, r.Family, r.DocumentId }).IsUnique().HasFilter("[State] = 1")
            .HasDatabaseName("UX_DocumentNumberChanges_OpenPerDocument");
        builder.HasIndex(static r => new { r.CompanyId, r.BranchId, r.Family, r.NewNumber }).IsUnique().HasFilter("[State] = 1")
            .HasDatabaseName("UX_DocumentNumberChanges_OpenNumber");
        builder.HasIndex(static r => new { r.CompanyId, r.BranchId, r.State, r.RequestedAtUtc }).HasDatabaseName("IX_DocumentNumberChanges_Scope");
        builder.HasOne<BranchRow>().WithMany().HasForeignKey(static r => new { r.CompanyId, r.BranchId })
            .HasPrincipalKey(static b => new { b.CompanyId, b.Id }).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<UserRow>().WithMany().HasForeignKey(static r => r.RequestedByUserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<UserRow>().WithMany().HasForeignKey(static r => r.DecidedByUserId).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class DocumentNumberAuditConfiguration : IEntityTypeConfiguration<DocumentNumberAuditRow>
{
    public void Configure(EntityTypeBuilder<DocumentNumberAuditRow> builder)
    {
        builder.ToTable("DocumentNumberAudit", "system", table =>
        {
            table.HasCheckConstraint("CK_DocumentNumberAudit_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_DocumentNumberAudit_Family", "[Family] BETWEEN 1 AND 3");
            table.HasCheckConstraint("CK_DocumentNumberAudit_Sequence", "[Sequence] > 0");
            table.HasCheckConstraint("CK_DocumentNumberAudit_TwoPerson", "[ApprovedByUserId] <> [RequestedByUserId]");
            table.HasCheckConstraint("CK_DocumentNumberAudit_Hash", "LEN([Hash]) = 64 AND LEN([PreviousHash]) = 64");
            table.HasCheckConstraint("CK_DocumentNumberAudit_Utc", "DATEPART(TZOFFSET, [OccurredAtUtc]) = 0");
        });
        builder.HasKey(static r => r.Id);
        builder.Property(static r => r.Id).ValueGeneratedNever();
        builder.Property(static r => r.Family).HasColumnType("tinyint");
        builder.Property(static r => r.OldNumber).HasMaxLength(64).UseCollation(StorageConventions.OrdinalCollation);
        builder.Property(static r => r.NewNumber).HasMaxLength(64).UseCollation(StorageConventions.OrdinalCollation);
        builder.Property(static r => r.OccurredAtUtc).HasPrecision(7);
        builder.Property(static r => r.PreviousHash).HasColumnType("char(64)").UseCollation(StorageConventions.OrdinalCollation);
        builder.Property(static r => r.Hash).HasColumnType("char(64)").UseCollation(StorageConventions.OrdinalCollation);
        builder.HasIndex(static r => new { r.CompanyId, r.Sequence }).IsUnique();
        builder.HasIndex(static r => r.RequestId).IsUnique();
        builder.HasIndex(static r => new { r.CompanyId, r.BranchId, r.Sequence });
        builder.HasIndex(static r => new { r.Family, r.DocumentId });
        builder.HasOne<DocumentNumberChangeRow>().WithMany().HasForeignKey(static r => r.RequestId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<UserRow>().WithMany().HasForeignKey(static r => r.RequestedByUserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<UserRow>().WithMany().HasForeignKey(static r => r.ApprovedByUserId).OnDelete(DeleteBehavior.NoAction);
    }
}
