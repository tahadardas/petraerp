using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.GeneralLedger;

internal sealed class JournalAuditConfiguration : IEntityTypeConfiguration<JournalAuditRow>
{
    public void Configure(EntityTypeBuilder<JournalAuditRow> builder)
    {
        builder.ToTable("JournalAudit", "ledger", table =>
        {
            table.HasCheckConstraint("CK_JournalAudit_EventId", "[EventId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_JournalAudit_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_JournalAudit_BranchId", "[BranchId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_JournalAudit_JournalId", "[JournalId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_JournalAudit_Revision", "[Revision] > 0");
            // Action ordinals: 1=Created, 2=Revised, 3=Validated, 4=Approved, 5=ReturnedToDraft, 6=Posted, 7=Reversed
            table.HasCheckConstraint("CK_JournalAudit_Action", "[Action] IN (1, 2, 3, 4, 5, 6, 7)");
            table.HasCheckConstraint("CK_JournalAudit_NewState", "[NewState] IN (1, 2, 3, 4, 5)");
            table.HasCheckConstraint("CK_JournalAudit_PreviousState", "[PreviousState] IS NULL OR [PreviousState] IN (1, 2, 3, 4, 5)");
            table.HasCheckConstraint("CK_JournalAudit_ActorUserId", "[ActorUserId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_JournalAudit_CorrelationId", "[CorrelationId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_JournalAudit_Reason", StorageConventions.NormalizedRequired("[Reason]"));
            table.HasCheckConstraint("CK_JournalAudit_OccurredAtUtc", "DATEPART(TZOFFSET, [OccurredAtUtc]) = 0");
            table.HasCheckConstraint("CK_JournalAudit_CreatedAtUtc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0");
        });

        builder.HasKey(r => r.EventId);
        builder.Property(r => r.EventId).ValueGeneratedNever();
        builder.Property(r => r.CompanyId).IsRequired();
        builder.Property(r => r.BranchId).IsRequired();
        builder.Property(r => r.JournalId).IsRequired();
        builder.Property(r => r.Revision).IsRequired();
        builder.Property(r => r.Action).HasColumnType("tinyint").IsRequired();
        builder.Property(r => r.PreviousState).HasColumnType("tinyint");
        builder.Property(r => r.NewState).HasColumnType("tinyint").IsRequired();
        builder.Property(r => r.ActorUserId).IsRequired();
        builder.Property(r => r.CorrelationId).IsRequired();
        builder.Property(r => r.Reason).HasMaxLength(500).IsRequired();
        builder.Property(r => r.OccurredAtUtc).HasPrecision(7).IsRequired();
        builder.Property(r => r.RelatedEntryId);
        builder.Property(r => r.CreatedAtUtc)
               .HasPrecision(7)
               .HasDefaultValueSql("SYSUTCDATETIME()")
               .ValueGeneratedOnAdd();

        // Uniqueness: one event per journal+revision
        builder.HasIndex(r => new { r.JournalId, r.Revision })
               .IsUnique()
               .HasDatabaseName("UQ_JournalAudit_JournalRevision");

        // Lookup by company + journal for history fetch
        builder.HasIndex(r => new { r.CompanyId, r.JournalId, r.Revision })
               .HasDatabaseName("IX_JournalAudit_CompanyJournalRevision");

        builder.HasOne(r => r.Journal)
               .WithMany(j => j.AuditEvents)
               .HasForeignKey(r => r.JournalId)
               .HasConstraintName("FK_JournalAudit_JournalEntries")
               .OnDelete(DeleteBehavior.Restrict);
    }
}
