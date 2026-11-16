using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PetraERP.Domain.FinancialValues;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Tests;

public sealed class LedgerFinancialPrecisionSqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ForwardMigrationAlignsStoragePrecisionAcceptsDomainMaximaAndPreventsDestructiveDowngrade()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            using var context = database.Context();
            var migrator = context.GetService<IMigrator>();

            // 1. Migrate to the previous migration (before precision alignment)
            const string previousMigration = "20261017102848_SecurityIdentifierCharacterSets";
            await migrator.MigrateAsync(previousMigration, Cancellation).ConfigureAwait(true);

            // Seed required parent entities for JournalLines
            var companyId = Guid.NewGuid();
            var branchId = Guid.NewGuid();
            var journalId = Guid.NewGuid();
            var debitAccountId = Guid.NewGuid();
            var creditAccountId = Guid.NewGuid();

            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO [system].[Currencies] ([Code],[NameArabic],[IsActive]) VALUES ('USD',N'دولار أمريكي',1);", Cancellation).ConfigureAwait(true);
            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO [system].[Currencies] ([Code],[NameArabic],[IsActive]) VALUES ('SYP',N'ليرة سورية',1);", Cancellation).ConfigureAwait(true);
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [system].[Companies] ([Id],[Code],[NameArabic],[BaseCurrencyCode],[IsActive]) VALUES ({companyId},N'COMP',N'شركة تجريبية','SYP',1);", Cancellation).ConfigureAwait(true);
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [system].[Branches] ([Id],[CompanyId],[Code],[NameArabic],[IsActive]) VALUES ({branchId},{companyId},N'MAIN',N'الفرع الرئيسي',1);", Cancellation).ConfigureAwait(true);
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [ledger].[Accounts] ([Id],[CompanyId],[Code],[NameArabic],[AccountType],[PostingAllowed],[CostCenterPolicy],[CurrencyMode],[IsActive]) VALUES ({debitAccountId},{companyId},N'101',N'صندوق',1,1,2,1,1);", Cancellation).ConfigureAwait(true);
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [ledger].[Accounts] ([Id],[CompanyId],[Code],[NameArabic],[AccountType],[PostingAllowed],[CostCenterPolicy],[CurrencyMode],[IsActive]) VALUES ({creditAccountId},{companyId},N'401',N'إيرادات',4,1,2,1,1);", Cancellation).ConfigureAwait(true);
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [ledger].[JournalEntries] ([Id],[CompanyId],[BranchId],[AccountingDate],[Description],[State]) VALUES ({journalId},{companyId},{branchId},'2026-01-01',N'قيد اختباري',1);", Cancellation).ConfigureAwait(true);

            // 2. Prove old schema limitation:
            // 1000000000000.0000 (13 integer digits) overflows decimal(18,6) which only allows 12 integer digits
            var overflowAmountEx = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO [ledger].[JournalLines] ([Id],[CompanyId],[JournalId],[AccountId],[TransactionCurrencyCode],[TransactionDebit],[TransactionCredit],[BaseCurrencyCode],[BaseDebit],[BaseCredit],[RateSourceCurrencyCode],[RateTargetCurrencyCode],[RateValue],[Rounding],[Description])
                   VALUES ({Guid.NewGuid()}, {companyId}, {journalId}, {debitAccountId}, 'USD', 1000000000000.0000, 0, 'SYP', 1000000000000.0000, 0, 'USD', 'SYP', 1.0, 0, N'Overflow amount');", Cancellation)).ConfigureAwait(true);
            Assert.Equal(8115, overflowAmountEx.Number);

            // 100000000.00000000 (9 integer digits) overflows decimal(18,10) which only allows 8 integer digits
            var overflowRateEx = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO [ledger].[JournalLines] ([Id],[CompanyId],[JournalId],[AccountId],[TransactionCurrencyCode],[TransactionDebit],[TransactionCredit],[BaseCurrencyCode],[BaseDebit],[BaseCredit],[RateSourceCurrencyCode],[RateTargetCurrencyCode],[RateValue],[Rounding],[Description])
                   VALUES ({Guid.NewGuid()}, {companyId}, {journalId}, {debitAccountId}, 'USD', 100.0000, 0, 'SYP', 100.0000, 0, 'USD', 'SYP', 100000000.00000000, 0, N'Overflow rate');", Cancellation)).ConfigureAwait(true);
            Assert.Equal(8115, overflowRateEx.Number);

            // 3. Test incompatible fractional data detection before upgrade:
            // Insert a row with 6 decimals into decimal(18,6)
            var fractionalLineId = Guid.NewGuid();
            await context.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO [ledger].[JournalLines] ([Id],[CompanyId],[JournalId],[AccountId],[TransactionCurrencyCode],[TransactionDebit],[TransactionCredit],[BaseCurrencyCode],[BaseDebit],[BaseCredit],[RateSourceCurrencyCode],[RateTargetCurrencyCode],[RateValue],[Rounding],[Description])
                   VALUES ({fractionalLineId}, {companyId}, {journalId}, {debitAccountId}, 'USD', 100.123456, 0, 'SYP', 100.123456, 0, 'USD', 'SYP', 1.0, 0, N'Fractional line');", Cancellation).ConfigureAwait(true);

            // Attempting to migrate forward to latest migration MUST be blocked by pre-check (error 51005)
            var upgradeBlockedEx = await Assert.ThrowsAsync<SqlException>(() => migrator.MigrateAsync(cancellationToken: Cancellation)).ConfigureAwait(true);
            Assert.Equal(51005, upgradeBlockedEx.Number);

            // Remove incompatible row
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM [ledger].[JournalLines] WHERE [Id] = {fractionalLineId};", Cancellation).ConfigureAwait(true);

            // 4. Migrate forward to latest migration (LedgerFinancialPrecisionAlignment)
            await migrator.MigrateAsync(cancellationToken: Cancellation).ConfigureAwait(true);

            // 5. Test Domain maxima round-trip and reconstitution:
            var maxMoney = FinancialPrecision.MaximumMoney;
            var maxRate = FinancialPrecision.MaximumRate;

            var line1 = new JournalLineRow
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                JournalId = journalId,
                AccountId = debitAccountId,
                TransactionCurrencyCode = "USD",
                TransactionDebit = maxMoney,
                TransactionCredit = 0m,
                BaseCurrencyCode = "SYP",
                BaseDebit = maxMoney,
                BaseCredit = 0m,
                RateSourceCurrencyCode = "USD",
                RateTargetCurrencyCode = "SYP",
                RateValue = maxRate,
                Rounding = 0,
                Description = "Max values line"
            };

            var line2 = new JournalLineRow
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                JournalId = journalId,
                AccountId = creditAccountId,
                TransactionCurrencyCode = "USD",
                TransactionDebit = 0m,
                TransactionCredit = maxMoney,
                BaseCurrencyCode = "SYP",
                BaseDebit = 0m,
                BaseCredit = maxMoney,
                RateSourceCurrencyCode = "USD",
                RateTargetCurrencyCode = "SYP",
                RateValue = maxRate,
                Rounding = 0,
                Description = "Max credit line"
            };

            context.JournalLines.AddRange(line1, line2);
            await context.SaveChangesAsync(Cancellation).ConfigureAwait(true);

            var line1Id = line1.Id;

            var retrievedLine = await context.JournalLines.AsNoTracking().SingleAsync(l => l.Id == line1Id, Cancellation).ConfigureAwait(true);
            Assert.Equal(maxMoney, retrievedLine.TransactionDebit);
            Assert.Equal(0m, retrievedLine.TransactionCredit);
            Assert.Equal(maxMoney, retrievedLine.BaseDebit);
            Assert.Equal(0m, retrievedLine.BaseCredit);
            Assert.Equal(maxRate, retrievedLine.RateValue);

            var money = Money.Create(retrievedLine.TransactionDebit, CurrencyCode.Create(retrievedLine.TransactionCurrencyCode));
            Assert.Equal(maxMoney, money.Amount);
            var rate = ExchangeRate.Create(CurrencyCode.Create(retrievedLine.RateSourceCurrencyCode), CurrencyCode.Create(retrievedLine.RateTargetCurrencyCode), retrievedLine.RateValue);
            Assert.Equal(maxRate, rate.Value);

            // 6. Test Downgrade protection:
            var downgradeBlockedEx = await Assert.ThrowsAsync<SqlException>(() => migrator.MigrateAsync(previousMigration, Cancellation)).ConfigureAwait(true);
            Assert.Equal(51007, downgradeBlockedEx.Number);

            // 7. Cleanup test lines and prove clean reversible schema migration
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM [ledger].[JournalLines] WHERE [JournalId] = {journalId};", Cancellation).ConfigureAwait(true);

            await migrator.MigrateAsync(previousMigration, Cancellation).ConfigureAwait(true);
            await migrator.MigrateAsync(cancellationToken: Cancellation).ConfigureAwait(true);

            // 8. Confirm no pending model changes
            Assert.False(context.Database.HasPendingModelChanges());
        }
    }
}
