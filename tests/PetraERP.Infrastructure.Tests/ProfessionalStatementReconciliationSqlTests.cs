using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.Reports;
using PetraERP.Application.Reports.Professional;
using PetraERP.Application.Sales;
using PetraERP.Application.Security;
using PetraERP.Application.Treasury;
using PetraERP.Domain.Primitives;
using PetraERP.Domain.Inventory;
using PetraERP.Domain.Sales;
using PetraERP.Infrastructure.Inventory;
using PetraERP.Infrastructure.Patterns;
using PetraERP.Infrastructure.Parties.OpenItems;
using PetraERP.Infrastructure.Reports;
using PetraERP.Infrastructure.Reports.Professional;
using PetraERP.Infrastructure.Sales;
using PetraERP.Infrastructure.Security;

namespace PetraERP.Infrastructure.Tests;

// Real posted sales chain: invoice, return and receipt voucher, reconciled against the receivable ledger,
// then a brought-forward prior-year open item enters the same statement without disturbing the period movements.
public sealed partial class SalesPostingSqlTests
{
    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task CustomerStatementReconcilesInvoiceReturnAndReceiptToExactReceivable()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true); var s = fixture.Scenario;
            using var context = database.Context();
            // VoucherSetupAsync grants Treasury.Vouchers.* for the same role; only the sales view must be added here.
            await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO [security].[RolePermissions]([RoleId],[PermissionCode]) SELECT [RoleId],{PermissionCatalog.SalesInvoicesView} FROM [security].[UserRoles] WHERE [UserId]={s.Actor}", Cancellation).ConfigureAwait(true);
            await LedgerScenario.AddStockMovementAsync(context, new StockMovementRow
            {
                Id = Guid.NewGuid(), CompanyId = s.CompanyA, BranchId = s.BranchA1, WarehouseId = fixture.WarehouseId, ItemId = fixture.ItemId, UnitId = fixture.UnitId,
                MovementKind = (int)StockMovementKind.AdjustmentIncrease, Quantity = 90, BaseQuantity = 90, UnitCost = 2, TotalCost = 180,
                MovementDateUtc = new DateTimeOffset(2026, 10, 14, 0, 0, 0, TimeSpan.Zero), DocumentType = "Adjustment", DocumentId = Guid.NewGuid(),
                CreatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = s.Actor,
            }, Cancellation).ConfigureAwait(true);
            // ReturnInvoiceAsync uses its injected clock as the return date; keep it inside the report window
            // independently of the day the test suite happens to run.
            var sales = new SqlSalesInvoiceService(context, new SqlPatternService(context),
                new StatementClock(new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero)));
            var draft = await sales.GetInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, Cancellation).ConfigureAwait(true);
            await sales.UpdateInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, new UpdateSalesInvoiceCommand(draft.CustomerId, draft.WarehouseId,
                new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.Zero), CurrencyCode: "USD", ExchangeRate: 1m, Notes: "فاتورة كشف 2000",
                Lines: [new(fixture.ItemId, fixture.UnitId, 20m, 100m)]) { ExpectedVersion = ConcurrencyToken.FromBytes(draft.RowVersion) }, Cancellation).ConfigureAwait(true);
            var invoice = await sales.PostInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, s.Actor,
                new PostSalesInvoiceCommand("statement-invoice"), Cancellation).ConfigureAwait(true);
            Assert.Equal(2000m, invoice.GrandTotal);
            var returned = await sales.ReturnInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, s.Actor,
                new CreateSalesReturnCommand("statement-return", "مرتجع كشف", [new(Assert.Single(invoice.Lines).Id, 1m)]), Cancellation).ConfigureAwait(true);
            Assert.Equal(100m, returned.GrandTotal);
            var setup = await VoucherSetupAsync(fixture).ConfigureAwait(true);
            var treasury = Treasury(context);
            var voucherDraft = await treasury.CreateDraftAsync(s.CompanyA, s.BranchA1, s.Actor,
                Receipt(setup, 750m, (fixture.InvoiceId, 750m)) with { CurrencyCode = "USD", ExchangeRate = 1m }, Cancellation).ConfigureAwait(true);
            await treasury.PostAsync(s.CompanyA, s.BranchA1, voucherDraft.Id, s.Actor,
                new PostVoucherCommand(ConcurrencyToken.FromBytes(voucherDraft.RowVersion), "statement-receipt"), Cancellation).ConfigureAwait(true);
            var reports = new SqlProfessionalReportService(context, new SqlAccessEvaluator(context), new SqlCommercialReportService(context));
            var year = await context.FiscalYears.Where(y => y.CompanyId == s.CompanyA).Select(y => y.Id).SingleAsync(Cancellation).ConfigureAwait(true);
            var scope = LedgerReportScope.Create(s.CompanyA, s.BranchA1, s.Actor, false);
            var request = new ProfessionalReportRequest
            {
                Kind = ProfessionalReportKind.CustomerStatement, FiscalYearId = year, PartyId = setup.Customer,
                From = new DateOnly(2026, 10, 1), To = new DateOnly(2026, 10, 31), CurrencyCode = "USD", CurrencyBasis = ProfessionalCurrencyBasis.Transaction,
                Sources = [ProfessionalReportSource.SalesInvoice, ProfessionalReportSource.SalesReturn, ProfessionalReportSource.ReceiptVoucher, ProfessionalReportSource.Settlement],
            };
            var report = await reports.ExecuteAsync(scope, request, Cancellation).ConfigureAwait(true);
            Assert.Equal(3, report.Rows.Count);
            Assert.Equal(0m, report.Summary.ActualOpening);
            Assert.Equal(2000m, report.Summary.ActualPeriodDebit);
            Assert.Equal(850m, report.Summary.ActualPeriodCredit);
            Assert.Equal(1150m, report.Summary.ActualClosing);
            Assert.Equal(1150m, report.Rows[^1].ActualBalance);
            Assert.Equal([2000m, 1250m, 1150m], report.Rows.Select(r => r.ActualBalance));
            Assert.Equal(ProfessionalReportSource.SalesInvoice, report.Rows[0].Source);
            Assert.Contains(report.Rows, r => r.Source == ProfessionalReportSource.SalesReturn && r.Credit == 100m);
            Assert.Contains(report.Rows, r => r.Source == ProfessionalReportSource.ReceiptVoucher && r.Credit == 750m);
            var ledger = await reports.ExecuteAsync(scope, new ProfessionalReportRequest
            {
                Kind = ProfessionalReportKind.AccountLedger, FiscalYearId = year, AccountId = fixture.ReceivableId,
                From = request.From, To = request.To, CurrencyCode = "USD", CurrencyBasis = ProfessionalCurrencyBasis.Transaction,
                Sources = [ProfessionalReportSource.SalesInvoice, ProfessionalReportSource.SalesReturn, ProfessionalReportSource.ReceiptVoucher, ProfessionalReportSource.Settlement],
            }, Cancellation).ConfigureAwait(true);
            Assert.Equal(1150m, ledger.Summary.ActualClosing);
            Assert.Equal(report.Summary.ActualClosing, ledger.Summary.ActualClosing);

            // Brought-forward prior-year open item: opening balance only until the statement window covers its origin date.
            var now = DateTimeOffset.UtcNow;
            context.OpenItems.Add(new OpenItemRow
            {
                Id = Guid.NewGuid(), CompanyId = s.CompanyA, BranchId = s.BranchA1, PartyId = setup.Customer, PartyType = (byte)OpenItemPartyType.Customer,
                OriginDocumentNumber = "PY-2025-1", OriginDocumentDateUtc = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero),
                CurrencyCode = "USD", OriginalAmount = 500m, SettledAmount = 0m, OutstandingAmount = 500m, ExchangeRate = 1m,
                OriginalBaseCarryingAmount = 500m, CurrentBaseCarryingAmount = 500m, OriginType = (byte)OpenItemOriginType.PriorYearInvoiceRollover,
                AccountId = fixture.ReceivableId, State = (byte)OpenItemState.Open, CreatedAtUtc = now, UpdatedAtUtc = now,
            });
            await context.SaveChangesAsync(Cancellation).ConfigureAwait(true);
            var withOpening = await reports.ExecuteAsync(scope, request, Cancellation).ConfigureAwait(true);
            // The brought-forward item predates the fiscal year window, so it enters as opening balance, not as a period row.
            Assert.Equal(3, withOpening.Rows.Count);
            Assert.Equal(500m, withOpening.Summary.ActualOpening);
            Assert.Equal(1650m, withOpening.Summary.ActualClosing);
            Assert.Equal(1650m, withOpening.Rows[^1].ActualBalance);
            Assert.All(withOpening.Rows, r => Assert.NotEqual(ProfessionalReportSource.Settlement, r.Source));
        }
    }

    private sealed class StatementClock(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
