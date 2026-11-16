using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Application.DocumentProcessing;
using PetraERP.Infrastructure.AccountingCalendars;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Security;

namespace PetraERP.Infrastructure.DocumentProcessing;

internal sealed class BulkPostingBatchRow : ITrackedStorageRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid BranchId { get; set; }
    public Guid FiscalYearId { get; set; }
    public Guid RequestedByUserId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string FilterJson { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public byte Status { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

internal sealed class BulkPostingItemRow : ITrackedStorageRow
{
    public Guid Id { get; set; }
    public Guid BatchId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid BranchId { get; set; }
    public byte DocumentType { get; set; }
    public Guid DocumentId { get; set; }
    public string? DocumentNumber { get; set; }
    public DateTimeOffset DocumentDateUtc { get; set; }
    public string? PartyName { get; set; }
    public decimal? Amount { get; set; }
    public string? CurrencyCode { get; set; }
    public string PreviousStatus { get; set; } = string.Empty;
    public string? NewStatus { get; set; }
    public Guid? JournalEntryId { get; set; }
    public string? PostingNumber { get; set; }
    public byte Status { get; set; }
    public byte Classification { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? FailureCode { get; set; }
    public int AttemptCount { get; set; }
    public int ProcessingOrder { get; set; }
    public DateTimeOffset? LastAttemptAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

internal sealed class BulkPostingBatchConfiguration : IEntityTypeConfiguration<BulkPostingBatchRow>
{
    public void Configure(EntityTypeBuilder<BulkPostingBatchRow> b)
    {
        b.ToTable("BulkPostingBatches", "posting", t =>
        {
            t.HasCheckConstraint("CK_BulkPostingBatches_Id", StorageConventions.NonemptyId);
            t.HasCheckConstraint("CK_BulkPostingBatches_Key", StorageConventions.NormalizedRequired("[IdempotencyKey]"));
            t.HasCheckConstraint("CK_BulkPostingBatches_Reason", StorageConventions.NormalizedRequired("[Reason]"));
            t.HasCheckConstraint("CK_BulkPostingBatches_Status", "[Status] IN (0,1,2,3,4,5)");
            t.HasCheckConstraint("CK_BulkPostingBatches_Times",
                "([StartedAtUtc] IS NULL OR DATEPART(TZOFFSET,[StartedAtUtc])=0) AND ([CompletedAtUtc] IS NULL OR DATEPART(TZOFFSET,[CompletedAtUtc])=0) AND ([CompletedAtUtc] IS NULL OR [StartedAtUtc] IS NOT NULL)");
            StorageConventions.UtcCheck(t, "CK_BulkPostingBatches_Utc");
        });
        b.HasKey(r => r.Id);
        b.Property(r => r.Id).ValueGeneratedNever();
        b.Property(r => r.IdempotencyKey).HasMaxLength(128).IsRequired();
        b.Property(r => r.FilterJson).HasMaxLength(4000).IsRequired();
        b.Property(r => r.Reason).HasMaxLength(500).IsRequired();
        b.Property(r => r.StartedAtUtc).HasPrecision(7);
        b.Property(r => r.CompletedAtUtc).HasPrecision(7);
        StorageConventions.Tracking(b);
        b.HasIndex(r => new { r.CompanyId, r.IdempotencyKey }).IsUnique();
        b.HasIndex(r => new { r.CompanyId, r.BranchId, r.Status });
        b.HasOne<CompanyRow>().WithMany().HasForeignKey(r => r.CompanyId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<BranchRow>().WithMany().HasForeignKey(r => r.BranchId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<FiscalYearRow>().WithMany().HasForeignKey(r => r.FiscalYearId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserRow>().WithMany().HasForeignKey(r => r.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BulkPostingItemConfiguration : IEntityTypeConfiguration<BulkPostingItemRow>
{
    public void Configure(EntityTypeBuilder<BulkPostingItemRow> b)
    {
        b.ToTable("BulkPostingItems", "posting", t =>
        {
            t.HasCheckConstraint("CK_BulkPostingItems_Id", StorageConventions.NonemptyId);
            t.HasCheckConstraint("CK_BulkPostingItems_DocumentType", "[DocumentType] BETWEEN 0 AND 4");
            t.HasCheckConstraint("CK_BulkPostingItems_Status", "[Status] IN (0,1,2,3,4,5)");
            t.HasCheckConstraint("CK_BulkPostingItems_Classification", "[Classification] BETWEEN 0 AND 12");
            t.HasCheckConstraint("CK_BulkPostingItems_Amount", "[Amount] IS NULL OR [Amount] >= 0");
            t.HasCheckConstraint("CK_BulkPostingItems_Attempts", "[AttemptCount] >= 0 AND [ProcessingOrder] >= 0");
            t.HasCheckConstraint("CK_BulkPostingItems_Utc", "DATEPART(TZOFFSET,[DocumentDateUtc])=0 AND DATEPART(TZOFFSET,[CreatedAtUtc])=0 AND DATEPART(TZOFFSET,[UpdatedAtUtc])=0");
        });
        b.HasKey(r => r.Id);
        b.Property(r => r.Id).ValueGeneratedNever();
        b.Property(r => r.DocumentNumber).HasMaxLength(64);
        b.Property(r => r.DocumentDateUtc).HasPrecision(7);
        b.Property(r => r.PartyName).HasMaxLength(200);
        b.Property(r => r.Amount).HasPrecision(19, 4);
        b.Property(r => r.CurrencyCode).HasMaxLength(3);
        b.Property(r => r.PreviousStatus).HasMaxLength(32).IsRequired();
        b.Property(r => r.NewStatus).HasMaxLength(32);
        b.Property(r => r.PostingNumber).HasMaxLength(64);
        b.Property(r => r.Reason).HasMaxLength(1000).IsRequired();
        b.Property(r => r.FailureCode).HasMaxLength(64);
        b.Property(r => r.LastAttemptAtUtc).HasPrecision(7);
        StorageConventions.Tracking(b);
        b.HasIndex(r => new { r.BatchId, r.DocumentType, r.DocumentId }).IsUnique();
        b.HasIndex(r => new { r.BatchId, r.ProcessingOrder });
        b.HasIndex(r => new { r.BatchId, r.Status });
        // One running item per document across all batches prevents two operators posting the same document.
        b.HasIndex(r => new { r.CompanyId, r.BranchId, r.DocumentType, r.DocumentId })
            .IsUnique().HasFilter("[Status] = 1").HasDatabaseName("UX_BulkPostingItems_ActiveDocument");
        b.HasOne<BulkPostingBatchRow>().WithMany().HasForeignKey(r => r.BatchId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CompanyRow>().WithMany().HasForeignKey(r => r.CompanyId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<BranchRow>().WithMany().HasForeignKey(r => r.BranchId).OnDelete(DeleteBehavior.Restrict);
    }
}
