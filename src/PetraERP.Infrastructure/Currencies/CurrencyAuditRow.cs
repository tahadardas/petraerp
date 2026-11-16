using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Security;

namespace PetraERP.Infrastructure.Currencies;

/// <summary>Append-only record of company currency-setting changes (base currency chosen/changed, currency enabled).</summary>
internal sealed class CurrencyAuditRow
{
    public const byte BaseCurrencyChanged = 1;
    public const byte CurrencyEnabled = 2;
    public const byte CurrencyCardChanged = 3;

    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public byte Action { get; set; }
    public required string CurrencyCode { get; set; }
    public string? PreviousCurrencyCode { get; set; }
    public required string Reason { get; set; }
    public Guid ActorUserId { get; set; }
    /// <summary>What changed (before → after) for card edits; null for other actions.</summary>
    public string? Details { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
}

internal sealed class CurrencyAuditConfiguration : IEntityTypeConfiguration<CurrencyAuditRow>
{
    public void Configure(EntityTypeBuilder<CurrencyAuditRow> builder)
    {
        builder.ToTable("CurrencyAudit", "system", table =>
        {
            table.HasCheckConstraint("CK_CurrencyAudit_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_CurrencyAudit_Action", "[Action] IN (1, 2, 3)");
            table.HasCheckConstraint("CK_CurrencyAudit_Reason", StorageConventions.NormalizedRequired("[Reason]"));
            table.HasCheckConstraint("CK_CurrencyAudit_Utc", "DATEPART(TZOFFSET, [OccurredAtUtc]) = 0");
            table.HasTrigger("TR_CurrencyAudit_AppendOnly");
            table.UseSqlOutputClause(false);
        });
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Action).HasColumnType("tinyint");
        builder.Property(r => r.CurrencyCode).HasColumnType("char(3)").UseCollation(StorageConventions.OrdinalCollation);
        builder.Property(r => r.PreviousCurrencyCode).HasColumnType("char(3)").UseCollation(StorageConventions.OrdinalCollation);
        builder.Property(r => r.Reason).HasMaxLength(500);
        builder.Property(r => r.Details).HasMaxLength(2000);
        builder.Property(r => r.OccurredAtUtc).HasPrecision(7);
        builder.HasIndex(r => new { r.CompanyId, r.OccurredAtUtc }).HasDatabaseName("IX_CurrencyAudit_CompanyTime");
        builder.HasOne<CompanyRow>().WithMany().HasForeignKey(r => r.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserRow>().WithMany().HasForeignKey(r => r.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CurrencyRow>().WithMany().HasForeignKey(r => r.CurrencyCode).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CurrencyRow>().WithMany().HasForeignKey(r => r.PreviousCurrencyCode).OnDelete(DeleteBehavior.Restrict);
    }
}
