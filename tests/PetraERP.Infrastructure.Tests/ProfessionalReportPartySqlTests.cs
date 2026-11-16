using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.Reports;
using PetraERP.Application.Reports.Professional;
using PetraERP.Application.Sales;
using PetraERP.Application.Treasury;
using PetraERP.Domain.Sales;
using PetraERP.Domain.Treasury;
using PetraERP.Application.Security;
using PetraERP.Infrastructure.Reports;
using PetraERP.Infrastructure.Reports.Professional;
using PetraERP.Infrastructure.Security;

namespace PetraERP.Infrastructure.Tests;

// Uses the established real sales posting fixture, including its inventory and published document pattern.
public sealed partial class SalesPostingSqlTests
{
    private static readonly decimal[] ChronologicalBalances = [50m, 350m];

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ProfessionalCustomerStatementKeepsMultilineInvoiceDetailsSeparateAndUsesRealSourceIds()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true);
            var s = fixture.Scenario;
            using var context = database.Context();
            foreach (var permission in new[] { PermissionCatalog.SalesInvoicesView, PermissionCatalog.TreasuryVouchersView })
                await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO [security].[RolePermissions]([RoleId],[PermissionCode]) SELECT [RoleId],{permission} FROM [security].[UserRoles] WHERE [UserId]={s.Actor}", Cancellation).ConfigureAwait(true);
            var sales = Service(context);
            var current = await sales.GetInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, Cancellation).ConfigureAwait(true);
            await sales.UpdateInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId,
                new UpdateSalesInvoiceCommand(current.CustomerId, current.WarehouseId, new DateTimeOffset(2026, 10, 19, 10, 0, 0, TimeSpan.Zero), CurrencyCode: "SYP", ExchangeRate: 0.01m,
                    Notes: "فاتورة أحمد [%]_", Lines: [new(fixture.ItemId, fixture.UnitId, 1m, 100m), new(fixture.ItemId, fixture.UnitId, 1m, 200m)])
                { ExpectedVersion = ConcurrencyToken.FromBytes(current.RowVersion) }, Cancellation).ConfigureAwait(true);
            var posted = await sales.PostInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, s.Actor,
                new PostSalesInvoiceCommand("professional-report-real-source"), Cancellation).ConfigureAwait(true);
            var service = new SqlProfessionalReportService(context, new SqlAccessEvaluator(context), new SqlCommercialReportService(context));
            var year = await context.FiscalYears.Where(y => y.CompanyId == s.CompanyA).Select(y => y.Id).SingleAsync(Cancellation).ConfigureAwait(true);
            var request = new ProfessionalReportRequest
            {
                Kind = ProfessionalReportKind.CustomerStatement, FiscalYearId = year, PartyId = current.CustomerId,
                From = new DateOnly(2026, 10, 1), To = new DateOnly(2026, 10, 31), CurrencyCode = "SYP", CurrencyBasis = ProfessionalCurrencyBasis.Transaction,
                Sources = [ProfessionalReportSource.SalesInvoice], DescriptionContains = "[%]_", IncludeInvoiceDetails = true, IncludeAging = true,
                PatternRevisionIds = [current.PatternRevisionId],
            };
            var scope = LedgerReportScope.Create(s.CompanyA, s.BranchA1, s.Actor, false);
            var report = await service.ExecuteAsync(scope, request, Cancellation).ConfigureAwait(true);
            Assert.Equal(300m, report.Summary.ActualPeriodDebit);
            Assert.Equal(300m, report.Summary.ActualClosing);
            Assert.Equal(300m, report.Summary.FilteredDebit);
            var row = Assert.Single(report.Rows);
            Assert.Equal(posted.Id, row.DocumentId);
            Assert.Equal(posted.JournalEntryId, row.JournalId);
            Assert.Equal(ProfessionalReportSource.SalesInvoice, row.Source);
            Assert.Equal(2, report.InvoiceDetails.Count);
            Assert.Equal(300m, report.InvoiceDetails.Sum(d => d.LineTotal));
            Assert.NotNull(report.Aging);
            Assert.Equal(300m, report.Aging.Net);
            var hidden = await service.ExecuteAsync(scope, request with { DescriptionContains = "لا يوجد", IncludeInvoiceDetails = false, IncludeAging = false }, Cancellation).ConfigureAwait(true);
            Assert.Empty(hidden.Rows);
            Assert.Equal(300m, hidden.Summary.ActualClosing);
            Assert.Equal(0m, hidden.Summary.FilteredDebit);
            var ledger = await service.ExecuteAsync(scope, new ProfessionalReportRequest
            {
                Kind = ProfessionalReportKind.AccountLedger, FiscalYearId = year, AccountId = fixture.ReceivableId,
                From = request.From, To = request.To, CurrencyCode = "SYP", CurrencyBasis = ProfessionalCurrencyBasis.Transaction,
                Sources = [ProfessionalReportSource.SalesInvoice], PatternRevisionIds = [current.PatternRevisionId],
            }, Cancellation).ConfigureAwait(true);
            Assert.Equal(300m, ledger.Summary.ActualClosing);
            Assert.All(ledger.Rows, r => Assert.Equal(posted.Id, r.DocumentId));
            // A pattern narrows only its own source: other selected sources keep every row.
            var mixed = new ProfessionalReportRequest
            {
                Kind = ProfessionalReportKind.AccountLedger, FiscalYearId = year, AccountId = fixture.ReceivableId, From = request.From, To = request.To,
                Sources = [ProfessionalReportSource.SalesInvoice, ProfessionalReportSource.ReceiptVoucher, ProfessionalReportSource.UnclassifiedJournal],
            };
            var unrestricted = await service.ExecuteAsync(scope, mixed, Cancellation).ConfigureAwait(true);
            var narrowed = await service.ExecuteAsync(scope, mixed with { PatternRevisionIds = [current.PatternRevisionId] }, Cancellation).ConfigureAwait(true);
            Assert.Equal(unrestricted.Rows.Select(r => r.RowId), narrowed.Rows.Select(r => r.RowId));
            Assert.Equal(unrestricted.Summary.FilteredDebit, narrowed.Summary.FilteredDebit);
            var statementNarrowed = await service.ExecuteAsync(scope, request with
            {
                Sources = [ProfessionalReportSource.SalesInvoice, ProfessionalReportSource.ReceiptVoucher], IncludeInvoiceDetails = false, IncludeAging = false,
            }, Cancellation).ConfigureAwait(true);
            Assert.Equal(posted.Id, Assert.Single(statementNarrowed.Rows).DocumentId);
            var earlier = await sales.CreateInvoiceAsync(s.CompanyA, s.BranchA1, s.Actor,
                new CreateSalesInvoiceCommand(current.PatternRevisionId, current.CustomerId, current.WarehouseId,
                    new DateTimeOffset(2026, 10, 19, 9, 0, 0, TimeSpan.Zero), CurrencyCode: "SYP", ExchangeRate: 0.01m,
                    Notes: "فاتورة أحمد [%]_", Lines: [new(fixture.ItemId, fixture.UnitId, 1m, 50m)]), Cancellation).ConfigureAwait(true);
            await sales.PostInvoiceAsync(s.CompanyA, s.BranchA1, earlier.Id, s.Actor, new PostSalesInvoiceCommand("professional-earlier-date"), Cancellation).ConfigureAwait(true);
            var chronological = await service.ExecuteAsync(scope, request with { IncludeAging = false, IncludeInvoiceDetails = false }, Cancellation).ConfigureAwait(true);
            var repeated = await service.ExecuteAsync(scope, request with { IncludeAging = false, IncludeInvoiceDetails = false }, Cancellation).ConfigureAwait(true);
            Assert.Equal(new[] { earlier.Id, posted.Id }, chronological.Rows.Select(r => r.DocumentId!.Value));
            Assert.Equal(ChronologicalBalances, chronological.Rows.Select(r => r.ActualBalance));
            Assert.Equal(chronological.Rows.Select(r => r.RowId), repeated.Rows.Select(r => r.RowId));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ProfessionalSupplierRealPurchaseAndRepeatedVoucherDistributionsHaveStableBalancesAndReversals()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true);
            var s = fixture.Scenario;
            var purchase = await PurchaseSetupAsync(fixture).ConfigureAwait(true);
            var vouchers = await VoucherSetupAsync(fixture).ConfigureAwait(true);
            using var context = database.Context();
            var purchases = Purchases(context);
            var draft = await purchases.CreateDraftAsync(s.CompanyA, s.BranchA1, s.Actor,
                CementPurchase(purchase, PaymentMethod.Credit, 1m) with { CurrencyCode = "USD", ExchangeRate = 1m,
                    Lines = [new(purchase.Cement, purchase.Kg, 1m, 100m)] }, Cancellation).ConfigureAwait(true);
            var invoice = await purchases.PostAsync(s.CompanyA, s.BranchA1, draft.Id, s.Actor,
                new PetraERP.Application.Purchases.PostPurchaseInvoiceCommand(ConcurrencyToken.FromBytes(draft.RowVersion), "professional-purchase"), Cancellation).ConfigureAwait(true);
            var treasury = Treasury(context);
            var voucher = await treasury.CreateDraftAsync(s.CompanyA, s.BranchA1, s.Actor,
                new SaveVoucherCommand(VoucherKind.Payment, vouchers.Payment, null, SettlementMethod.Cash,
                    new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero), "USD", 1m, 100m, "أحمد [%]_ مورد", [],
                    [new(purchase.Supplier, null, 40m), new(purchase.Supplier, null, 60m)]), Cancellation).ConfigureAwait(true);
            var payment = await treasury.PostAsync(s.CompanyA, s.BranchA1, voucher.Id, s.Actor,
                new(ConcurrencyToken.FromBytes(voucher.RowVersion), "professional-distributions"), Cancellation).ConfigureAwait(true);
            var reports = new SqlProfessionalReportService(context, new SqlAccessEvaluator(context), new SqlCommercialReportService(context));
            var year = await context.FiscalYears.Where(y => y.CompanyId == s.CompanyA).Select(y => y.Id).SingleAsync(Cancellation).ConfigureAwait(true);
            var scope = LedgerReportScope.Create(s.CompanyA, s.BranchA1, s.Actor, false);
            var request = new ProfessionalReportRequest
            {
                Kind = ProfessionalReportKind.SupplierStatement, FiscalYearId = year, PartyId = purchase.Supplier,
                From = new DateOnly(2026, 10, 1), To = new DateOnly(2026, 10, 31), CurrencyBasis = ProfessionalCurrencyBasis.Transaction, CurrencyCode = "USD",
                Sources = [ProfessionalReportSource.PurchaseInvoice, ProfessionalReportSource.PaymentVoucher], IncludeInvoiceDetails = true, IncludeAging = true,
            };
            var report = await reports.ExecuteAsync(scope, request, Cancellation).ConfigureAwait(true);
            Assert.Equal(3, report.Rows.Count);
            Assert.Equal(100m, report.Summary.ActualPeriodDebit);
            Assert.Equal(100m, report.Summary.ActualPeriodCredit);
            Assert.Equal(0m, report.Summary.ActualClosing);
            Assert.Equal(invoice.Id, report.Rows[0].DocumentId);
            Assert.Single(report.InvoiceDetails);
            Assert.NotNull(report.Aging);
            Assert.Equal(100m, report.Aging.Advance);
            Assert.Equal(0m, report.Aging.Net);
            decimal balance = 0m;
            foreach (var row in report.Rows) { balance += row.Credit - row.Debit; Assert.Equal(balance, row.ActualBalance); }
            var repeated = await reports.ExecuteAsync(scope, request, Cancellation).ConfigureAwait(true);
            Assert.Equal(report.Rows.Select(r => r.RowId), repeated.Rows.Select(r => r.RowId));
            await treasury.ReverseAsync(s.CompanyA, s.BranchA1, payment.Id, s.Actor,
                new(ConcurrencyToken.FromBytes(payment.RowVersion), "عكس توزيعات المورد"), Cancellation).ConfigureAwait(true);
            // Period semantics: a reversal belongs to the period of ITS OWN accounting date, not to the period of the
            // document it reverses. An October statement therefore keeps showing the October settlement, and the
            // reversal rows appear once the window covers the reversal journal.
            var octoberStatement = await reports.ExecuteAsync(scope, request with { IncludeAging = false }, Cancellation).ConfigureAwait(true);
            Assert.Equal(3, octoberStatement.Rows.Count);
            var reversal = await context.Vouchers.AsNoTracking()
                .Where(v => v.Id == payment.Id && v.ReversalJournalEntryId != null)
                .Join(context.JournalEntries.AsNoTracking(), v => v.ReversalJournalEntryId!, j => j.Id, (_, j) => new { j.Id, j.AccountingDate })
                .SingleAsync(Cancellation).ConfigureAwait(true);

            Assert.DoesNotContain(reversal.Id, octoberStatement.Rows.Select(static r => r.JournalId ?? Guid.Empty));

            var reversalAccountingDate = reversal.AccountingDate;

            var throughReversal = await reports.ExecuteAsync(scope,
                request with { To = reversalAccountingDate, IncludeAging = false }, Cancellation).ConfigureAwait(true);

            Assert.Equal(5, throughReversal.Rows.Count);
            Assert.Equal(5, throughReversal.Rows.Select(static r => r.RowId).Distinct().Count());
            Assert.Equal(100m, throughReversal.Summary.ActualClosing);

            // The mirrors are per-distribution: each original distribution gets its own reversal row, and the original
            // rows stay visible - a correction never rewrites posted records.
            var mirrorRows = throughReversal.Rows.Where(r => r.JournalId == reversal.Id).OrderBy(static r => r.Credit).ToArray();
            Assert.Equal(2, mirrorRows.Length);
            Assert.Equal(40m, mirrorRows[0].Credit);
            Assert.Equal(60m, mirrorRows[1].Credit);
            Assert.All(mirrorRows, static r => Assert.Equal(0m, r.Debit));
            Assert.Equal(
                throughReversal.Rows.Where(r => r.JournalId != reversal.Id).Select(static r => r.RowId).Order().ToArray(),
                octoberStatement.Rows.Select(static r => r.RowId).Order().ToArray());

            var reversed = throughReversal;
            var catalog = await reports.GetCatalogAsync(scope, Cancellation).ConfigureAwait(true);
            Assert.Contains(catalog.Patterns, p => p.RevisionId == purchase.Revision && p.Source == ProfessionalReportSource.PurchaseInvoice);
        }
    }
}
