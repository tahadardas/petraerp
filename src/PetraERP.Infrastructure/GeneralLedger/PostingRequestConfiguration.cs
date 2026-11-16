using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.GeneralLedger;

internal sealed class PostingRequestConfiguration : IEntityTypeConfiguration<PostingRequestRow>
{
    public void Configure(EntityTypeBuilder<PostingRequestRow> builder)
    {
        builder.ToTable("PostingRequests", "ledger", table =>
        {
            table.HasCheckConstraint("CK_PostingRequests_IdempotencyKey", "[IdempotencyKey] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_PostingRequests_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_PostingRequests_BranchId", "[BranchId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_PostingRequests_ActorUserId", "[ActorUserId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_PostingRequests_Operation", "[Operation] IN (1, 2)");
            table.HasCheckConstraint("CK_PostingRequests_JournalId", "[JournalId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_PostingRequests_ExpectedRevision", "[ExpectedRevision] > 0");
            table.HasCheckConstraint("CK_PostingRequests_FingerprintHex", "LEN([FingerprintHex]) = 64");
            table.HasCheckConstraint("CK_PostingRequests_Reason", StorageConventions.NormalizedRequired("[Reason]"));
            // Reversal consistency: OriginalJournalId iff Operation=2 (PostJournalReversal)
            table.HasCheckConstraint("CK_PostingRequests_ReversalConsistency",
                "([Operation] = 2 AND [OriginalJournalId] IS NOT NULL AND [OriginalJournalId] <> '00000000-0000-0000-0000-000000000000') OR " +
                "([Operation] = 1 AND [OriginalJournalId] IS NULL)");
            table.HasCheckConstraint("CK_PostingRequests_RegisteredAtUtc", "DATEPART(TZOFFSET, [RegisteredAtUtc]) = 0");
            table.HasCheckConstraint("CK_PostingRequests_CompletedAtUtc",
                "[CompletedAtUtc] IS NULL OR DATEPART(TZOFFSET, [CompletedAtUtc]) = 0");
        });

        builder.HasKey(r => new { r.CompanyId, r.BranchId, r.Operation, r.IdempotencyKey });
        builder.Property(r => r.IdempotencyKey).ValueGeneratedNever();
        builder.Property(r => r.CompanyId).IsRequired();
        builder.Property(r => r.BranchId).IsRequired();
        builder.Property(r => r.ActorUserId).IsRequired();
        builder.Property(r => r.Operation).HasColumnType("tinyint").IsRequired();
        builder.Property(r => r.JournalId).IsRequired();
        builder.Property(r => r.ExpectedRevision).IsRequired();
        builder.Property(r => r.ExpectedVersionBase64).HasMaxLength(200).IsRequired();
        builder.Property(r => r.OriginalJournalId);
        builder.Property(r => r.OriginalExpectedRevision);
        builder.Property(r => r.OriginalVersionBase64).HasMaxLength(200);
        builder.Property(r => r.Reason).HasMaxLength(500).IsRequired();
        builder.Property(r => r.FingerprintHex).HasMaxLength(64).UseCollation(StorageConventions.OrdinalCollation).IsRequired();
        builder.Property(r => r.RegisteredAtUtc)
               .HasPrecision(7)
               .HasDefaultValueSql("SYSUTCDATETIME()")
               .ValueGeneratedOnAdd()
               .IsRequired();
        builder.Property(r => r.CompletedAtUtc).HasPrecision(7);
        builder.Property(r => r.OutcomeJournalId);

        // Allow fast lookup: "what's the outcome for this journal?" (replay)
        builder.HasIndex(r => new { r.CompanyId, r.BranchId, r.JournalId })
               .HasDatabaseName("IX_PostingRequests_CompanyBranchJournal");
    }
}
