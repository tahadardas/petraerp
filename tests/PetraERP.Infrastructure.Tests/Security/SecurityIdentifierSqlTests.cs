using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PetraERP.Application.Security;

namespace PetraERP.Infrastructure.Tests.Security;

public sealed class SecurityIdentifierSqlTests
{
    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ForwardMigrationAcceptsContractCharactersRejectsOthersAndNeverRenamesDataOnDowngrade()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            using var context = database.Context(); var migrator = context.GetService<IMigrator>();
            const string previous = "20261017092305_AccountantWorkspaceFoundation";
            await migrator.MigrateAsync(previous, Cancellation).ConfigureAwait(true);
            var before = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlRawAsync("INSERT [security].[Roles] ([Id],[Code],[NameArabic]) VALUES (NEWID(),N'A_B',N'دور');", Cancellation)).ConfigureAwait(true);
            Assert.Equal(547, before.Number);
            await migrator.MigrateAsync(cancellationToken: Cancellation).ConfigureAwait(true);
            foreach (var code in new[] { "AB", "A_B", "A-B", "A.B", "A09" })
                await context.Database.ExecuteSqlInterpolatedAsync($"INSERT [security].[Roles] ([Id],[Code],[NameArabic]) VALUES ({Guid.NewGuid()},{code},N'دور عربي');", Cancellation).ConfigureAwait(true);
            foreach (var invalid in new[] { "aB", "A!B", "A[B", "A B", "AآB" })
            {
                var rejected = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync($"INSERT [security].[Roles] ([Id],[Code],[NameArabic]) VALUES ({Guid.NewGuid()},{invalid},N'دور');", Cancellation)).ConfigureAwait(true);
                Assert.Equal(547, rejected.Number);
            }
            var down = await Assert.ThrowsAsync<SqlException>(() => migrator.MigrateAsync(previous, Cancellation)).ConfigureAwait(true);
            Assert.Equal(51004, down.Number); Assert.Equal(5, await context.Roles.CountAsync(Cancellation).ConfigureAwait(true));
            // Remove only the two fixture rows in this owned temporary database, then prove reversible schema changes.
            await context.Database.ExecuteSqlRawAsync("DELETE [security].[Roles] WHERE [Code] IN (N'A_B',N'A-B');", Cancellation).ConfigureAwait(true);
            await migrator.MigrateAsync(previous, Cancellation).ConfigureAwait(true);
            await migrator.MigrateAsync(cancellationToken: Cancellation).ConfigureAwait(true);
            var bootstrap = await SecuritySqlFixture.BootstrapAsync(context, Cancellation).ConfigureAwait(true);
            var user = await SecuritySqlFixture.Administration(context).CreateUserAsync(new CreateUserCommand("desktop_user-1", "مستخدم عربي", null, "Secure-Desktop-2026!"),
                new AdministrativeActor(bootstrap.AdministratorUserId, SecuritySqlFixture.Client), Cancellation).ConfigureAwait(true);
            Assert.Equal("desktop_user-1", user.UserName);
            var invalidUser = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync($"UPDATE [security].[Users] SET [UserName]=N'bad!name',[NormalizedUserName]=N'BAD!NAME' WHERE [Id]={user.Id};", Cancellation)).ConfigureAwait(true);
            Assert.Equal(547, invalidUser.Number); Assert.False(context.Database.HasPendingModelChanges());
        }
    }
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;
}
