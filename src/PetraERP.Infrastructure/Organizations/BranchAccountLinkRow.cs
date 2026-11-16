using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Security;

namespace PetraERP.Infrastructure.Organizations;

/// <summary>
/// The account a branch uses for one posting role from <see cref="EffectiveFrom"/> to <see cref="EffectiveTo"/> (open-ended when null).
/// Posted journals keep the account they were posted with; changing a mapping only affects documents dated on or after the new date.
/// </summary>
internal sealed class BranchAccountLinkRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid BranchId { get; set; }
    public byte Role { get; set; }
    public Guid AccountId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

internal sealed class BranchAccountLinkConfiguration : IEntityTypeConfiguration<BranchAccountLinkRow>
{
    public void Configure(EntityTypeBuilder<BranchAccountLinkRow> builder)
    {
        builder.ToTable("BranchAccountLinks", "ledger", table =>
        {
            table.HasCheckConstraint("CK_BranchAccountLinks_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_BranchAccountLinks_Role", "[Role] BETWEEN 1 AND 11");
            table.HasCheckConstraint("CK_BranchAccountLinks_Range", "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]");
            table.HasCheckConstraint("CK_BranchAccountLinks_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [UpdatedAtUtc]) = 0");
        });
        builder.HasKey(static r => r.Id);
        builder.Property(static r => r.Id).ValueGeneratedNever();
        builder.Property(static r => r.Role).HasColumnType("tinyint");
        builder.Property(static r => r.EffectiveFrom).HasColumnType("date");
        builder.Property(static r => r.EffectiveTo).HasColumnType("date");
        builder.Property(static r => r.CreatedAtUtc).HasPrecision(7);
        builder.Property(static r => r.UpdatedAtUtc).HasPrecision(7);
        builder.HasIndex(static r => new { r.CompanyId, r.BranchId, r.Role, r.EffectiveFrom }).IsUnique();
        builder.HasIndex(static r => r.AccountId);
        builder.HasOne<BranchRow>().WithMany().HasForeignKey(static r => new { r.CompanyId, r.BranchId })
            .HasPrincipalKey(static b => new { b.CompanyId, b.Id }).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<AccountRow>().WithMany().HasForeignKey(static r => r.AccountId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<UserRow>().WithMany().HasForeignKey(static r => r.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
    }
}

/// <summary>Append-only history of branch card changes, activation and account mappings.</summary>
internal sealed class BranchAuditRow
{
    public const byte Created = 1;
    public const byte Updated = 2;
    public const byte Activated = 3;
    public const byte Deactivated = 4;
    public const byte AccountLinkSet = 5;

    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid BranchId { get; set; }
    public byte Action { get; set; }
    public required string Detail { get; set; }
    public required string Reason { get; set; }
    public Guid ActorUserId { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
}

internal sealed class BranchAuditConfiguration : IEntityTypeConfiguration<BranchAuditRow>
{
    public void Configure(EntityTypeBuilder<BranchAuditRow> builder)
    {
        builder.ToTable("BranchAudit", "ledger", table =>
        {
            table.HasCheckConstraint("CK_BranchAudit_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_BranchAudit_Action", "[Action] BETWEEN 1 AND 5");
            table.HasCheckConstraint("CK_BranchAudit_Reason", StorageConventions.NormalizedRequired("[Reason]"));
            table.HasCheckConstraint("CK_BranchAudit_Utc", "DATEPART(TZOFFSET, [OccurredAtUtc]) = 0");
        });
        builder.HasKey(static r => r.Id);
        builder.Property(static r => r.Id).ValueGeneratedNever();
        builder.Property(static r => r.Action).HasColumnType("tinyint");
        builder.Property(static r => r.Detail).HasMaxLength(2000);
        builder.Property(static r => r.Reason).HasMaxLength(400);
        builder.Property(static r => r.OccurredAtUtc).HasPrecision(7);
        builder.HasIndex(static r => new { r.CompanyId, r.BranchId, r.OccurredAtUtc });
        builder.HasOne<BranchRow>().WithMany().HasForeignKey(static r => new { r.CompanyId, r.BranchId })
            .HasPrincipalKey(static b => new { b.CompanyId, b.Id }).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<UserRow>().WithMany().HasForeignKey(static r => r.ActorUserId).OnDelete(DeleteBehavior.NoAction);
    }
}
