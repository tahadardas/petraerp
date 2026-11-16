using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PetraERP.Application.Common;
using PetraERP.Application.Purchases;
using PetraERP.Domain.Purchases;
using PetraERP.Domain.Sales;

namespace PetraERP.Infrastructure.Tests;

public sealed partial class SalesPostingSqlTests
{
    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task PurchaseApprovalLegacyUpgradePreservesDocumentsAndBlocksUnsafeDowngrade()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true);
            var setup = await PurchaseSetupAsync(fixture).ConfigureAwait(true);
            var s = fixture.Scenario;
            using var context = database.Context();
            var service = Purchases(context);
            var posted = await service.CreateDraftAsync(s.CompanyA, s.BranchA1, s.Actor,
                CementPurchase(setup, PaymentMethod.Credit, 1m), Cancellation).ConfigureAwait(true);
            posted = await service.PostAsync(s.CompanyA, s.BranchA1, posted.Id, s.Actor,
                new(ConcurrencyToken.FromBytes(posted.RowVersion), "purchase-schema-upgrade"), Cancellation).ConfigureAwait(true);
            var draft = await service.CreateDraftAsync(s.CompanyA, s.BranchA1, s.Actor,
                CementPurchase(setup, PaymentMethod.Credit, 2m), Cancellation).ConfigureAwait(true);
            context.ChangeTracker.Clear();
            var journalCount = await context.JournalEntries.CountAsync(Cancellation).ConfigureAwait(true);
            var stockCount = await context.StockMovements.CountAsync(Cancellation).ConfigureAwait(true);
            var migrator = context.GetService<IMigrator>();
            const string Previous = "20261025123038_CompanyPolicies";
            await migrator.MigrateAsync(Previous, Cancellation).ConfigureAwait(true);
            Assert.Equal(Previous, (await context.Database.GetAppliedMigrationsAsync(Cancellation).ConfigureAwait(true)).Last());
            await context.Database.MigrateAsync(Cancellation).ConfigureAwait(true);
            context.ChangeTracker.Clear();
            var kept = await service.GetAsync(s.CompanyA, s.BranchA1, posted.Id, Cancellation).ConfigureAwait(true);
            Assert.Equal(posted.InvoiceNumber, kept.InvoiceNumber);
            Assert.Equal(posted.JournalEntryId, kept.JournalEntryId);
            Assert.Equal(posted.GrandTotal, kept.GrandTotal);
            Assert.Equal(posted.RowVersion, kept.RowVersion);
            var keptDraft = await service.GetAsync(s.CompanyA, s.BranchA1, draft.Id, Cancellation).ConfigureAwait(true);
            Assert.Equal(PurchaseInvoiceStatus.Draft, keptDraft.Status);
            Assert.Equal(draft.RowVersion, keptDraft.RowVersion);
            var invalid = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [purchases].[Invoices] SET [Status]=2 WHERE [Id]={draft.Id}", Cancellation)).ConfigureAwait(true);
            Assert.Equal(547, invalid.Number);
            Assert.Contains("CK_PurchaseInvoices_Posted", invalid.Message, StringComparison.Ordinal);
            var approved = await service.ApproveAsync(s.CompanyA, s.BranchA1, draft.Id, s.Actor,
                new ApprovePurchaseInvoiceCommand(ConcurrencyToken.FromBytes(draft.RowVersion)), Cancellation).ConfigureAwait(true);
            Assert.Equal(PurchaseInvoiceStatus.Approved, approved.Status);
            Assert.Null(approved.InvoiceNumber);
            Assert.Null(approved.JournalEntryId);
            var blocked = await Assert.ThrowsAsync<SqlException>(() => migrator.MigrateAsync(Previous, Cancellation)).ConfigureAwait(true);
            Assert.Equal(51042, blocked.Number);
            // The rejected downgrade has already rolled back the newer, independent migrations; bring the schema back to the model before reading through it again.
            Assert.DoesNotContain("20261029085155_CoreCentralInterCompanyBranches", await context.Database.GetAppliedMigrationsAsync(Cancellation).ConfigureAwait(true));
            await context.Database.MigrateAsync(Cancellation).ConfigureAwait(true);
            context.ChangeTracker.Clear();
            var after = await service.GetAsync(s.CompanyA, s.BranchA1, draft.Id, Cancellation).ConfigureAwait(true);
            Assert.Equal(PurchaseInvoiceStatus.Approved, after.Status);
            Assert.Equal(approved.RowVersion, after.RowVersion);
            Assert.Equal(journalCount, await context.JournalEntries.CountAsync(Cancellation).ConfigureAwait(true));
            Assert.Equal(stockCount, await context.StockMovements.CountAsync(Cancellation).ConfigureAwait(true));
            // A later data-only permission repair can roll back first. The approval schema
            // migration itself must remain applied when its data-preservation guard rejects Down.
            Assert.Contains("20261025210100_PurchaseApprovalStates", await context.Database.GetAppliedMigrationsAsync(Cancellation).ConfigureAwait(true));
        }
    }
}
