using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Security;

internal sealed class SecurityAuditEventConfiguration : IEntityTypeConfiguration<SecurityAuditEventRow>
{
    public void Configure(EntityTypeBuilder<SecurityAuditEventRow> builder)
    {
        builder.ToTable("SecurityAuditEvents", SecurityStorage.Schema, table =>
        {
            table.HasCheckConstraint("CK_SecurityAuditEvents_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_SecurityAuditEvents_EventType", "[EventType] BETWEEN 1 AND 26");
            table.HasCheckConstraint("CK_SecurityAuditEvents_Utc", SecurityStorage.Utc("[OccurredAtUtc]"));
            table.HasTrigger(SecurityStorage.AuditAppendOnlyTrigger);
            table.UseSqlOutputClause(false);
        });
        builder.HasKey(static row => row.Id);
        builder.Property(static row => row.Id).ValueGeneratedNever();
        builder.Property(static row => row.OccurredAtUtc).HasPrecision(7);
        builder.Property(static row => row.EventType).HasColumnType("tinyint");
        builder.Property(static row => row.UserNameSnapshot).HasMaxLength(64);
        builder.Property(static row => row.IpAddress).HasMaxLength(45);
        builder.Property(static row => row.UserAgent).HasMaxLength(256);
        builder.Property(static row => row.CorrelationId).HasMaxLength(64);
        builder.Property(static row => row.Reason).HasMaxLength(500);
        builder.Property(static row => row.Details).HasMaxLength(4000);
        builder.HasIndex(static row => row.OccurredAtUtc);
        builder.HasIndex(static row => new { row.UserId, row.OccurredAtUtc });
        builder.HasIndex(static row => new { row.EventType, row.OccurredAtUtc });
    }
}
