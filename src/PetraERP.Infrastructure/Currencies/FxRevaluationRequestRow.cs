using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Currencies;

/// <summary>Durable FX-revaluation request tracking in the annual database. The canonical content fingerprint lets the
/// service replay an identical request, reject the same key with different economic content, and recover a retry after a
/// mid-sequence failure without creating an untracked duplicate financial effect. Status: 1 = Pending, 2 = Completed.</summary>
internal sealed class FxRevaluationRequestRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid BranchId { get; set; }
    public Guid FiscalYearId { get; set; }
    public required string IdempotencyKey { get; set; }
    public required string FingerprintHex { get; set; }
    public Guid ActorUserId { get; set; }
    public byte Status { get; set; }
    public Guid? DraftJournalId { get; set; }
    public Guid? OutcomeJournalId { get; set; }
    public DateTimeOffset RegisteredAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
}

internal sealed class FxRevaluationRequestConfiguration : IEntityTypeConfiguration<FxRevaluationRequestRow>
{
    public void Configure(EntityTypeBuilder<FxRevaluationRequestRow> builder)
    {
        builder.ToTable("FxRevaluationRequests", "ledger", table =>
        {
            table.HasCheckConstraint("CK_FxRevaluationRequests_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_FxRevaluationRequests_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_FxRevaluationRequests_BranchId", "[BranchId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_FxRevaluationRequests_FiscalYearId", "[FiscalYearId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_FxRevaluationRequests_ActorUserId", "[ActorUserId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_FxRevaluationRequests_Status", "[Status] IN (1, 2)");
            table.HasCheckConstraint("CK_FxRevaluationRequests_FingerprintHex", "LEN([FingerprintHex]) = 64");
            table.HasCheckConstraint("CK_FxRevaluationRequests_IdempotencyKey", "LEN([IdempotencyKey]) > 0");
            table.HasCheckConstraint("CK_FxRevaluationRequests_Completed",
                "([Status] = 2 AND [OutcomeJournalId] IS NOT NULL AND [CompletedAtUtc] IS NOT NULL) OR ([Status] = 1)");
            table.HasCheckConstraint("CK_FxRevaluationRequests_RegisteredAtUtc", "DATEPART(TZOFFSET, [RegisteredAtUtc]) = 0");
            table.HasCheckConstraint("CK_FxRevaluationRequests_CompletedAtUtc", "[CompletedAtUtc] IS NULL OR DATEPART(TZOFFSET, [CompletedAtUtc]) = 0");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.CompanyId).IsRequired();
        builder.Property(r => r.BranchId).IsRequired();
        builder.Property(r => r.FiscalYearId).IsRequired();
        builder.Property(r => r.ActorUserId).IsRequired();
        builder.Property(r => r.IdempotencyKey).HasMaxLength(128).UseCollation(StorageConventions.OrdinalCollation).IsRequired();
        builder.Property(r => r.FingerprintHex).HasMaxLength(64).UseCollation(StorageConventions.OrdinalCollation).IsRequired();
        builder.Property(r => r.Status).HasColumnType("tinyint").IsRequired();
        builder.Property(r => r.DraftJournalId);
        builder.Property(r => r.OutcomeJournalId);
        builder.Property(r => r.RegisteredAtUtc).HasPrecision(7).IsRequired();
        builder.Property(r => r.CompletedAtUtc).HasPrecision(7);

        // One reservation per caller idempotency key within a company/branch — enforces concurrency dedup at the SQL layer.
        builder.HasIndex(r => new { r.CompanyId, r.BranchId, r.IdempotencyKey })
               .IsUnique()
               .HasDatabaseName("UQ_FxRevaluationRequests_Scope_Key");
    }
}
