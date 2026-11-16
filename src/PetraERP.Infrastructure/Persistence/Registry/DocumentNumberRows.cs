using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Security;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Persistence.Registry;

internal sealed class DocumentNumberCounterRow
{
    public Guid CompanyId { get; set; }
    public Guid PatternId { get; set; }
    public Guid ScopeBranchId { get; set; }
    public Guid ScopeFiscalYearId { get; set; }
    public int PeriodKey { get; set; }
    public long LastSequence { get; set; }
}
internal sealed class DocumentNumberReservationRow
{
    public Guid CompanyId { get; set; }
    public Guid AllocationKey { get; set; }
    public Guid PatternId { get; set; }
    public Guid RevisionId { get; set; }
    public Guid BranchId { get; set; }
    public Guid ScopeBranchId { get; set; }
    public Guid ScopeFiscalYearId { get; set; }
    public int PeriodKey { get; set; }
    public int DocumentFamily { get; set; }
    public long Sequence { get; set; }
    public string? RequestedManualNumber { get; set; }
    public string Number { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
    public Guid ActorUserId { get; set; }
    public DateTimeOffset ReservedAtUtc { get; set; }
}
internal sealed class DocumentNumberCounterConfiguration:IEntityTypeConfiguration<DocumentNumberCounterRow>
{
    public void Configure(EntityTypeBuilder<DocumentNumberCounterRow> b)
    {
        b.ToTable("DocumentNumberCounters","patterns",t=>t.HasCheckConstraint("CK_DocumentNumberCounters_Sequence","[LastSequence] >= 0"));
        b.HasKey(r=>new {r.CompanyId,r.PatternId,r.ScopeBranchId,r.ScopeFiscalYearId,r.PeriodKey});
        b.HasOne<CompanyRow>().WithMany().HasForeignKey(r=>r.CompanyId).OnDelete(DeleteBehavior.Restrict);
    }
}
internal sealed class DocumentNumberReservationConfiguration:IEntityTypeConfiguration<DocumentNumberReservationRow>
{
    public void Configure(EntityTypeBuilder<DocumentNumberReservationRow> b)
    {
        b.ToTable("DocumentNumberReservations","patterns",t=>
        {
            t.HasCheckConstraint("CK_DocumentNumberReservations_Sequence","[Sequence] >= 0");
            t.HasCheckConstraint("CK_DocumentNumberReservations_Number",StorageConventions.NormalizedRequired("[Number]"));
            t.HasCheckConstraint("CK_DocumentNumberReservations_Utc","DATEPART(TZOFFSET,[ReservedAtUtc])=0");
        });
        b.HasKey(r=>new {r.CompanyId,r.AllocationKey});
        b.Property(r=>r.RequestedManualNumber).HasMaxLength(128);
        b.Property(r=>r.Number).HasMaxLength(128).UseCollation(StorageConventions.OrdinalCollation).IsRequired();
        b.Property(r=>r.Fingerprint).HasMaxLength(64).IsRequired();
        b.Property(r=>r.ReservedAtUtc).HasPrecision(7);
        b.HasIndex(r=>new {r.CompanyId,r.DocumentFamily,r.ScopeBranchId,r.ScopeFiscalYearId,r.Number}).IsUnique();
        b.HasOne<CompanyRow>().WithMany().HasForeignKey(r=>r.CompanyId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<BranchRow>().WithMany().HasForeignKey(r=>new {r.CompanyId,r.BranchId}).HasPrincipalKey(r=>new {r.CompanyId,r.Id}).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserRow>().WithMany().HasForeignKey(r=>r.ActorUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
