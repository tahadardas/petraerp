using Microsoft.EntityFrameworkCore;
using PetraERP.Infrastructure.AccountingCalendars;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Security;

namespace PetraERP.Infrastructure.Persistence.Actors;

/// <summary>Applied explicitly by AccountingDbContext only; legacy/System assembly discovery cannot apply it.</summary>
internal static class AccountingActorModel
{
    private static readonly string[] ActorProperties =
    ["ActorUserId", "ActorId", "CreatedByUserId", "ApprovedByUserId", "PostedByUserId", "CreatedBy", "PostedBy", "ApprovedBy", "ReversedBy", "ReviewedByUserId", "CompletedByUserId", "RequestedByUserId", "DecidedByUserId"];

    internal static void Configure(ModelBuilder model)
    {
        var actor = model.Entity<ActorReferenceRow>();
        actor.ToTable("ActorReferences", "audit", table =>
        {
            table.HasCheckConstraint("CK_ActorReferences_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_ActorReferences_Name", StorageConventions.NormalizedRequired("[DisplayNameArabic]"));
            table.HasCheckConstraint("CK_ActorReferences_Utc", "DATEPART(TZOFFSET, [FirstVerifiedAtUtc]) = 0");
            table.HasTrigger("TR_ActorReferences_Immutable");
            table.UseSqlOutputClause(false);
        });
        actor.HasKey(a => a.Id);
        actor.Property(a => a.Id).ValueGeneratedNever();
        actor.HasAlternateKey(a => new { a.CompanyId, a.Id });
        actor.Property(a => a.DisplayNameArabic).HasMaxLength(200).IsRequired();
        actor.Property(a => a.DisplayNameEnglish).HasMaxLength(200);
        actor.Property(a => a.FirstVerifiedAtUtc).HasPrecision(7);
        actor.HasOne<CompanyRow>().WithMany().HasForeignKey(a => a.CompanyId).OnDelete(DeleteBehavior.Restrict);

        foreach (var entity in model.Model.GetEntityTypes().ToArray())
        {
            foreach (var key in entity.GetForeignKeys().Where(k => k.PrincipalEntityType.ClrType == typeof(UserRow)).ToArray())
                entity.RemoveForeignKey(key);
            foreach (var property in entity.GetProperties().Where(p => ActorProperties.Contains(p.Name, StringComparer.Ordinal)
                && (p.ClrType == typeof(Guid) || p.ClrType == typeof(Guid?))).ToArray())
            {
                var relationship = model.Entity(entity.ClrType).HasOne(typeof(ActorReferenceRow)).WithMany();
                if (entity.FindProperty("CompanyId") is not null)
                    relationship.HasForeignKey("CompanyId", property.Name).HasPrincipalKey("CompanyId", "Id").OnDelete(DeleteBehavior.Restrict);
                else
                    relationship.HasForeignKey(property.Name).OnDelete(DeleteBehavior.Restrict);
            }
        }
        model.Ignore<UserRow>();
        foreach (var entity in model.Model.GetEntityTypes().Where(e => ImmutableTables.Contains(e.GetTableName()!, StringComparer.Ordinal)).ToArray())
            model.Entity(entity.ClrType).ToTable(entity.GetTableName()!, entity.GetSchema(), table =>
            {
                table.HasTrigger("TR_" + entity.GetTableName() + "_Immutable");
                table.UseSqlOutputClause(false);
            });
        StrengthenWorkspaceKeys(model);
        if (model.Model.GetEntityTypes().Any(e => e.ClrType.Namespace?.Contains(".Security", StringComparison.Ordinal) == true))
            throw new InvalidOperationException("Authentication entities must never enter the accounting model.");
    }

    private static readonly string[] ImmutableTables =
    ["AccountAudit", "AccountAuditStates", "CostCenterAudit", "CostCenterAuditStates", "JournalAudit", "PeriodAudit", "StockMovements", "CompanyPolicyAudit", "BranchAudit", "DocumentNumberAudit"];

    private static void StrengthenWorkspaceKeys(ModelBuilder model)
    {
        model.Entity<FiscalYearRow>().HasAlternateKey(y => new { y.CompanyId, y.Id });
        model.Entity<AccountingPeriodRow>().HasAlternateKey(p => new { p.CompanyId, p.FiscalYearId, p.Id });
        foreach (var entity in model.Model.GetEntityTypes().ToArray())
        {
            if (entity.FindProperty("CompanyId") is null) continue;
            foreach (var key in entity.GetForeignKeys().Where(k => k.PrincipalEntityType.ClrType == typeof(BranchRow)
                || k.PrincipalEntityType.ClrType == typeof(FiscalYearRow) || k.PrincipalEntityType.ClrType == typeof(AccountingPeriodRow)).ToArray())
            {
                if (key.Properties.Any(p => p.Name == "CompanyId")) continue;
                var principal = key.PrincipalEntityType.ClrType;
                var names = key.Properties.Select(p => p.Name).ToArray();
                var dependentNavigation = key.DependentToPrincipal?.Name;
                var principalNavigation = key.PrincipalToDependent?.Name;
                var deletion = key.DeleteBehavior;
                entity.RemoveForeignKey(key);
                var relationship = model.Entity(entity.ClrType).HasOne(principal, dependentNavigation).WithMany(principalNavigation);
                if (principal == typeof(AccountingPeriodRow) && (entity.FindProperty("FiscalYearId") ?? entity.FindProperty("PostingFiscalYearId")) is { } year)
                    relationship.HasForeignKey("CompanyId", year.Name, names.Single()).HasPrincipalKey("CompanyId", "FiscalYearId", "Id").OnDelete(deletion);
                else
                    relationship.HasForeignKey(["CompanyId", .. names]).HasPrincipalKey("CompanyId", "Id").OnDelete(deletion);
            }
        }
    }
}
