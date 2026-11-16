using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Persistence.Actors;
using PetraERP.Infrastructure.Security;

namespace PetraERP.Infrastructure.Tests;

public sealed class IndependentAccountingModelTests
{
    private static readonly string[] ActorFields = ["CompanyId", "DisplayNameArabic", "DisplayNameEnglish", "FirstVerifiedAtUtc", "Id"];
    [Fact]
    public void AnnualModelContainsExactlyApprovedBusinessTypesAndMinimalActorSnapshot()
    {
        using var annual = Context();
        using var legacy = new PetraDbContext(new DbContextOptionsBuilder<PetraDbContext>().UseSqlServer().Options);
        // All approved financial/master entities, plus the explicitly added local identity reference.
        var expected = legacy.Model.GetEntityTypes().Where(e => e.ClrType.Namespace is { } ns
            && !ns.Contains(".Security", StringComparison.Ordinal) && !ns.Contains(".Registry", StringComparison.Ordinal)
            && !ns.Contains(".Productivity", StringComparison.Ordinal)).Select(e => e.ClrType)
            .Append(typeof(ActorReferenceRow)).OrderBy(t => t.FullName, StringComparer.Ordinal).ToArray();
        Assert.Equal(79, expected.Length);
        Assert.Equal(expected, annual.Model.GetEntityTypes().Select(e => e.ClrType).OrderBy(t => t.FullName, StringComparer.Ordinal));
        var actor = annual.Model.FindEntityType(typeof(ActorReferenceRow))!;
        Assert.Equal(ActorFields,
            actor.GetProperties().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(annual.Model.GetEntityTypes(), e => e.GetSchema() == "security");
        Assert.DoesNotContain(annual.Model.GetEntityTypes().SelectMany(e => e.GetProperties()), p =>
            p.Name.Contains("Password", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Token", StringComparison.OrdinalIgnoreCase));
        Assert.False(annual.Database.HasPendingModelChanges());
        Assert.False(legacy.Database.HasPendingModelChanges());
    }

    [Fact]
    public void ActorForeignKeysAreLocalAndLegacyUserForeignKeysRemainUnchanged()
    {
        using var annual = Context();
        using var legacy = new PetraDbContext(new DbContextOptionsBuilder<PetraDbContext>().UseSqlServer().Options);
        var legacyReferences = legacy.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys())
            .Where(k => k.PrincipalEntityType.ClrType == typeof(UserRow)
                && annual.Model.FindEntityType(k.DeclaringEntityType.ClrType) is not null).ToArray();
        Assert.NotEmpty(legacyReferences);
        foreach (var key in legacyReferences)
        {
            var independent = annual.Model.FindEntityType(key.DeclaringEntityType.ClrType)!;
            Assert.Contains(independent.GetForeignKeys(), k => k.PrincipalEntityType.ClrType == typeof(ActorReferenceRow)
                && k.Properties[^1].Name == key.Properties[^1].Name);
        }
        Assert.Null(legacy.Model.FindEntityType(typeof(ActorReferenceRow)));
        Assert.All(annual.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()), k => Assert.NotEqual(typeof(UserRow), k.PrincipalEntityType.ClrType));
        var annualHistory = annual.GetService<Microsoft.EntityFrameworkCore.Migrations.IHistoryRepository>().GetCreateScript();
        Assert.Contains("__AccountingMigrationsHistory", annualHistory, StringComparison.Ordinal);
        Assert.Equal(AccountingSchema.ApprovedMigrations, annual.Database.GetMigrations());
    }

    [Fact]
    public async Task WritesWithoutAuthorityFailBeforeConnectingAndContextCannotChangeScope()
    {
        using (var context = Context())
        {
            context.Accounts.Add(new AccountRow { Id = Guid.NewGuid(), CompanyId = Guid.NewGuid(), Code = "1", NameArabic = "اختبار" });
            var exception = await Assert.ThrowsAsync<PetraERP.Application.Common.BusinessRuleException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken)).ConfigureAwait(true);
            Assert.Equal("accounting.write_scope_required", exception.Code);
        }
        // A failed unit of work is poisoned; a clean context is required, and its scope is immutable once bound.
        using var fresh = Context();
        var scope = new AccountingWriteScope(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Ledger.Accounts.Manage");
        fresh.BindWriteScope(scope);
        Assert.Throws<InvalidOperationException>(() => fresh.BindWriteScope(new AccountingWriteScope(scope.CompanyId, Guid.NewGuid(), scope.ActorUserId, "Ledger.Accounts.Manage")));
    }

    private static AccountingDbContext Context() => new(new DbContextOptionsBuilder<AccountingDbContext>().UseSqlServer().Options);
}
