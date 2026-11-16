using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using PetraERP.Application.Security;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Security;

namespace PetraERP.Infrastructure.Tests.Security;

public sealed class SecurityModelTests
{
    private static readonly string[] BranchScopeForeignKey = ["CompanyId", "BranchId"];
    private static readonly string[] BranchPrincipalKey = ["CompanyId", "Id"];
    private static readonly string[] CompanyScopeForeignKey = ["UserId", "CompanyId"];

    private static PetraDbContext Context() => new(new DbContextOptionsBuilder<PetraDbContext>().UseSqlServer().Options);

    [Fact]
    public void SecuritySchemaIsIsolatedWithElevenTablesAndNoCascadeDeletes()
    {
        using var context = Context();
        var entities = context.Model.GetEntityTypes().Where(static entity => entity.GetSchema() == "security").ToArray();
        Assert.Equal(11, entities.Length);
        Assert.All(entities.SelectMany(static entity => entity.GetForeignKeys()),
            static foreignKey => Assert.Equal(DeleteBehavior.NoAction, foreignKey.DeleteBehavior));
        Assert.DoesNotContain(context.Model.GetEntityTypes(), static entity =>
            entity.GetSchema() == "ledger" && entity.ClrType.Namespace == typeof(UserRow).Namespace);
    }

    [Fact]
    public void BranchScopeCannotReferenceABranchOfAnotherCompany()
    {
        using var context = Context();
        var scope = context.Model.FindEntityType(typeof(UserBranchScopeRow))!;
        var toBranch = Assert.Single(scope.GetForeignKeys(), static key => key.PrincipalEntityType.ClrType == typeof(BranchRow));
        Assert.Equal(BranchScopeForeignKey, toBranch.Properties.Select(static property => property.Name));
        Assert.Equal(BranchPrincipalKey, toBranch.PrincipalKey.Properties.Select(static property => property.Name));
        var toCompanyScope = Assert.Single(scope.GetForeignKeys(), static key =>
            key.PrincipalEntityType.ClrType == typeof(UserCompanyScopeRow));
        Assert.Equal(CompanyScopeForeignKey, toCompanyScope.Properties.Select(static property => property.Name));
    }

    [Fact]
    public void CredentialsAreStoredOnlyAsFixedSizeVerifiersAndHashes()
    {
        using var context = Context();
        var user = context.Model.FindEntityType(typeof(UserRow))!;
        Assert.Equal("binary(64)", user.FindProperty(nameof(UserRow.PasswordHash))!.GetColumnType());
        Assert.Equal("binary(32)", user.FindProperty(nameof(UserRow.PasswordSalt))!.GetColumnType());
        Assert.DoesNotContain(user.GetProperties(), static property =>
            property.Name.Equals("Password", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(user.GetIndexes(), static index => index.IsUnique
            && index.Properties.Single().Name == nameof(UserRow.NormalizedUserName));

        var token = context.Model.FindEntityType(typeof(RefreshTokenRow))!;
        Assert.Equal("binary(32)", token.FindProperty(nameof(RefreshTokenRow.TokenHash))!.GetColumnType());
        Assert.Contains(token.GetIndexes(), static index => index.IsUnique
            && index.Properties.Single().Name == nameof(RefreshTokenRow.TokenHash));
        Assert.DoesNotContain(token.GetProperties(), static property => property.Name == "Token");
    }

    [Fact]
    public void SecurityAuditIsAppendOnlyWithoutRowVersionAndHasDatabaseTrigger()
    {
        using var context = Context();
        var audit = context.Model.FindEntityType(typeof(SecurityAuditEventRow))!;
        Assert.Null(audit.FindProperty("RowVersion"));
        Assert.Empty(audit.GetForeignKeys());
        Assert.Contains(audit.GetDeclaredTriggers(), static trigger => trigger.ModelName == "TR_SecurityAuditEvents_AppendOnly");

        var script = context.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
        Assert.Contains("CREATE TRIGGER [security].[TR_SecurityAuditEvents_AppendOnly]", script, StringComparison.Ordinal);
        Assert.Contains("INSTEAD OF UPDATE, DELETE", script, StringComparison.Ordinal);
    }

    [Fact]
    public void PermissionSeedMatchesTheApplicationCatalogExactly()
    {
        using var context = Context();
        var permission = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(PermissionRow))!;
        var seeded = permission.GetSeedData().Select(static row => (string)row[nameof(PermissionRow.Code)]!).Order(StringComparer.Ordinal);
        Assert.Equal(PermissionCatalog.All.Select(static definition => definition.Code), seeded);
    }

    [Fact]
    public void SecurityModelHasNoPendingMigrations()
    {
        using var context = Context();
        Assert.False(context.Database.HasPendingModelChanges());
    }
}
