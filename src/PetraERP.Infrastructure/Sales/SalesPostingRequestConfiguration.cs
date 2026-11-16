using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Security;

namespace PetraERP.Infrastructure.Sales;

internal sealed class SalesPostingRequestConfiguration : IEntityTypeConfiguration<SalesPostingRequestRow>
{
    public void Configure(EntityTypeBuilder<SalesPostingRequestRow> builder)
    {
        builder.ToTable("PostingRequests", "sales", table =>
        {
            table.HasCheckConstraint("CK_SalesPostingRequests_Operation", "[Operation] IN (1,2)");
            table.HasCheckConstraint("CK_SalesPostingRequests_Key", "[IdempotencyKey]<>'00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_SalesPostingRequests_Completion", "([CompletedAtUtc] IS NULL AND [OutcomeJournalId] IS NULL) OR ([CompletedAtUtc] IS NOT NULL AND [OutcomeJournalId] IS NOT NULL)");
            table.HasCheckConstraint("CK_SalesPostingRequests_Version", "DATALENGTH([ExpectedInvoiceVersion])=8");
        });
        builder.HasKey(r => new { r.CompanyId, r.BranchId, r.Operation, r.IdempotencyKey });
        builder.Property(r => r.ExpectedInvoiceVersion).HasColumnType("binary(8)").IsRequired();
        builder.Property(r => r.Reason).HasMaxLength(1000).IsRequired();
        builder.Property(r => r.FingerprintHex).HasColumnType("char(64)").IsRequired();
        builder.Property(r => r.RegisteredAtUtc).HasPrecision(7);
        builder.Property(r => r.CompletedAtUtc).HasPrecision(7);
        builder.HasIndex(r => new { r.CompanyId, r.BranchId, r.InvoiceId });
        builder.HasOne<CompanyRow>().WithMany().HasForeignKey(r => r.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BranchRow>().WithMany().HasForeignKey(r => r.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserRow>().WithMany().HasForeignKey(r => r.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SalesInvoiceRow>().WithMany().HasForeignKey(r => r.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<JournalEntryRow>().WithMany().HasForeignKey(r => r.OutcomeJournalId).OnDelete(DeleteBehavior.Restrict);
    }
}
