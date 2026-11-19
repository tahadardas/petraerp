using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Text.RegularExpressions;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Tests;

public sealed class OrganizationSqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;
    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task MigrationUpDownReapplyAndArabicRoundTripUseARealOwnedDatabase()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            using var context = database.Context();
            await ApplyGeneratedUpScript(context, Cancellation).ConfigureAwait(true);
            await ApplyGeneratedUpScript(context, Cancellation).ConfigureAwait(true);
            var seed = await OrganizationSqlSeed.CreateAsync(context).ConfigureAwait(true);
            context.ChangeTracker.Clear();
            var company = await context.Companies.SingleAsync(row => row.Id == seed.FirstCompanyId, Cancellation).ConfigureAwait(true);
            Assert.Equal("شركة البتراء 🏛", company.NameArabic);
            Assert.Equal("البتراء", company.Code);
            Assert.Equal("USD", company.BaseCurrencyCode);
            Assert.Equal(8, company.RowVersion.Length);
            Assert.Equal(TimeSpan.Zero, company.CreatedAtUtc.Offset);
            Assert.Equal("فرع المَزّة", (await context.Branches.SingleAsync(row => row.CompanyId == seed.FirstCompanyId, Cancellation).ConfigureAwait(true)).NameArabic);
            Assert.Equal(3, await context.Currencies.CountAsync(Cancellation).ConfigureAwait(true));
            Assert.Equal(2, await context.CompanyAdditionalCurrencies.CountAsync(Cancellation).ConfigureAwait(true));
            await context.Database.MigrateAsync(Cancellation).ConfigureAwait(true);
            // Phase 2 policies must also apply through the reviewed, idempotent SQL script.
            var applied = (await context.Database.GetAppliedMigrationsAsync(Cancellation).ConfigureAwait(true)).ToArray();
            Assert.Equal(52, applied.Length);
            Assert.Equal("20261116112328_LedgerCoveringIndexes", applied[^1]);
            context.ChangeTracker.Clear();
            await context.GetService<IMigrator>().MigrateAsync(Migration.InitialDatabase, Cancellation).ConfigureAwait(true);
            Assert.Empty(await context.Database.GetAppliedMigrationsAsync(Cancellation).ConfigureAwait(true));
            await context.Database.MigrateAsync(Cancellation).ConfigureAwait(true);
            Assert.Empty(await context.Companies.ToListAsync(Cancellation).ConfigureAwait(true));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task DatabaseRejectsOrphansDuplicatesMalformedCodesAndRestrictedDeletes()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            using var context = database.Context();
            var seed = await OrganizationSqlSeed.CreateAsync(context).ConfigureAwait(true);
            await SqlFailure(() => context.Database.ExecuteSqlInterpolatedAsync($"INSERT [system].[Branches] ([Id],[CompanyId],[Code],[NameArabic],[IsActive]) VALUES ({Guid.NewGuid()},{Guid.NewGuid()},N'ORPHAN',N'فرع',1);"), 547).ConfigureAwait(true);
            await SqlFailure(() => context.Database.ExecuteSqlInterpolatedAsync($"INSERT [system].[Branches] ([Id],[CompanyId],[Code],[NameArabic],[IsActive]) VALUES ({Guid.NewGuid()},{seed.FirstCompanyId},N'BR',N'مكرر',1);"), 2601, 2627).ConfigureAwait(true);
            await SqlFailure(() => context.Database.ExecuteSqlInterpolatedAsync($"INSERT [system].[Companies] ([Id],[Code],[NameArabic],[BaseCurrencyCode],[IsActive]) VALUES ({Guid.NewGuid()},N'البتراء',N'مكرر','USD',1);"), 2601, 2627).ConfigureAwait(true);
            await SqlFailure(() => context.Database.ExecuteSqlRawAsync("INSERT [system].[Currencies] ([Code],[NameArabic],[IsActive]) VALUES ('usd',N'عملة غير صحيحة',1);"), 547).ConfigureAwait(true);
            await SqlFailure(() => context.Database.ExecuteSqlRawAsync("INSERT [system].[Currencies] ([Code],[NameArabic],[IsActive]) VALUES ('US ',N'عملة ناقصة',1);"), 547).ConfigureAwait(true);
            await SqlFailure(() => context.Database.ExecuteSqlInterpolatedAsync($"INSERT [system].[Companies] ([Id],[Code],[NameArabic],[BaseCurrencyCode],[IsActive]) VALUES ({Guid.Empty},N'EMPTY',N'شركة','USD',1);"), 547).ConfigureAwait(true);
            await SqlFailure(() => context.Database.ExecuteSqlInterpolatedAsync($"INSERT [system].[Companies] ([Id],[Code],[NameArabic],[BaseCurrencyCode],[IsActive]) VALUES ({Guid.NewGuid()},{"A\u00a0"},N'شركة','USD',1);"), 547).ConfigureAwait(true);
            await SqlFailure(() => context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [system].[Companies] WHERE [Id]={seed.FirstCompanyId};"), 547).ConfigureAwait(true);
            await context.Database.ExecuteSqlInterpolatedAsync($"INSERT [system].[Companies] ([Id],[Code],[NameArabic],[BaseCurrencyCode],[IsActive]) VALUES ({Guid.NewGuid()},N'Case',N'شركة','USD',1),({Guid.NewGuid()},N'case',N'شركة','USD',1);", Cancellation).ConfigureAwait(true);
            Assert.Equal(4, await context.Companies.CountAsync(Cancellation).ConfigureAwait(true));
            Assert.Equal(2, await context.Branches.CountAsync(Cancellation).ConfigureAwait(true));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task BaseCurrencyGuardsRejectWholeBatchesAndChangesFromEitherSide()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            using var context = database.Context();
            var seed = await OrganizationSqlSeed.CreateAsync(context).ConfigureAwait(true);
            await SqlFailure(() => context.Database.ExecuteSqlInterpolatedAsync($"INSERT [system].[CompanyAdditionalCurrencies] ([CompanyId],[CurrencyCode]) VALUES ({seed.FirstCompanyId},'USD'),({seed.SecondCompanyId},'SYP');"), 51001).ConfigureAwait(true);
            Assert.Equal(2, await context.CompanyAdditionalCurrencies.CountAsync(Cancellation).ConfigureAwait(true));
            await SqlFailure(() => context.Database.ExecuteSqlInterpolatedAsync($"UPDATE [system].[Companies] SET [BaseCurrencyCode]='SYP' WHERE [Id]={seed.FirstCompanyId};"), 51001).ConfigureAwait(true);
            await SqlFailure(() => context.Database.ExecuteSqlInterpolatedAsync($"UPDATE [system].[Companies] SET [BaseCurrencyCode]=CASE WHEN [Id]={seed.FirstCompanyId} THEN 'SYP' ELSE 'EUR' END WHERE [Id] IN ({seed.FirstCompanyId},{seed.SecondCompanyId});"), 51001).ConfigureAwait(true);
            Assert.All(await context.Companies.AsNoTracking().ToListAsync(Cancellation).ConfigureAwait(true), row => Assert.Equal("USD", row.BaseCurrencyCode));
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE [system].[Companies] SET [BaseCurrencyCode]='EUR' WHERE [Id]={seed.FirstCompanyId};", Cancellation).ConfigureAwait(true);
            Assert.Equal("EUR", (await context.Companies.AsNoTracking().SingleAsync(row => row.Id == seed.FirstCompanyId, Cancellation).ConfigureAwait(true)).BaseCurrencyCode);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task StaleTrackedUpdateFailsWithoutOverwritingTheFirstWritersData()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            using var initial = database.Context();
            var seed = await OrganizationSqlSeed.CreateAsync(initial).ConfigureAwait(true);
            using var first = database.Context();
            using var second = database.Context();
            var firstRow = await first.Companies.SingleAsync(row => row.Id == seed.FirstCompanyId, Cancellation).ConfigureAwait(true);
            var secondRow = await second.Companies.SingleAsync(row => row.Id == seed.FirstCompanyId, Cancellation).ConfigureAwait(true);
            var previousVersion = firstRow.RowVersion.ToArray();
            var created = firstRow.CreatedAtUtc;
            firstRow.NameArabic = "تعديل المستخدم الأول";
            await first.SaveChangesAsync(Cancellation).ConfigureAwait(true);
            Assert.NotEqual(previousVersion, firstRow.RowVersion);
            Assert.Equal(created, firstRow.CreatedAtUtc);
            secondRow.NameArabic = "تعديل متأخر";
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(Cancellation)).ConfigureAwait(true);
            using var read = database.Context();
            var saved = await read.Companies.SingleAsync(row => row.Id == seed.FirstCompanyId, Cancellation).ConfigureAwait(true);
            Assert.Equal("تعديل المستخدم الأول", saved.NameArabic);
            Assert.Equal(TimeSpan.Zero, saved.UpdatedAtUtc.Offset);
            saved.CreatedAtUtc = created.AddDays(1);
            await Assert.ThrowsAsync<InvalidOperationException>(() => read.SaveChangesAsync(Cancellation)).ConfigureAwait(true);
        }
    }

    private static async Task SqlFailure(Func<Task> action, params int[] expectedNumbers)
    {
        var exception = await Assert.ThrowsAsync<SqlException>(action).ConfigureAwait(false);
        Assert.Contains(exception.Number, expectedNumbers);
    }

    private static async Task ApplyGeneratedUpScript(PetraDbContext context, CancellationToken cancellationToken)
    {
        var script = context.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
        await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Only split this provider-generated migration script; this is not an input SQL parser.
            foreach (var batch in Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)))
            {
                if (!string.IsNullOrWhiteSpace(batch))
                {
                    await context.Database.ExecuteSqlRawAsync(batch, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            await context.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }
}
