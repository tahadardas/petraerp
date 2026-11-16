using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.Currencies;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Parties.OpenItems;

internal sealed class OpenItemConfiguration : IEntityTypeConfiguration<OpenItemRow>
{
    public void Configure(EntityTypeBuilder<OpenItemRow> builder)
    {
        builder.ToTable("OpenItems", "parties", table =>
        {
            table.HasCheckConstraint("CK_OpenItems_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_OpenItems_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_OpenItems_BranchId", "[BranchId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_OpenItems_PartyId", "[PartyId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_OpenItems_PartyType", "[PartyType] IN (1, 2)");
            table.HasCheckConstraint("CK_OpenItems_OriginType", "[OriginType] IN (1, 2, 3, 4)");
            table.HasCheckConstraint("CK_OpenItems_State", "[State] IN (1, 2, 3, 4)");
            table.HasCheckConstraint("CK_OpenItems_Amounts",
                "[OriginalAmount] > 0 AND [SettledAmount] >= 0 AND [OutstandingAmount] >= 0 AND [SettledAmount] + [OutstandingAmount] = [OriginalAmount]");
            table.HasCheckConstraint("CK_OpenItems_CarryingAmounts",
                "[OriginalBaseCarryingAmount] >= 0 AND [CurrentBaseCarryingAmount] >= 0");
            table.HasCheckConstraint("CK_OpenItems_ExchangeRate", "[ExchangeRate] > 0");
            StorageConventions.UtcCheck(table, "CK_OpenItems_Utc");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.CompanyId).IsRequired();
        builder.Property(r => r.BranchId).IsRequired();
        builder.Property(r => r.PartyId).IsRequired();
        builder.Property(r => r.PartyType).HasColumnType("tinyint").IsRequired();
        builder.Property(r => r.OriginFiscalYearId);
        builder.Property(r => r.OriginDocumentId);
        builder.Property(r => r.OriginDocumentNumber).HasMaxLength(64);
        builder.Property(r => r.OriginDocumentDateUtc).HasPrecision(7).IsRequired();
        builder.Property(r => r.DueDateUtc).HasPrecision(7);
        builder.Property(r => r.CurrencyCode).HasColumnType("char(3)").UseCollation(StorageConventions.OrdinalCollation).IsRequired();
        builder.Property(r => r.OriginalAmount).HasPrecision(19, 4).IsRequired();
        builder.Property(r => r.SettledAmount).HasPrecision(19, 4).IsRequired();
        builder.Property(r => r.OutstandingAmount).HasPrecision(19, 4).IsRequired();
        builder.Property(r => r.ExchangeRate).HasPrecision(19, 8).IsRequired();
        builder.Property(r => r.OriginalBaseCarryingAmount).HasPrecision(19, 4).IsRequired();
        builder.Property(r => r.CurrentBaseCarryingAmount).HasPrecision(19, 4).IsRequired();
        builder.Property(r => r.OriginType).HasColumnType("tinyint").IsRequired();
        builder.Property(r => r.SourceReference).HasMaxLength(128);
        builder.Property(r => r.RolloverBatchId);
        builder.Property(r => r.AccountId).IsRequired();
        builder.Property(r => r.State).HasColumnType("tinyint").IsRequired();

        StorageConventions.Tracking(builder);

        builder.HasIndex(r => new { r.CompanyId, r.BranchId, r.PartyId, r.State })
            .HasDatabaseName("IX_OpenItems_CompanyBranchPartyState");

        builder.HasIndex(r => new { r.CompanyId, r.PartyId, r.OutstandingAmount })
            .HasDatabaseName("IX_OpenItems_CompanyPartyOutstanding");

        builder.HasOne<CompanyRow>()
            .WithMany()
            .HasForeignKey(r => r.CompanyId)
            .HasConstraintName("FK_OpenItems_Companies")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<BranchRow>()
            .WithMany()
            .HasForeignKey(r => r.BranchId)
            .HasConstraintName("FK_OpenItems_Branches")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<PartyRow>()
            .WithMany()
            .HasForeignKey(r => r.PartyId)
            .HasConstraintName("FK_OpenItems_Parties")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<AccountRow>()
            .WithMany()
            .HasForeignKey(r => r.AccountId)
            .HasConstraintName("FK_OpenItems_Accounts")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
