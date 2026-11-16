using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;
using PetraERP.Application.GeneralLedger.Reports;
using PetraERP.Application.Inventory;
using PetraERP.Application.Parties;
using PetraERP.Application.Patterns;
using PetraERP.Application.Reports.Professional;
using PetraERP.Application.Sales;
using PetraERP.Application.Security;
using PetraERP.Application.Treasury;
using PetraERP.Domain.Inventory;
using PetraERP.Domain.Parties;
using PetraERP.Domain.Patterns;
using PetraERP.Domain.Primitives;
using PetraERP.Domain.Sales;
using PetraERP.Domain.Treasury;
using PetraERP.Infrastructure.Inventory;
using PetraERP.Infrastructure.Parties;
using PetraERP.Infrastructure.Patterns;
using PetraERP.Infrastructure.Reports;
using PetraERP.Infrastructure.Reports.Professional;
using PetraERP.Infrastructure.Security;

namespace PetraERP.Infrastructure.Tests;

// Section 16 matrix coverage: T06 (customer statement in a non-USD-base company), T07 (cash invoices never
// create receivable movements) and T10 (cross-currency settlement posted and reversed through real services).
public sealed partial class SalesPostingSqlTests
{
    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task CustomerStatementInSyPBaseCompanyRequiresExplicitUsdAndNeverSubstitutesBaseCurrency()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var s = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            using var context = database.Context();
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [system].[CompanyAdditionalCurrencies]([CompanyId],[CurrencyCode]) VALUES({s.CompanyB},N'USD')", Cancellation).ConfigureAwait(true);
            foreach (var permission in new[] { "Sales.Invoices.Post", PermissionCatalog.SalesInvoicesView, PermissionCatalog.TreasuryVouchersView })
                await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO [security].[RolePermissions]([RoleId],[PermissionCode]) SELECT [RoleId],{permission} FROM [security].[UserRoles] WHERE [UserId]={s.Actor}", Cancellation).ConfigureAwait(true);
            var ar = await s.AccountAsync(s.CompanyB, "1131", AccountTypeCode.Assets, true).ConfigureAwait(true);
            var cashAccount = await s.AccountAsync(s.CompanyB, "1011", AccountTypeCode.Assets, true).ConfigureAwait(true);
            var revenue = await s.AccountAsync(s.CompanyB, "4111", AccountTypeCode.Revenue, true).ConfigureAwait(true);
            var cogs = await s.AccountAsync(s.CompanyB, "5111", AccountTypeCode.Expenses, true).ConfigureAwait(true);
            var inventoryAccount = await s.AccountAsync(s.CompanyB, "1211", AccountTypeCode.Assets, true).ConfigureAwait(true);
            var discount = await s.AccountAsync(s.CompanyB, "5121", AccountTypeCode.Expenses, true).ConfigureAwait(true);
            var tax = await s.AccountAsync(s.CompanyB, "2111", AccountTypeCode.Liabilities, true).ConfigureAwait(true);
            var inventory = new SqlInventoryService(context);
            var unit = await inventory.CreateUnitAsync(s.CompanyB, new CreateUnitCommand("PCS", "قطعة", null, "pc", 0), Cancellation).ConfigureAwait(true);
            var warehouse = await inventory.CreateWarehouseAsync(s.CompanyB, new CreateWarehouseCommand(s.BranchB1, "WHB", "مستودع الشركة الأخرى", null, null), Cancellation).ConfigureAwait(true);
            var item = await inventory.CreateItemAsync(s.CompanyB, new CreateItemCommand("I1B", "مادة بالدولار", null, null, unit.Id, warehouse.Id, CostMethod.WeightedAverage, null, null, null), Cancellation).ConfigureAwait(true);
            await LedgerScenario.AddStockMovementAsync(context, new StockMovementRow
            {
                Id = Guid.NewGuid(), CompanyId = s.CompanyB, BranchId = s.BranchB1, WarehouseId = warehouse.Id, ItemId = item.Id, UnitId = unit.Id,
                MovementKind = (int)StockMovementKind.OpeningBalance, Quantity = 10m, BaseQuantity = 10m, UnitCost = 100m, TotalCost = 1000m,
                MovementDateUtc = new DateTimeOffset(2026, 10, 14, 0, 0, 0, TimeSpan.Zero), DocumentType = "OpeningBalance", DocumentId = Guid.NewGuid(),
                CreatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = s.Actor,
            }, Cancellation).ConfigureAwait(true);
            var customer = await new SqlPartyService(context).CreatePartyAsync(s.CompanyB,
                new CreatePartyCommand("CUSD", "عميل الدولار", null, PartyRole.Customer, ReceivableAccountId: ar.Id), Cancellation).ConfigureAwait(true);
            var patterns = new SqlPatternService(context);
            var patternScope = PatternScope.Create(s.CompanyB, s.BranchB1, s.Actor);
            var pattern = await patterns.CreatePatternAsync(patternScope, new CreateDocumentPatternCommand
            {
                Code = "SIB", NameArabic = "بيع بالدولار", ShortCode = "SIB", OperationKind = BusinessOperationKind.SalesInvoice, BranchIds = [s.BranchB1],
                InitialConfiguration = new PatternConfiguration
                {
                    AccountMappings = [new() { Role = "Customer", AccountId = ar.Id }, new() { Role = "Sales", AccountId = revenue.Id },
                        new() { Role = "CostOfGoods", AccountId = cogs.Id }, new() { Role = "Inventory", AccountId = inventoryAccount.Id },
                        new() { Role = "Discount", AccountId = discount.Id }, new() { Role = "Cash", AccountId = cashAccount.Id }, new() { Role = "Tax", AccountId = tax.Id }],
                    NumberingPolicy = new() { Prefix = "SIB-", MinimumDigits = 4 },
                }
            }, Cancellation).ConfigureAwait(true);
            var published = await patterns.PublishRevisionAsync(patternScope, pattern.Id, new PublishPatternRevisionCommand
            { ExpectedVersion = pattern.Version, RevisionId = pattern.ActiveRevision!.Id, Reason = "نشر" }, Cancellation).ConfigureAwait(true);
            var sales = Service(context);
            var draft = await sales.CreateInvoiceAsync(s.CompanyB, s.BranchB1, s.Actor, new CreateSalesInvoiceCommand(published.ActiveRevision!.Id, customer.Id, warehouse.Id,
                new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.Zero), PaymentMethod.Credit, CurrencyCode: "USD", ExchangeRate: 100m,
                Lines: [new(item.Id, unit.Id, 2m, 50m)]), Cancellation).ConfigureAwait(true);
            var posted = await sales.PostInvoiceAsync(s.CompanyB, s.BranchB1, draft.Id, s.Actor, new PostSalesInvoiceCommand("t06-usd-invoice"), Cancellation).ConfigureAwait(true);
            Assert.Equal(100m, posted.GrandTotal);
            var reports = new SqlProfessionalReportService(context, new SqlAccessEvaluator(context), new SqlCommercialReportService(context));
            var year = await context.FiscalYears.Where(y => y.CompanyId == s.CompanyB).Select(y => y.Id).SingleAsync(Cancellation).ConfigureAwait(true);
            var scope = LedgerReportScope.Create(s.CompanyB, s.BranchB1, s.Actor, false);
            var before = await reports.GetPartyActivityCurrenciesAsync(scope, year, customer.Id, ProfessionalReportKind.CustomerStatement,
                new DateOnly(2026, 10, 14), Cancellation).ConfigureAwait(true);
            Assert.Empty(before);
            var currencies = await reports.GetPartyActivityCurrenciesAsync(scope, year, customer.Id, ProfessionalReportKind.CustomerStatement,
                new DateOnly(2026, 10, 31), Cancellation).ConfigureAwait(true);
            Assert.Equal("USD", Assert.Single(currencies));
            var baseRequest = new ProfessionalReportRequest
            {
                Kind = ProfessionalReportKind.CustomerStatement, FiscalYearId = year, PartyId = customer.Id,
                From = new DateOnly(2026, 10, 1), To = new DateOnly(2026, 12, 31), Sources = [ProfessionalReportSource.SalesInvoice],
            };
            // A defaulted base-currency basis is refused outright: the statement can never silently run in SYP.
            var substitution = await Assert.ThrowsAsync<RequestValidationException>(() => reports.ExecuteAsync(scope, baseRequest, Cancellation)).ConfigureAwait(true);
            Assert.Contains("لا تُستبدل أي عملة تلقائياً", substitution.Message, StringComparison.Ordinal);
            var syp = await reports.ExecuteAsync(scope, baseRequest with { CurrencyBasis = ProfessionalCurrencyBasis.Transaction, CurrencyCode = "SYP" }, Cancellation).ConfigureAwait(true);
            // The USD customer looks empty under SYP, but the server states why and which currencies hold activity.
            Assert.Empty(syp.Rows);
            Assert.Equal(0m, syp.Summary.ActualClosing);
            Assert.Contains(syp.Notes, note => note.Contains("بالعملة SYP", StringComparison.Ordinal) && note.Contains("USD", StringComparison.Ordinal));
            var usd = await reports.ExecuteAsync(scope, baseRequest with { CurrencyBasis = ProfessionalCurrencyBasis.Transaction, CurrencyCode = "USD" }, Cancellation).ConfigureAwait(true);
            var row = Assert.Single(usd.Rows);
            Assert.Equal(ProfessionalReportSource.SalesInvoice, row.Source);
            Assert.Equal(100m, row.Debit);
            Assert.Equal(100m, row.ActualBalance);
            Assert.Equal(100m, usd.Summary.ActualClosing);
            Assert.Equal(0m, usd.Summary.ActualOpening);
            Assert.Equal("USD", usd.Parameters.CurrencyCode);
            Assert.DoesNotContain(usd.Notes, note => note.Contains("لا حركات لهذا الطرف بالعملة USD", StringComparison.Ordinal));
            var afterInvoice = await reports.ExecuteAsync(scope, baseRequest with { From = new DateOnly(2026, 10, 16), CurrencyBasis = ProfessionalCurrencyBasis.Transaction, CurrencyCode = "USD" }, Cancellation).ConfigureAwait(true);
            Assert.Empty(afterInvoice.Rows);
            Assert.Equal(100m, afterInvoice.Summary.ActualOpening);
            Assert.Contains(afterInvoice.Notes, note => note.Contains("لا توجد حركات في الفترة المحددة", StringComparison.Ordinal));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task CustomerStatementExcludesCashInvoiceBecauseItNeverCreatesAReceivable()
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
            await sales.UpdateInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, new UpdateSalesInvoiceCommand(current.CustomerId, current.WarehouseId,
                new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.Zero), CurrencyCode: "USD", ExchangeRate: 1m, Notes: "فاتورة آجلة 200",
                Lines: [new(fixture.ItemId, fixture.UnitId, 2m, 100m)]) { ExpectedVersion = ConcurrencyToken.FromBytes(current.RowVersion) }, Cancellation).ConfigureAwait(true);
            var credit = await sales.PostInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, s.Actor, new PostSalesInvoiceCommand("t07-credit"), Cancellation).ConfigureAwait(true);
            Assert.Equal(200m, credit.GrandTotal);
            var cashDraft = await sales.CreateInvoiceAsync(s.CompanyA, s.BranchA1, s.Actor, new CreateSalesInvoiceCommand(current.PatternRevisionId, current.CustomerId, current.WarehouseId,
                new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero), PaymentMethod.Cash, CurrencyCode: "USD", ExchangeRate: 1m,
                Lines: [new(fixture.ItemId, fixture.UnitId, 1m, 100m)]), Cancellation).ConfigureAwait(true);
            var cash = await sales.PostInvoiceAsync(s.CompanyA, s.BranchA1, cashDraft.Id, s.Actor, new PostSalesInvoiceCommand("t07-cash"), Cancellation).ConfigureAwait(true);
            Assert.Equal(100m, cash.GrandTotal);
            var reports = new SqlProfessionalReportService(context, new SqlAccessEvaluator(context), new SqlCommercialReportService(context));
            var year = await context.FiscalYears.Where(y => y.CompanyId == s.CompanyA).Select(y => y.Id).SingleAsync(Cancellation).ConfigureAwait(true);
            var scope = LedgerReportScope.Create(s.CompanyA, s.BranchA1, s.Actor, false);
            var request = new ProfessionalReportRequest
            {
                Kind = ProfessionalReportKind.CustomerStatement, FiscalYearId = year, PartyId = current.CustomerId,
                From = new DateOnly(2026, 10, 1), To = new DateOnly(2026, 12, 31), CurrencyCode = "USD", CurrencyBasis = ProfessionalCurrencyBasis.Transaction,
                Sources = [ProfessionalReportSource.SalesInvoice, ProfessionalReportSource.SalesReturn, ProfessionalReportSource.ReceiptVoucher, ProfessionalReportSource.Settlement],
            };
            var report = await reports.ExecuteAsync(scope, request, Cancellation).ConfigureAwait(true);
            var row = Assert.Single(report.Rows);
            Assert.Equal(credit.Id, row.DocumentId);
            Assert.Equal(200m, report.Summary.ActualPeriodDebit);
            Assert.Equal(0m, report.Summary.ActualPeriodCredit);
            Assert.Equal(200m, report.Summary.ActualClosing);
            var ledger = await reports.ExecuteAsync(scope, new ProfessionalReportRequest
            {
                Kind = ProfessionalReportKind.AccountLedger, FiscalYearId = year, AccountId = fixture.ReceivableId,
                From = request.From, To = request.To, CurrencyCode = "USD", CurrencyBasis = ProfessionalCurrencyBasis.Transaction,
                Sources = [ProfessionalReportSource.SalesInvoice],
            }, Cancellation).ConfigureAwait(true);
            Assert.Equal(200m, ledger.Summary.ActualClosing);
            Assert.DoesNotContain(ledger.Rows, r => r.DocumentId == cash.Id);
            var cashLedger = await reports.ExecuteAsync(scope, new ProfessionalReportRequest
            {
                Kind = ProfessionalReportKind.AccountLedger, FiscalYearId = year, AccountId = fixture.CashId,
                From = request.From, To = request.To, CurrencyCode = "USD", CurrencyBasis = ProfessionalCurrencyBasis.Transaction,
                Sources = [ProfessionalReportSource.SalesInvoice],
            }, Cancellation).ConfigureAwait(true);
            Assert.Equal(100m, cashLedger.Summary.ActualClosing);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task CustomerStatementTracksCrossCurrencySettlementAndItsReversalThroughRealPostings()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true);
            var s = fixture.Scenario;
            using var context = database.Context();
            await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO [security].[RolePermissions]([RoleId],[PermissionCode]) SELECT [RoleId],{PermissionCatalog.SalesInvoicesView} FROM [security].[UserRoles] WHERE [UserId]={s.Actor}", Cancellation).ConfigureAwait(true);
            var sales = Service(context);
            var current = await sales.GetInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, Cancellation).ConfigureAwait(true);
            await sales.UpdateInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, new UpdateSalesInvoiceCommand(current.CustomerId, current.WarehouseId,
                new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.Zero), CurrencyCode: "USD", ExchangeRate: 1m, Notes: "فاتورة 1000 دولار",
                Lines: [new(fixture.ItemId, fixture.UnitId, 10m, 100m)]) { ExpectedVersion = ConcurrencyToken.FromBytes(current.RowVersion) }, Cancellation).ConfigureAwait(true);
            var posted = await sales.PostInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, s.Actor, new PostSalesInvoiceCommand("t10-invoice"), Cancellation).ConfigureAwait(true);
            Assert.Equal(1000m, posted.GrandTotal);
            var setup = await VoucherSetupAsync(fixture).ConfigureAwait(true);
            var treasury = Treasury(context);
            // An SYP receipt settles 400 USD of the USD invoice at 0.01 USD per SYP: invoice amount = settlement amount x cross rate.
            var voucherDraft = await treasury.CreateDraftAsync(s.CompanyA, s.BranchA1, s.Actor, new SaveVoucherCommand(
                VoucherKind.Receipt, setup.Receipt, setup.Customer, SettlementMethod.Cash, new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero),
                "SYP", 0.01m, 40000m, "سند قبض بعملة غير عملة الفاتورة", [], [])
            { InvoiceAllocations = [new InvoiceSettlementCommand(fixture.InvoiceId, 400m, 40000m, 0.01m)] }, Cancellation).ConfigureAwait(true);
            var postedVoucher = await treasury.PostAsync(s.CompanyA, s.BranchA1, voucherDraft.Id, s.Actor,
                new PostVoucherCommand(ConcurrencyToken.FromBytes(voucherDraft.RowVersion), "t10-settle"), Cancellation).ConfigureAwait(true);
            var reports = new SqlProfessionalReportService(context, new SqlAccessEvaluator(context), new SqlCommercialReportService(context));
            var year = await context.FiscalYears.Where(y => y.CompanyId == s.CompanyA).Select(y => y.Id).SingleAsync(Cancellation).ConfigureAwait(true);
            var scope = LedgerReportScope.Create(s.CompanyA, s.BranchA1, s.Actor, false);
            var activityCurrencies = await reports.GetPartyActivityCurrenciesAsync(scope, year, setup.Customer,
                ProfessionalReportKind.CustomerStatement, new DateOnly(2026, 12, 31), Cancellation).ConfigureAwait(true);
            Assert.Collection(activityCurrencies, first => Assert.Equal("SYP", first), second => Assert.Equal("USD", second));
            var request = new ProfessionalReportRequest
            {
                Kind = ProfessionalReportKind.CustomerStatement, FiscalYearId = year, PartyId = setup.Customer,
                From = new DateOnly(2026, 10, 1), To = new DateOnly(2026, 12, 31), CurrencyCode = "USD", CurrencyBasis = ProfessionalCurrencyBasis.Transaction,
                Sources = [ProfessionalReportSource.SalesInvoice, ProfessionalReportSource.ReceiptVoucher, ProfessionalReportSource.Settlement],
            };
            var usd = await reports.ExecuteAsync(scope, request, Cancellation).ConfigureAwait(true);
            Assert.Equal(2, usd.Rows.Count);
            Assert.Equal(1000m, usd.Summary.ActualPeriodDebit);
            Assert.Equal(400m, usd.Summary.ActualPeriodCredit);
            Assert.Equal(600m, usd.Summary.ActualClosing);
            Assert.Contains(usd.Rows, row => row.Source == ProfessionalReportSource.Settlement && row.Credit == 400m);
            // In SYP the same voucher nets to zero: the inflow leaves again as its USD application.
            var syp = await reports.ExecuteAsync(scope, request with { CurrencyCode = "SYP" }, Cancellation).ConfigureAwait(true);
            Assert.Equal(2, syp.Rows.Count);
            Assert.Equal(40000m, Assert.Single(syp.Rows, row => row.Source == ProfessionalReportSource.ReceiptVoucher).Credit);
            Assert.Equal(0m, syp.Summary.ActualClosing);
            await treasury.ReverseAsync(s.CompanyA, s.BranchA1, postedVoucher.Id, s.Actor,
                new ReverseVoucherCommand(ConcurrencyToken.FromBytes(postedVoucher.RowVersion), "عكس التسوية المتعدد العملات"), Cancellation).ConfigureAwait(true);
            var afterReversalCurrencies = await reports.GetPartyActivityCurrenciesAsync(scope, year, setup.Customer,
                ProfessionalReportKind.CustomerStatement, new DateOnly(2026, 12, 31), Cancellation).ConfigureAwait(true);
            Assert.Collection(afterReversalCurrencies, first => Assert.Equal("SYP", first), second => Assert.Equal("USD", second));
            var reversed = await reports.ExecuteAsync(scope, request, Cancellation).ConfigureAwait(true);
            Assert.Equal(3, reversed.Rows.Count);
            Assert.Equal(2, reversed.Rows.Count(row => row.Source == ProfessionalReportSource.Settlement));
            Assert.Equal(1000m, reversed.Summary.ActualClosing);
            var reversedSyp = await reports.ExecuteAsync(scope, request with { CurrencyCode = "SYP" }, Cancellation).ConfigureAwait(true);
            Assert.Equal(0m, reversedSyp.Summary.ActualClosing);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task CustomerStatementWithoutControlAccountFailsLoudlyWithAssignmentInstruction()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true);
            var s = fixture.Scenario;
            using var context = database.Context();
            foreach (var permission in new[] { PermissionCatalog.SalesInvoicesView, PermissionCatalog.TreasuryVouchersView })
                await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO [security].[RolePermissions]([RoleId],[PermissionCode]) SELECT [RoleId],{permission} FROM [security].[UserRoles] WHERE [UserId]={s.Actor}", Cancellation).ConfigureAwait(true);
            // A customer with no mapped receivable account must never yield a silently empty statement.
            var unassigned = await new SqlPartyService(context).CreatePartyAsync(s.CompanyA,
                new CreatePartyCommand("CAR", "عميل بلا حساب ذمة", null, PartyRole.Customer), Cancellation).ConfigureAwait(true);
            // Creating a customer now provisions its own receivable account; simulate legacy data that has none.
            await context.Parties.Where(p => p.Id == unassigned.Id).ExecuteUpdateAsync(u => u.SetProperty(p => p.ReceivableAccountId, (Guid?)null), Cancellation).ConfigureAwait(true);
            var reports = new SqlProfessionalReportService(context, new SqlAccessEvaluator(context), new SqlCommercialReportService(context));
            var year = await context.FiscalYears.Where(y => y.CompanyId == s.CompanyA).Select(y => y.Id).SingleAsync(Cancellation).ConfigureAwait(true);
            var scope = LedgerReportScope.Create(s.CompanyA, s.BranchA1, s.Actor, false);
            var failure = await Assert.ThrowsAsync<BusinessRuleException>(() => reports.ExecuteAsync(scope, new ProfessionalReportRequest
            {
                Kind = ProfessionalReportKind.CustomerStatement, FiscalYearId = year, PartyId = unassigned.Id,
                From = new DateOnly(2026, 10, 1), To = new DateOnly(2026, 12, 31), CurrencyCode = "USD", CurrencyBasis = ProfessionalCurrencyBasis.Transaction,
                Sources = [ProfessionalReportSource.SalesInvoice],
            }, Cancellation)).ConfigureAwait(true);
            Assert.Contains("حساب قبض الزبائن", failure.Message, StringComparison.Ordinal);
            Assert.Contains("receivable", failure.Message, StringComparison.OrdinalIgnoreCase);
        }
    }
}
