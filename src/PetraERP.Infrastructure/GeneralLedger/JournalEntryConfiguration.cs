using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.AccountingCalendars;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.GeneralLedger;

internal sealed class JournalEntryConfiguration : IEntityTypeConfiguration<JournalEntryRow>
{
    public void Configure(EntityTypeBuilder<JournalEntryRow> builder)
    {
        builder.ToTable("JournalEntries", "ledger", table =>
        {
            table.HasCheckConstraint("CK_JournalEntries_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_JournalEntries_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_JournalEntries_BranchId", "[BranchId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_JournalEntries_State", "[State] IN (1, 2, 3, 4, 5)");
            table.HasCheckConstraint("CK_JournalEntries_Description", StorageConventions.NormalizedRequired("[Description]"));
            // Posting fields: all present or all absent for the three posting columns
            table.HasCheckConstraint("CK_JournalEntries_PostingConsistency",
                "([PostingSequence] IS NULL AND [PostingFiscalYearId] IS NULL AND [PostingPeriodId] IS NULL) OR " +
                "([PostingSequence] IS NOT NULL AND [PostingSequence] > 0 AND [PostingFiscalYearId] IS NOT NULL AND [PostingPeriodId] IS NOT NULL)");
            // State IN (4, 5) (Posted, Reversed) iff posting fields are set
            table.HasCheckConstraint("CK_JournalEntries_PostedState",
                "([State] IN (4, 5) AND [PostingSequence] IS NOT NULL) OR ([State] NOT IN (4, 5) AND [PostingSequence] IS NULL)");
            // Reversal self-FK: entry cannot reverse itself
            table.HasCheckConstraint("CK_JournalEntries_NotSelfReversal",
                "[ReversesEntryId] IS NULL OR [ReversesEntryId] <> [Id]");
            table.HasCheckConstraint("CK_JournalEntries_NotSelfCorrected",
                "[CorrectsEntryId] IS NULL OR [CorrectsEntryId] <> [Id]");
            StorageConventions.UtcCheck(table, "CK_JournalEntries_Utc");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.CompanyId).IsRequired();
        builder.Property(r => r.BranchId).IsRequired();
        builder.Property(r => r.AccountingDate).HasColumnType("date").IsRequired();
        builder.Property(r => r.Description).HasMaxLength(1000).IsRequired();
        builder.Property(r => r.State).HasColumnType("tinyint").IsRequired();
        builder.Property(r => r.PostingSequence);
        builder.Property(r => r.PostingFiscalYearId);
        builder.Property(r => r.PostingPeriodId);
        builder.Property(r => r.ReversesEntryId);
        builder.Property(r => r.ReversedByEntryId);
        builder.Property(r => r.CorrectsEntryId);

        StorageConventions.Tracking(builder);

        // Posting number uniqueness: one sequence per company+branch+year
        builder.HasIndex(r => new { r.CompanyId, r.BranchId, r.PostingFiscalYearId, r.PostingSequence })
               .IsUnique()
               .HasFilter("[PostingSequence] IS NOT NULL")
               .HasDatabaseName("UQ_JournalEntries_PostingNumber");

        // State lookup for pending/draft queries
        // PostingSequence completes the ledger's keyset order (date, sequence, id) so a page can seek instead of sort.
        builder.HasIndex(r => new { r.CompanyId, r.BranchId, r.State, r.AccountingDate, r.PostingSequence })
               .HasDatabaseName("IX_JournalEntries_CompanyBranchStateDate");

        // Reversal uniqueness: one reversal entry per original
        builder.HasIndex(r => r.ReversesEntryId)
               .IsUnique()
               .HasFilter("[ReversesEntryId] IS NOT NULL")
               .HasDatabaseName("UQ_JournalEntries_ReversesEntry");

        builder.HasOne<CompanyRow>()
               .WithMany()
               .HasForeignKey(r => r.CompanyId)
               .HasConstraintName("FK_JournalEntries_Companies")
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<BranchRow>()
               .WithMany()
               .HasForeignKey(r => r.BranchId)
               .HasConstraintName("FK_JournalEntries_Branches")
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<AccountingPeriodRow>()
               .WithMany()
               .HasForeignKey(r => r.PostingPeriodId)
               .HasConstraintName("FK_JournalEntries_PostingPeriod")
               .OnDelete(DeleteBehavior.Restrict)
               .IsRequired(false);

        builder.HasMany(r => r.Lines)
               .WithOne(l => l.Journal)
               .HasForeignKey(l => l.JournalId)
               .HasConstraintName("FK_JournalLines_JournalEntries")
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(r => r.AuditEvents)
               .WithOne(a => a.Journal)
               .HasForeignKey(a => a.JournalId)
               .HasConstraintName("FK_JournalAudit_JournalEntries")
               .OnDelete(DeleteBehavior.Restrict);
    }
}
