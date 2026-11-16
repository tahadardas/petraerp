using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.Reports;
using PetraERP.Application.Reports.Professional;
using PetraERP.Application.Security;
using PetraERP.Application.Treasury;
using PetraERP.Domain.Sales;
using PetraERP.Domain.Treasury;
using PetraERP.Infrastructure.Reports;
using PetraERP.Infrastructure.Reports.Professional;
using PetraERP.Infrastructure.Security;

namespace PetraERP.Infrastructure.Tests;

public sealed partial class SalesPostingSqlTests
{
    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task CombinedPartyStatementNetsPurchasesAndVouchersInOneDebitMinusCreditBalance()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true);
            var s = fixture.Scenario;
            var purchase = await PurchaseSetupAsync(fixture).ConfigureAwait(true);
            var vouchers = await VoucherSetupAsync(fixture).ConfigureAwait(true);
            using var context = database.Context();
            // The combined statement spans sales and purchases, so it requires both view permissions.
            foreach (var permission in new[] { PermissionCatalog.SalesInvoicesView, PermissionCatalog.TreasuryVouchersView })
                await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO [security].[RolePermissions]([RoleId],[PermissionCode]) SELECT [RoleId],{permission} FROM [security].[UserRoles] WHERE [UserId]={s.Actor} AND NOT EXISTS (SELECT 1 FROM [security].[RolePermissions] rp WHERE rp.[RoleId]=[security].[UserRoles].[RoleId] AND rp.[PermissionCode]={permission})", Cancellation).ConfigureAwait(true);
            var purchases = Purchases(context);
            var draft = await purchases.CreateDraftAsync(s.CompanyA, s.BranchA1, s.Actor,
                CementPurchase(purchase, PaymentMethod.Credit, 1m) with { CurrencyCode = "USD", ExchangeRate = 1m,
                    Lines = [new(purchase.Cement, purchase.Kg, 1m, 100m)] }, Cancellation).ConfigureAwait(true);
            await purchases.PostAsync(s.CompanyA, s.BranchA1, draft.Id, s.Actor,
                new PetraERP.Application.Purchases.PostPurchaseInvoiceCommand(ConcurrencyToken.FromBytes(draft.RowVersion), "combined-purchase"), Cancellation).ConfigureAwait(true);
            var reports = new SqlProfessionalReportService(context, new SqlAccessEvaluator(context), new SqlCommercialReportService(context));
            var year = await context.FiscalYears.Where(y => y.CompanyId == s.CompanyA).Select(y => y.Id).SingleAsync(Cancellation).ConfigureAwait(true);
            var scope = LedgerReportScope.Create(s.CompanyA, s.BranchA1, s.Actor, false);
            var request = new ProfessionalReportRequest
            {
                Kind = ProfessionalReportKind.CombinedPartyStatement, FiscalYearId = year, PartyId = purchase.Supplier,
                From = new DateOnly(2026, 10, 1), To = new DateOnly(2026, 10, 31), CurrencyBasis = ProfessionalCurrencyBasis.Transaction, CurrencyCode = "USD",
                Sources = [ProfessionalReportSource.PurchaseInvoice, ProfessionalReportSource.PaymentVoucher],
            };
            var owed = await reports.ExecuteAsync(scope, request, Cancellation).ConfigureAwait(true);
            // We owe the supplier 100: the combined balance is debit minus credit, so it is negative.
            Assert.Equal(-100m, owed.Summary.ActualClosing);
            Assert.Single(owed.Rows);

            var treasury = Treasury(context);
            var voucher = await treasury.CreateDraftAsync(s.CompanyA, s.BranchA1, s.Actor,
                new SaveVoucherCommand(VoucherKind.Payment, vouchers.Payment, null, SettlementMethod.Cash,
                    new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero), "USD", 1m, 100m, "تسوية موحدة", [],
                    [new(purchase.Supplier, null, 100m)]), Cancellation).ConfigureAwait(true);
            await treasury.PostAsync(s.CompanyA, s.BranchA1, voucher.Id, s.Actor,
                new(ConcurrencyToken.FromBytes(voucher.RowVersion), "combined-payment"), Cancellation).ConfigureAwait(true);
            var settled = await reports.ExecuteAsync(scope, request, Cancellation).ConfigureAwait(true);
            Assert.Equal(2, settled.Rows.Count);
            Assert.Equal(0m, settled.Summary.ActualClosing);

            // The combined view must agree with the supplier-only statement on the same movements.
            var supplier = await reports.ExecuteAsync(scope, request with { Kind = ProfessionalReportKind.SupplierStatement }, Cancellation).ConfigureAwait(true);
            Assert.Equal(supplier.Rows.Select(r => r.RowId).Order(), settled.Rows.Select(r => r.RowId).Order());

            // An account ledger without an account lists every account, each with its own running balance.
            var all = await reports.ExecuteAsync(scope, new ProfessionalReportRequest
            {
                Kind = ProfessionalReportKind.AccountLedger, FiscalYearId = year, From = request.From, To = request.To,
                Sources = [ProfessionalReportSource.PurchaseInvoice, ProfessionalReportSource.PaymentVoucher, ProfessionalReportSource.UnclassifiedJournal],
            }, Cancellation).ConfigureAwait(true);
            Assert.True(all.Rows.Select(r => r.AccountId).Distinct().Count() > 1);
            foreach (var account in all.Rows.GroupBy(r => r.AccountId))
                Assert.Equal(account.Sum(r => r.Debit - r.Credit), account.Last().ActualBalance);
            Assert.Equal(all.Rows.Select(r => r.AccountCode).Order(StringComparer.Ordinal), all.Rows.Select(r => r.AccountCode));

            // Al-Ameen rule: each movement is valued at the register rate effective on its own date (SYP enabled by the scenario).
            var baseCode = all.CurrencyCode;
            foreach (var (date, rate) in new[] { (new DateOnly(2026, 10, 1), 0.0001m), (new DateOnly(2026, 10, 18), 0.0002m) })
                context.ExchangeRates.Add(new PetraERP.Infrastructure.Currencies.ExchangeRateRow
                {
                    Id = Guid.NewGuid(), CompanyId = s.CompanyA, CurrencyCode = "SYP", BaseCurrencyCode = baseCode, Rate = rate,
                    EffectiveDate = date, Source = "test", Reason = "historical valuation test", CreatedByUserId = s.Actor,
                });
            await context.SaveChangesAsync(Cancellation).ConfigureAwait(true);
            var syp = await reports.ExecuteAsync(scope, new ProfessionalReportRequest
            {
                Kind = ProfessionalReportKind.AccountLedger, FiscalYearId = year, From = request.From, To = request.To,
                Sources = [ProfessionalReportSource.PurchaseInvoice, ProfessionalReportSource.PaymentVoucher, ProfessionalReportSource.UnclassifiedJournal],
                DisplayCurrencyCode = "SYP",
            }, Cancellation).ConfigureAwait(true);
            Assert.Equal("SYP", syp.CurrencyCode);
            Assert.Equal(all.Rows.Count, syp.Rows.Count);
            var byId = all.Rows.ToDictionary(r => r.RowId);
            foreach (var row in syp.Rows)
            {
                var native = byId[row.RowId];
                var rateOfDay = row.Date >= new DateOnly(2026, 10, 18) ? 0.0002m : 0.0001m;
                Assert.Equal(Math.Round(native.Debit / rateOfDay, 4, MidpointRounding.AwayFromZero), row.Debit);
                Assert.Equal(Math.Round(native.Credit / rateOfDay, 4, MidpointRounding.AwayFromZero), row.Credit);
            }
            foreach (var account in syp.Rows.GroupBy(r => r.AccountId))
                Assert.Equal(account.Sum(r => r.Debit - r.Credit), account.Last().ActualBalance);

            // The trial balance values each movement the same way (per account, per day, per currency).
            var trialSyp = await reports.ExecuteAsync(scope, new ProfessionalReportRequest
            {
                Kind = ProfessionalReportKind.TrialBalance, FiscalYearId = year, From = request.From, To = request.To,
                Sources = [ProfessionalReportSource.PurchaseInvoice, ProfessionalReportSource.PaymentVoucher, ProfessionalReportSource.UnclassifiedJournal],
                DisplayCurrencyCode = "SYP",
            }, Cancellation).ConfigureAwait(true);
            Assert.Equal("SYP", trialSyp.CurrencyCode);
            foreach (var row in trialSyp.Rows.Where(r => !r.IsAggregate))
                Assert.Equal(syp.Rows.Where(r => r.AccountId == row.AccountId).Sum(r => r.Debit), row.Debit);

            // Currency balance: one row per account and transaction currency; the local equivalent adds up to the trial balance.
            var sources3 = new[] { ProfessionalReportSource.PurchaseInvoice, ProfessionalReportSource.PaymentVoucher, ProfessionalReportSource.UnclassifiedJournal };
            var trialBase = await reports.ExecuteAsync(scope, new ProfessionalReportRequest
            {
                Kind = ProfessionalReportKind.TrialBalance, FiscalYearId = year, From = request.From, To = request.To, Sources = sources3,
            }, Cancellation).ConfigureAwait(true);
            var currencyBalance = await reports.ExecuteAsync(scope, new ProfessionalReportRequest
            {
                Kind = ProfessionalReportKind.CurrencyBalance, FiscalYearId = year, From = request.From, To = request.To, Sources = sources3,
                CurrencyBasis = ProfessionalCurrencyBasis.Base,
            }, Cancellation).ConfigureAwait(true);
            Assert.Equal(baseCode, currencyBalance.CurrencyCode);
            Assert.NotEmpty(currencyBalance.Rows);
            Assert.All(currencyBalance.Rows, row => Assert.False(string.IsNullOrWhiteSpace(row.TransactionCurrencyCode)));
            foreach (var row in trialBase.Rows.Where(r => !r.IsAggregate))
            {
                var perCurrency = currencyBalance.Rows.Where(r => r.AccountId == row.AccountId).ToList();
                Assert.Equal(row.Debit, perCurrency.Sum(r => r.Debit));
                Assert.Equal(row.Credit, perCurrency.Sum(r => r.Credit));
                Assert.Equal(row.ActualBalance, perCurrency.Sum(r => r.ActualBalance));
            }
            Assert.Contains(currencyBalance.Rows, r => r.TransactionCurrencyCode == "USD" && (r.TransactionDebit != 0m || r.TransactionCredit != 0m));
            var onlyUsd = await reports.ExecuteAsync(scope, new ProfessionalReportRequest
            {
                Kind = ProfessionalReportKind.CurrencyBalance, FiscalYearId = year, From = request.From, To = request.To, Sources = sources3,
                CurrencyBasis = ProfessionalCurrencyBasis.Base, CurrencyCode = "USD",
            }, Cancellation).ConfigureAwait(true);
            Assert.NotEmpty(onlyUsd.Rows);
            Assert.All(onlyUsd.Rows, row => Assert.Equal("USD", row.TransactionCurrencyCode));
            await Assert.ThrowsAnyAsync<Exception>(() => reports.ExecuteAsync(scope, new ProfessionalReportRequest
            {
                Kind = ProfessionalReportKind.CurrencyBalance, FiscalYearId = year, From = request.From, To = request.To, Sources = sources3,
                CurrencyBasis = ProfessionalCurrencyBasis.Base, DisplayCurrencyCode = "SYP",
            }, Cancellation)).ConfigureAwait(true);


            // A party statement keeps the document currency in the transaction columns and values each movement on its own date.
            var supplierSyp = await reports.ExecuteAsync(scope, request with { Kind = ProfessionalReportKind.SupplierStatement, DisplayCurrencyCode = "SYP" }, Cancellation).ConfigureAwait(true);
            Assert.Equal("SYP", supplierSyp.CurrencyCode);
            Assert.Equal(supplier.Rows.Count, supplierSyp.Rows.Count);
            foreach (var row in supplierSyp.Rows)
            {
                var original = supplier.Rows.Single(r => r.RowId == row.RowId);
                var rateOfDay = row.Date >= new DateOnly(2026, 10, 18) ? 0.0002m : 0.0001m;
                Assert.Equal(Math.Round(original.Debit / rateOfDay, 4, MidpointRounding.AwayFromZero), row.Debit);
                Assert.Equal(Math.Round(original.Credit / rateOfDay, 4, MidpointRounding.AwayFromZero), row.Credit);
                Assert.Equal("USD", row.TransactionCurrencyCode);
            }

            // A display currency that is not enabled for the company is refused instead of guessing a rate.
            var denied = await Assert.ThrowsAnyAsync<Exception>(() => reports.ExecuteAsync(scope, new ProfessionalReportRequest
            {
                Kind = ProfessionalReportKind.AccountLedger, FiscalYearId = year, From = request.From, To = request.To,
                Sources = [ProfessionalReportSource.PurchaseInvoice], DisplayCurrencyCode = "XXX",
            }, Cancellation)).ConfigureAwait(true);
            Assert.Contains("XXX", denied.Message, StringComparison.Ordinal);
            // The base currency as display currency is the identity conversion.
            var same = await reports.ExecuteAsync(scope, new ProfessionalReportRequest
            {
                Kind = ProfessionalReportKind.AccountLedger, FiscalYearId = year, From = request.From, To = request.To,
                Sources = [ProfessionalReportSource.PurchaseInvoice, ProfessionalReportSource.PaymentVoucher, ProfessionalReportSource.UnclassifiedJournal],
                DisplayCurrencyCode = all.CurrencyCode,
            }, Cancellation).ConfigureAwait(true);
            Assert.Equal(all.Rows.Select(r => r.ActualBalance), same.Rows.Select(r => r.ActualBalance));
        }
    }
}
