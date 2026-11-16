using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.Currencies;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.GeneralLedger.OpeningBalances;

internal sealed class OpeningBalanceBatchConfiguration : IEntityTypeConfiguration<OpeningBalanceBatchRow>
{
    public void Configure(EntityTypeBuilder<OpeningBalanceBatchRow> builder)
    {
        builder.ToTable("OpeningBalanceBatches", "ledger", table =>
        {
            table.HasCheckConstraint("CK_OpeningBalanceBatches_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_OpeningBalanceBatches_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_OpeningBalanceBatches_FiscalYearId", "[FiscalYearId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_OpeningBalanceBatches_ApprovalState", "[ApprovalState] IN (1, 2, 3, 4, 5)");
            table.HasCheckConstraint("CK_OpeningBalanceBatches_SourceType", "LEN(LTRIM(RTRIM([SourceType]))) > 0");
            StorageConventions.UtcCheck(table, "CK_OpeningBalanceBatches_Utc");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.CompanyId).IsRequired();
        builder.Property(r => r.FiscalYearId).IsRequired();
        builder.Property(r => r.BranchId);
        builder.Property(r => r.OpeningDate).IsRequired();
        builder.Property(r => r.SourceType).HasMaxLength(32).IsRequired();
        builder.Property(r => r.SourceReference).HasMaxLength(128);
        builder.Property(r => r.SourceFingerprint).HasMaxLength(64);
        builder.Property(r => r.OriginalCurrency).HasColumnType("char(3)").UseCollation(StorageConventions.OrdinalCollation).IsRequired();
        builder.Property(r => r.BaseCurrency).HasColumnType("char(3)").UseCollation(StorageConventions.OrdinalCollation).IsRequired();
        builder.Property(r => r.ApprovalState).HasColumnType("tinyint").IsRequired();
        builder.Property(r => r.ActorId).IsRequired();
        builder.Property(r => r.ApprovedByActorId);
        builder.Property(r => r.ApprovedAtUtc);
        builder.Property(r => r.PostedJournalId);
        builder.Property(r => r.ReversalJournalId);
        builder.Property(r => r.IdempotencyKey).HasMaxLength(128).UseCollation(StorageConventions.OrdinalCollation).IsRequired();

        StorageConventions.Tracking(builder);

        builder.HasIndex(r => new { r.CompanyId, r.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("UQ_OpeningBalanceBatches_CompanyIdempotency");

        builder.HasIndex(r => new { r.CompanyId, r.FiscalYearId, r.ApprovalState })
            .HasDatabaseName("IX_OpeningBalanceBatches_CompanyYearState");

        builder.HasOne<CompanyRow>()
            .WithMany()
            .HasForeignKey(r => r.CompanyId)
            .HasConstraintName("FK_OpeningBalanceBatches_Companies")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<BranchRow>()
            .WithMany()
            .HasForeignKey(r => r.BranchId)
            .HasConstraintName("FK_OpeningBalanceBatches_Branches")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.HasOne<JournalEntryRow>()
            .WithMany()
            .HasForeignKey(r => r.PostedJournalId)
            .HasConstraintName("FK_OpeningBalanceBatches_PostedJournal")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.HasOne<JournalEntryRow>()
            .WithMany()
            .HasForeignKey(r => r.ReversalJournalId)
            .HasConstraintName("FK_OpeningBalanceBatches_ReversalJournal")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.HasMany(r => r.Lines)
            .WithOne(l => l.Batch)
            .HasForeignKey(l => l.BatchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class OpeningBalanceLineConfiguration : IEntityTypeConfiguration<OpeningBalanceLineRow>
{
    public void Configure(EntityTypeBuilder<OpeningBalanceLineRow> builder)
    {
        builder.ToTable("OpeningBalanceLines", "ledger", table =>
        {
            table.HasCheckConstraint("CK_OpeningBalanceLines_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_OpeningBalanceLines_OriginalAmounts",
                "[DebitOriginal] >= 0 AND [CreditOriginal] >= 0 AND (([DebitOriginal] > 0 AND [CreditOriginal] = 0) OR ([DebitOriginal] = 0 AND [CreditOriginal] > 0))");
            table.HasCheckConstraint("CK_OpeningBalanceLines_BaseAmounts",
                "[DebitBase] >= 0 AND [CreditBase] >= 0 AND (([DebitBase] > 0 AND [CreditBase] = 0) OR ([DebitBase] = 0 AND [CreditBase] > 0))");
            table.HasCheckConstraint("CK_OpeningBalanceLines_ExchangeRate", "[ExchangeRate] > 0");
            table.HasCheckConstraint("CK_OpeningBalanceLines_LineOrder", "[LineOrder] >= 0");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.BatchId).IsRequired();
        builder.Property(r => r.AccountId).IsRequired();
        builder.Property(r => r.CostCenterId);
        builder.Property(r => r.CurrencyCode).HasColumnType("char(3)").UseCollation(StorageConventions.OrdinalCollation).IsRequired();
        builder.Property(r => r.DebitOriginal).HasPrecision(19, 4).IsRequired();
        builder.Property(r => r.CreditOriginal).HasPrecision(19, 4).IsRequired();
        builder.Property(r => r.ExchangeRate).HasPrecision(19, 8).IsRequired();
        builder.Property(r => r.DebitBase).HasPrecision(19, 4).IsRequired();
        builder.Property(r => r.CreditBase).HasPrecision(19, 4).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(255);
        builder.Property(r => r.LineOrder).IsRequired();

        builder.HasOne<AccountRow>()
            .WithMany()
            .HasForeignKey(r => r.AccountId)
            .HasConstraintName("FK_OpeningBalanceLines_Accounts")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<CostCenterRow>()
            .WithMany()
            .HasForeignKey(r => r.CostCenterId)
            .HasConstraintName("FK_OpeningBalanceLines_CostCenters")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);
    }
}
