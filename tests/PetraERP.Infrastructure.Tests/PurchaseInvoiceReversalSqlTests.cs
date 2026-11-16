using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Patterns;
using PetraERP.Application.Purchases;
using PetraERP.Application.Treasury;
using PetraERP.Domain.Inventory;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Domain.Patterns;
using PetraERP.Domain.Purchases;
using PetraERP.Domain.Sales;
using PetraERP.Domain.Treasury;
using PetraERP.Infrastructure.Patterns;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Purchases;
using PetraERP.Infrastructure.Treasury;

namespace PetraERP.Infrastructure.Tests;

/// <summary>
/// Reversing a posted purchase invoice. The posted document is never edited: a linked reversal document carries the same amounts, the
/// journal is mirrored line for line, and the stock goes back out at the cost it arrived at. These are the guarantees the immutability
/// rule in the engineering contract depends on, so they are asserted against a real SQL Server.
/// </summary>
public sealed partial class SalesPostingSqlTests
{
    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ReversedPurchaseRestoresStockAtOriginalCostMirrorsTheJournalAndLinksBothDocuments()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true); var s = fixture.Scenario;
            var p = await PurchaseSetupAsync(fixture).ConfigureAwait(true);
            using var context = database.Context();
            var service = Purchases(context);

            var draft = await service.CreateDraftAsync(s.CompanyA, s.BranchA1, s.Actor, CementPurchase(p, PaymentMethod.Credit, 2.5m), Cancellation).ConfigureAwait(true);
            // A draft has nothing to reverse; only a posted document is immutable.
            await AssertRuleAsync("purchases.invoice_not_posted", () => service.ReverseInvoiceAsync(s.CompanyA, s.BranchA1, draft.Id, s.Actor,
                Reverse("purchase-draft-reverse", "طلب كان بالخطأ", draft), Cancellation)).ConfigureAwait(true);

            var posted = await service.PostAsync(s.CompanyA, s.BranchA1, draft.Id, s.Actor,
                new PostPurchaseInvoiceCommand(ConcurrencyToken.FromBytes(draft.RowVersion), "purchase-reverse-post"), Cancellation).ConfigureAwait(true);
            var receipt = await context.StockMovements.AsNoTracking().SingleAsync(m => m.DocumentId == posted.Id, Cancellation).ConfigureAwait(true);

            var command = Reverse("purchase-reverse-1", "البضاعة رفضها المفتش والطلب أُلغي", posted);
            var reversed = await service.ReverseInvoiceAsync(s.CompanyA, s.BranchA1, posted.Id, s.Actor, command, Cancellation).ConfigureAwait(true);

            // The call answers with the original, now marked reversed and pointing at the document that undoes it.
            Assert.Equal(posted.Id, reversed.Id);
            Assert.Equal(PurchaseInvoiceStatus.Reversed, reversed.Status);
            Assert.NotNull(reversed.ReversedByInvoiceId);
            var reversal = await service.GetAsync(s.CompanyA, s.BranchA1, reversed.ReversedByInvoiceId!.Value, Cancellation).ConfigureAwait(true);
            Assert.Equal(posted.Id, reversal.ReversesInvoiceId);
            Assert.Null(reversal.ReturnOfInvoiceId);
            Assert.Equal(PurchaseInvoiceStatus.Posted, reversal.Status);
            Assert.Equal(posted.GrandTotal, reversal.GrandTotal);
            Assert.Equal("PI-0002", reversal.InvoiceNumber);
            Assert.NotEqual(posted.JournalEntryId, reversal.JournalEntryId);

            // Stock leaves again at the cost it arrived at, and the movement names the movement it undoes.
            var restored = await context.StockMovements.AsNoTracking().SingleAsync(m => m.DocumentId == reversal.Id, Cancellation).ConfigureAwait(true);
            Assert.Equal((int)StockMovementKind.PurchaseReturn, restored.MovementKind);
            Assert.Equal(-receipt.BaseQuantity, restored.BaseQuantity);
            Assert.Equal(receipt.TotalCost, restored.TotalCost);
            Assert.Equal(receipt.UnitCost, restored.UnitCost);
            Assert.Equal(receipt.Id, restored.ReversesMovementId);
            Assert.Equal(0m, await StockQuantityAsync(context, s.CompanyA, p.Warehouse, p.Cement).ConfigureAwait(true));

            // The journal mirrors the original line for line and balances on its own.
            var originalLines = await context.JournalLines.AsNoTracking().Where(l => l.JournalId == posted.JournalEntryId).ToListAsync(Cancellation).ConfigureAwait(true);
            var reversalLines = await context.JournalLines.AsNoTracking().Where(l => l.JournalId == reversal.JournalEntryId).ToListAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(originalLines.Sum(l => l.BaseDebit), reversalLines.Sum(l => l.BaseCredit));
            Assert.Equal(originalLines.Sum(l => l.BaseCredit), reversalLines.Sum(l => l.BaseDebit));
            Assert.Equal(reversalLines.Sum(l => l.BaseDebit), reversalLines.Sum(l => l.BaseCredit));
            Assert.Equal((byte)JournalEntryState.Posted, (await context.JournalEntries.AsNoTracking()
                .SingleAsync(j => j.Id == reversal.JournalEntryId, Cancellation).ConfigureAwait(true)).State);
            // The original journal is left posted and records the entry that reversed it.
            Assert.Equal(reversal.JournalEntryId, (await context.JournalEntries.AsNoTracking()
                .SingleAsync(j => j.Id == posted.JournalEntryId, Cancellation).ConfigureAwait(true)).ReversedByEntryId);

            // Replay of the same request key answers with the same pair and posts nothing a second time.
            var replay = await service.ReverseInvoiceAsync(s.CompanyA, s.BranchA1, posted.Id, s.Actor, command, Cancellation).ConfigureAwait(true);
            Assert.Equal(reversed.ReversedByInvoiceId, replay.ReversedByInvoiceId);
            Assert.Equal(1, await context.StockMovements.CountAsync(m => m.DocumentId == reversal.Id, Cancellation).ConfigureAwait(true));
            Assert.Equal(2, await context.JournalEntries.CountAsync(Cancellation).ConfigureAwait(true));

            // A reversal is not itself reversible, and an invoice already reversed cannot be reversed again under another key.
            await AssertRuleAsync("purchases.reversal_document_not_reversible", () => service.ReverseInvoiceAsync(s.CompanyA, s.BranchA1, reversal.Id, s.Actor,
                Reverse("purchase-reverse-2", "عكس العكس", reversal), Cancellation)).ConfigureAwait(true);
            await AssertRuleAsync("purchases.invoice_already_reversed", () => service.ReverseInvoiceAsync(s.CompanyA, s.BranchA1, posted.Id, s.Actor,
                Reverse("purchase-reverse-3", "عكس ثانٍ", posted), Cancellation)).ConfigureAwait(true);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task PurchaseReversalRefusesADocumentWithReturnsAndAnInvoiceWhoseSupplierWasPaid()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true); var s = fixture.Scenario;
            var p = await PurchaseSetupAsync(fixture).ConfigureAwait(true);
            using var context = database.Context();
            var service = Purchases(context);

            var draft = await service.CreateDraftAsync(s.CompanyA, s.BranchA1, s.Actor, CementPurchase(p, PaymentMethod.Credit, 2.5m), Cancellation).ConfigureAwait(true);
            var posted = await service.PostAsync(s.CompanyA, s.BranchA1, draft.Id, s.Actor,
                new PostPurchaseInvoiceCommand(ConcurrencyToken.FromBytes(draft.RowVersion), "purchase-guard-post"), Cancellation).ConfigureAwait(true);
            var afterReturn = await service.ReturnAsync(s.CompanyA, s.BranchA1, posted.Id, s.Actor,
                new CreatePurchaseReturnCommand("purchase-guard-return", "تضرر جزئي", [new(posted.Lines[0].Id, 0.5m)]), Cancellation).ConfigureAwait(true);
            // A partial return has already moved part of the payable and the stock back; a full reversal would move it a second time.
            await AssertRuleAsync("purchases.invoice_has_returns", () => service.ReverseInvoiceAsync(s.CompanyA, s.BranchA1, posted.Id, s.Actor,
                Reverse("purchase-guard-reverse", "عكس كامل بعد مرتجع", posted), Cancellation)).ConfigureAwait(true);

            var second = await service.CreateDraftAsync(s.CompanyA, s.BranchA1, s.Actor, CementPurchase(p, PaymentMethod.Credit, 1m), Cancellation).ConfigureAwait(true);
            var settled = await service.PostAsync(s.CompanyA, s.BranchA1, second.Id, s.Actor,
                new PostPurchaseInvoiceCommand(ConcurrencyToken.FromBytes(second.RowVersion), "purchase-settled-post"), Cancellation).ConfigureAwait(true);
            await PaySupplierAsync(context, s, p, settled, Cancellation).ConfigureAwait(true);
            await AssertRuleAsync("purchases.invoice_has_settlements", () => service.ReverseInvoiceAsync(s.CompanyA, s.BranchA1, settled.Id, s.Actor,
                Reverse("purchase-settled-reverse", "سُددت ثم أُلغيت", settled), Cancellation)).ConfigureAwait(true);
        }
    }

    private static ReversePurchaseInvoiceCommand Reverse(string key, string reason, PurchaseDetailResult document) =>
        new(ConcurrencyToken.FromBytes(document.RowVersion), key, reason);

    private static async Task<decimal> StockQuantityAsync(PetraDbContext context, Guid companyId, Guid warehouseId, Guid itemId) =>
        await context.StockMovements.AsNoTracking().Where(m => m.CompanyId == companyId && m.WarehouseId == warehouseId && m.ItemId == itemId)
            .SumAsync(m => m.BaseQuantity).ConfigureAwait(true);

    /// <summary>Pays the supplier for the invoice through the ordinary treasury path, so the open item really is settled.</summary>
    private static async Task PaySupplierAsync(PetraDbContext context, LedgerScenario s, PurchaseSetup p, PurchaseDetailResult invoice,
        CancellationToken cancellationToken)
    {
        foreach (var permission in new[] { "Treasury.Vouchers.View", "Treasury.Vouchers.Manage", "Treasury.Vouchers.Approve", "Treasury.Vouchers.Post", "Treasury.Vouchers.Allocate" })
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [security].[RolePermissions]([RoleId],[PermissionCode]) SELECT [RoleId],{permission} FROM [security].[UserRoles] WHERE [UserId]={s.Actor}",
                cancellationToken).ConfigureAwait(false);
        var patterns = new SqlPatternService(context);
        var scope = PatternScope.Create(s.CompanyA, s.BranchA1, s.Actor);
        var created = await patterns.CreatePatternAsync(scope, new CreateDocumentPatternCommand
        {
            Code = "PV-RV", NameArabic = "سند دفع", ShortCode = "PVR", OperationKind = BusinessOperationKind.CashPayment, BranchIds = [s.BranchA1],
            InitialConfiguration = new PatternConfiguration
            {
                AccountMappings = [new() { Role = "Payable", AccountId = p.Payable }, new() { Role = "Cash", AccountId = p.Cash }],
                NumberingPolicy = new() { Prefix = "PVR-", MinimumDigits = 4 }
            }
        }, cancellationToken).ConfigureAwait(false);
        var published = await patterns.PublishRevisionAsync(scope, created.Id, new PublishPatternRevisionCommand
        {
            ExpectedVersion = created.Version, RevisionId = created.ActiveRevision!.Id, Reason = "نشر"
        }, cancellationToken).ConfigureAwait(false);

        var treasury = new SqlTreasuryVoucherService(context, patterns, TimeProvider.System);
        var draft = await treasury.CreateDraftAsync(s.CompanyA, s.BranchA1, s.Actor, new SaveVoucherCommand(
            VoucherKind.Payment, published.ActiveRevision!.Id, p.Supplier, SettlementMethod.Cash, invoice.InvoiceDateUtc, "SYP", 0.01m,
            invoice.GrandTotal, "تسديد فاتورة مشتريات", [], [])
        {
            InvoiceAllocations = [new InvoiceSettlementCommand(invoice.Id, invoice.GrandTotal, invoice.GrandTotal, 1m)]
        }, cancellationToken).ConfigureAwait(false);
        var approved = await treasury.ApproveAsync(s.CompanyA, s.BranchA1, draft.Id, s.Actor,
            new VoucherActionCommand(ConcurrencyToken.FromBytes(draft.RowVersion), "اعتماد الدفع"), cancellationToken).ConfigureAwait(false);
        await treasury.PostAsync(s.CompanyA, s.BranchA1, draft.Id, s.Actor,
            new PostVoucherCommand(ConcurrencyToken.FromBytes(approved.RowVersion), "purchase-reversal-settle"), cancellationToken).ConfigureAwait(false);
    }
}
