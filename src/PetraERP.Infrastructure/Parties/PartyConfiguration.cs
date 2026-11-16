using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Parties;

internal sealed class PartyConfiguration : IEntityTypeConfiguration<PartyRow>
{
    public void Configure(EntityTypeBuilder<PartyRow> builder)
    {
        builder.ToTable("Parties", "parties", table =>
        {
            table.HasCheckConstraint("CK_Parties_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_Parties_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_Parties_Code", StorageConventions.NormalizedRequired("[Code]"));
            table.HasCheckConstraint("CK_Parties_Names", StorageConventions.NormalizedNames);
            table.HasCheckConstraint("CK_Parties_Role", "[Role] IN (1, 2, 3)");
            table.HasCheckConstraint("CK_Parties_CreditPolicyKind", "[CreditPolicyKind] IN (1, 2, 3)");
            table.HasCheckConstraint("CK_Parties_CreditLimit", "[CreditLimit] >= 0");
            table.HasCheckConstraint("CK_Parties_PaymentTermDays", "[PaymentTermDays] >= 0");
            table.HasCheckConstraint("CK_Parties_ProfileDiscount", "[DiscountPercent] >= 0 AND [DiscountPercent] <= 100");
            StorageConventions.UtcCheck(table, "CK_Parties_Utc");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.CompanyId).IsRequired();
        builder.Property(r => r.Code).HasMaxLength(64).UseCollation(StorageConventions.OrdinalCollation).IsRequired();
        builder.Property(r => r.NameArabic).HasMaxLength(200).IsRequired();
        builder.Property(r => r.NameEnglish).HasMaxLength(200);
        builder.Property(r => r.NormalizedSearchText).HasMaxLength(500).IsRequired();
        builder.Property(r => r.Role).IsRequired();
        builder.Property(r => r.IsActive).IsRequired();
        builder.Property(r => r.TaxNumber).HasMaxLength(64);
        builder.Property(r => r.CommercialRegistration).HasMaxLength(64);
        builder.Property(r => r.DefaultCurrencyCode).HasMaxLength(3).UseCollation(StorageConventions.OrdinalCollation);
        builder.Property(r => r.ReceivableAccountId);
        builder.Property(r => r.PayableAccountId);
        builder.Property(r => r.CreditLimit).HasPrecision(19, 4).IsRequired();
        builder.Property(r => r.IsCashCustomer).HasDefaultValue(false);
        builder.Property(r => r.IsCreditBlocked).HasDefaultValue(false);
        builder.Property(r => r.Notes).HasMaxLength(1000);
        builder.Property(r => r.Title).HasMaxLength(100);
        builder.Property(r => r.Suffix).HasMaxLength(100);
        builder.Property(r => r.Barcode).HasMaxLength(64);
        builder.Property(r => r.HideInSearch).HasDefaultValue(false);
        builder.Property(r => r.DiscountPercent).HasPrecision(9, 4).HasDefaultValueSql("0");
        builder.Property(r => r.SalesPriceType).HasMaxLength(32).HasDefaultValueSql("N'CONSUMER'").IsRequired();
        builder.HasIndex(r => new { r.CompanyId, r.Barcode }).IsUnique().HasFilter("[Barcode] IS NOT NULL").HasDatabaseName("UQ_Parties_CompanyBarcode");
        builder.HasOne<AccountRow>().WithMany().HasForeignKey(r => r.ConditionalDiscountAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AccountRow>().WithMany().HasForeignKey(r => r.VatAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(r => new { r.CompanyId, r.IsCashCustomer }).IsUnique().HasFilter("[IsCashCustomer]=1").HasDatabaseName("UQ_Parties_CashCustomer");
        builder.Property(r => r.PaymentTermDays).IsRequired();
        builder.Property(r => r.CreditPolicyKind).IsRequired();
        builder.Property(r => r.ContactsJson);
        builder.Property(r => r.AddressesJson);

        StorageConventions.Tracking(builder);

        builder.HasIndex(r => new { r.CompanyId, r.Code })
               .IsUnique()
               .HasDatabaseName("UQ_Parties_CompanyCode");

        builder.HasIndex(r => new { r.CompanyId, r.Role, r.IsActive })
               .HasDatabaseName("IX_Parties_CompanyRoleActive");

        builder.HasIndex(r => new { r.CompanyId, r.NormalizedSearchText })
               .HasDatabaseName("IX_Parties_CompanyNormalizedSearchText");

        builder.HasOne<CompanyRow>()
               .WithMany()
               .HasForeignKey(r => r.CompanyId)
               .HasConstraintName("FK_Parties_Companies")
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.ReceivableAccount)
               .WithMany()
               .HasForeignKey(r => r.ReceivableAccountId)
               .HasConstraintName("FK_Parties_ReceivableAccount")
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.PayableAccount)
               .WithMany()
               .HasForeignKey(r => r.PayableAccountId)
               .HasConstraintName("FK_Parties_PayableAccount")
               .OnDelete(DeleteBehavior.Restrict);
    }
}
