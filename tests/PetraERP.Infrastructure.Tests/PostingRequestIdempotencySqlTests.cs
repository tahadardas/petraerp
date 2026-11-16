using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Tests;

public sealed class PostingRequestIdempotencySqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ForwardMigrationAlignsIdempotencyScopeAllowsCrossScopeSameKeyAndBlocksDestructiveDowngrade()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            using var context = database.Context();
            var migrator = context.GetService<IMigrator>();

            // 1. Migrate to previous migration (before idempotency composite key)
            const string previousMigration = "20261017110251_LedgerFinancialPrecisionAlignment";
            await migrator.MigrateAsync(previousMigration, Cancellation).ConfigureAwait(true);

            var company1 = Guid.NewGuid();
            var company2 = Guid.NewGuid();
            var branch1 = Guid.NewGuid();
            var branch2 = Guid.NewGuid();
            var actor = Guid.NewGuid();
            var sharedKey = Guid.NewGuid();
            var journal1 = Guid.NewGuid();
            var journal2 = Guid.NewGuid();
            var fp1 = new string('A', 64);
            var fp2 = new string('B', 64);

            // Insert valid row under company 1
            await context.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO [ledger].[PostingRequests] (
                    [IdempotencyKey], [CompanyId], [BranchId], [ActorUserId], [Operation],
                    [JournalId], [ExpectedRevision], [ExpectedVersionBase64],
                    [Reason], [FingerprintHex]
                ) VALUES (
                    {sharedKey}, {company1}, {branch1}, {actor}, 1,
                    {journal1}, 1, 'AAAAAAAAAAAA',
                    N'قيد ترحيل شركة 1', {fp1}
                )", Cancellation).ConfigureAwait(true);

            // Under old schema, inserting same sharedKey with company 2 fails with PK violation (2627)
            var duplicateKeyEx = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO [ledger].[PostingRequests] (
                    [IdempotencyKey], [CompanyId], [BranchId], [ActorUserId], [Operation],
                    [JournalId], [ExpectedRevision], [ExpectedVersionBase64],
                    [Reason], [FingerprintHex]
                ) VALUES (
                    {sharedKey}, {company2}, {branch2}, {actor}, 1,
                    {journal2}, 1, 'BBBBBBBBBBBB',
                    N'قيد ترحيل شركة 2', {fp2}
                )", Cancellation)).ConfigureAwait(true);
            Assert.True(duplicateKeyEx.Number is 2601 or 2627);

            // 2. Migrate up to latest migration
            await migrator.MigrateAsync(cancellationToken: Cancellation).ConfigureAwait(true);

            // Now, inserting same sharedKey with company 2 succeeds under composite PK!
            await context.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO [ledger].[PostingRequests] (
                    [IdempotencyKey], [CompanyId], [BranchId], [Operation], [ActorUserId],
                    [JournalId], [ExpectedRevision], [ExpectedVersionBase64],
                    [Reason], [FingerprintHex]
                ) VALUES (
                    {sharedKey}, {company2}, {branch2}, 1, {actor},
                    {journal2}, 1, 'BBBBBBBBBBBB',
                    N'قيد ترحيل شركة 2', {fp2}
                )", Cancellation).ConfigureAwait(true);

            // Also inserting same sharedKey for Operation 2 (reversal) succeeds!
            var originalJournal = Guid.NewGuid();
            await context.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO [ledger].[PostingRequests] (
                    [IdempotencyKey], [CompanyId], [BranchId], [Operation], [ActorUserId],
                    [JournalId], [ExpectedRevision], [ExpectedVersionBase64],
                    [OriginalJournalId], [OriginalExpectedRevision], [OriginalVersionBase64],
                    [Reason], [FingerprintHex]
                ) VALUES (
                    {sharedKey}, {company1}, {branch1}, 2, {actor},
                    {journal1}, 1, 'AAAAAAAAAAAA',
                    {originalJournal}, 1, 'CCCCCCCCCCCC',
                    N'عكس قيد شركة 1', {fp1}
                )", Cancellation).ConfigureAwait(true);

            var count = await context.PostingRequests.CountAsync(r => r.IdempotencyKey == sharedKey, Cancellation).ConfigureAwait(true);
            Assert.Equal(3, count);

            // 3. Prove downgrade pre-check: attempting downgrade with duplicate IdempotencyKey throws 51009
            var downgradeEx = await Assert.ThrowsAsync<SqlException>(() =>
                migrator.MigrateAsync(previousMigration, Cancellation)).ConfigureAwait(true);
            Assert.Equal(51009, downgradeEx.Number);
            Assert.Contains("duplicate IdempotencyKey", downgradeEx.Message);

            // 4. Remove duplicate rows and verify downgrade succeeds cleanly
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM [ledger].[PostingRequests] WHERE [IdempotencyKey] = {sharedKey} AND ([CompanyId] = {company2} OR [Operation] = 2)",
                Cancellation).ConfigureAwait(true);

            await migrator.MigrateAsync(previousMigration, Cancellation).ConfigureAwait(true);

            // 5. Re-apply up to verify idempotence
            await migrator.MigrateAsync(cancellationToken: Cancellation).ConfigureAwait(true);

            // Model snapshot is fully in sync
            Assert.False(context.Database.HasPendingModelChanges());
        }
    }
}
