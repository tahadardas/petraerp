using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Treasury;

internal sealed class CommercialPaperConfiguration : IEntityTypeConfiguration<CommercialPaperRow>
{
    public void Configure(EntityTypeBuilder<CommercialPaperRow> builder)
    {
        builder.ToTable("CommercialPapers", "treasury", table =>
        {
            table.HasCheckConstraint("CK_CommercialPapers_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_CommercialPapers_Kind", "[PaperKind] IN (1, 2, 3)");
            table.HasCheckConstraint("CK_CommercialPapers_Status", "[Status] IN (1, 2, 3, 4, 5, 6)");
            table.HasCheckConstraint("CK_CommercialPapers_Amount", "[Amount] > 0");
            table.HasCheckConstraint("CK_CommercialPapers_ExchangeRate", "[ExchangeRate] > 0");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.PaperNumber).HasMaxLength(64).UseCollation(StorageConventions.OrdinalCollation);
        builder.Property(r => r.BankName).HasMaxLength(128);
        builder.Property(r => r.BranchName).HasMaxLength(128);
        builder.Property(r => r.DrawerName).HasMaxLength(128);
        builder.Property(r => r.PayeeName).HasMaxLength(128);
        builder.Property(r => r.CurrencyCode).HasMaxLength(3).IsFixedLength().IsUnicode(false);
        builder.Property(r => r.ExchangeRate).HasPrecision(19, 8);
        builder.Property(r => r.Amount).HasPrecision(19, 4);
        builder.Property(r => r.BaseAmount).HasPrecision(19, 4);
        builder.Property(r => r.Notes).HasMaxLength(500);

        builder.HasOne(r => r.Party).WithMany().HasForeignKey(r => r.PartyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.EndorsedParty).WithMany().HasForeignKey(r => r.EndorsedPartyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.PortfolioAccount).WithMany().HasForeignKey(r => r.PortfolioAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.IntermediaryAccount).WithMany().HasForeignKey(r => r.IntermediaryAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.BankAccount).WithMany().HasForeignKey(r => r.BankAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.LastJournalEntry).WithMany().HasForeignKey(r => r.LastJournalEntryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.Voucher).WithMany().HasForeignKey(r => r.VoucherId).OnDelete(DeleteBehavior.Restrict);

        StorageConventions.Tracking(builder);

        builder.HasIndex(r => new { r.CompanyId, r.BranchId, r.PaperNumber });
        builder.HasIndex(r => new { r.CompanyId, r.BranchId, r.Status });
        builder.HasIndex(r => new { r.CompanyId, r.DueDate, r.Status });
    }
}
