using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Security;

namespace PetraERP.Infrastructure.Persistence.Registry;

/// <summary>Central master pairing an account of one company with the mirror account of a counterparty company (separate databases).</summary>
public sealed class InterCompanyLinkRow : ITrackedStorageRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid CounterpartyCompanyId { get; set; }
    public string AccountCode { get; set; } = string.Empty;
    public string CounterpartyAccountCode { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string? Note { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

internal sealed class InterCompanyLinkConfiguration : IEntityTypeConfiguration<InterCompanyLinkRow>
{
    public void Configure(EntityTypeBuilder<InterCompanyLinkRow> builder)
    {
        builder.ToTable("InterCompanyLinks", "system", table =>
        {
            table.HasCheckConstraint("CK_InterCompanyLinks_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_InterCompanyLinks_Distinct", "[CompanyId] <> [CounterpartyCompanyId]");
            table.HasCheckConstraint("CK_InterCompanyLinks_Codes", StorageConventions.NormalizedRequired("[AccountCode]") + " AND " + StorageConventions.NormalizedRequired("[CounterpartyAccountCode]"));
            table.HasCheckConstraint("CK_InterCompanyLinks_Currency", "[CurrencyCode] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z]%'");
            StorageConventions.UtcCheck(table, "CK_InterCompanyLinks_Utc");
        });
        builder.HasKey(static r => r.Id);
        builder.Property(static r => r.Id).ValueGeneratedNever();
        builder.Property(static r => r.AccountCode).HasMaxLength(64).UseCollation(StorageConventions.OrdinalCollation);
        builder.Property(static r => r.CounterpartyAccountCode).HasMaxLength(64).UseCollation(StorageConventions.OrdinalCollation);
        builder.Property(static r => r.CurrencyCode).HasColumnType("char(3)").UseCollation(StorageConventions.OrdinalCollation);
        builder.Property(static r => r.Note).HasMaxLength(500);
        StorageConventions.Tracking(builder);
        builder.HasIndex(static r => new { r.CompanyId, r.CounterpartyCompanyId, r.AccountCode, r.CurrencyCode }).IsUnique();
        builder.HasOne<CompanyRow>().WithMany().HasForeignKey(static r => r.CompanyId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<CompanyRow>().WithMany().HasForeignKey(static r => r.CounterpartyCompanyId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<UserRow>().WithMany().HasForeignKey(static r => r.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
    }
}
