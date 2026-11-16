using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;
using PetraERP.Application.Inventory;
using PetraERP.Application.Parties;
using PetraERP.Application.Patterns;
using PetraERP.Application.Sales;
using PetraERP.Domain.Inventory;
using PetraERP.Domain.AccountingCalendars;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Domain.Parties;
using PetraERP.Domain.Patterns;
using PetraERP.Domain.Sales;
using PetraERP.Infrastructure.Inventory;
using PetraERP.Infrastructure.Parties;
using PetraERP.Infrastructure.Patterns;
using PetraERP.Infrastructure.Sales;
using Xunit;

namespace PetraERP.Infrastructure.Tests;

public sealed class SalesInvoiceSqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task SalesInvoiceCreationQueryAndApproval()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var scenario = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            using var context = database.Context();

            var invService = new SqlInventoryService(context);
            var partiesService = new SqlPartyService(context);
            var patternsService = new SqlPatternService(context);
            var salesService = new SqlSalesInvoiceService(context, patternsService, TimeProvider.System);

            // 1. Setup Master Data
            var unit = await invService.CreateUnitAsync(scenario.CompanyA, new CreateUnitCommand("M3", "متر مكعب", "Cubic Meter", "m³", 2), Cancellation).ConfigureAwait(true);
            var warehouse = await invService.CreateWarehouseAsync(scenario.CompanyA, new CreateWarehouseCommand(scenario.BranchA1, "WH-01", "مستودع الخرسانة", "Concrete Yard", null), Cancellation).ConfigureAwait(true);
            var item = await invService.CreateItemAsync(scenario.CompanyA, new CreateItemCommand("CONC-30", "خرسانة جاهزة C30", "Ready-mix Concrete C30", null, unit.Id, warehouse.Id, CostMethod.WeightedAverage, 0m, 1000m, 50m), Cancellation).ConfigureAwait(true);

            var customer = await partiesService.CreatePartyAsync(scenario.CompanyA, new CreatePartyCommand(
                Code: "CUST-001",
                NameArabic: "شركة الإعمار الهندسية",
                NameEnglish: "Emaar Engineering Co",
                Role: PartyRole.Customer,
                CreditLimit: 0m,
                PaymentTermDays: 30,
                PolicyKind: CreditPolicyKind.NoLimit), Cancellation).ConfigureAwait(true);

            var revAcc = await scenario.AccountAsync(scenario.CompanyA, "4111", AccountTypeCode.Revenue, true).ConfigureAwait(true);
            var invAcc = await scenario.AccountAsync(scenario.CompanyA, "1141", AccountTypeCode.Assets, true).ConfigureAwait(true);
            var cogsAcc = await scenario.AccountAsync(scenario.CompanyA, "5111", AccountTypeCode.Expenses, true).ConfigureAwait(true);
            var scope = PatternScope.Create(scenario.CompanyA, scenario.BranchA1, scenario.Actor);
            var patternCmd = new CreateDocumentPatternCommand
            {
                OperationKind = BusinessOperationKind.SalesInvoice,
                Code = "SINV",
                NameArabic = "فاتورة مبيعات خرسانة",
                NameEnglish = "Sales Invoice",
                ShortCode = "INV",
                Icon = "invoice",
                Group = "Sales",
                IsDefault = true,
                BranchIds = [scenario.BranchA1],
                InitialConfiguration = new PatternConfiguration
                {
                    AccountMappings = [new() { Role = "Revenue", AccountId = revAcc.Id }, new() { Role = "Inventory", AccountId = invAcc.Id }, new() { Role = "COGS", AccountId = cogsAcc.Id }],
                    NumberingPolicy = new PatternNumberingPolicy
                    {
                        Prefix = "INV-",
                        MinimumDigits = 5,
                        Scope = PatternNumberingScope.BranchAndFiscalYear,
                    }
                }
            };

            var pattern = await patternsService.CreatePatternAsync(scope, patternCmd, Cancellation).ConfigureAwait(true);

            var patternRevision = await patternsService.PublishRevisionAsync(scope, pattern.Id, new PublishPatternRevisionCommand
            {
                ExpectedVersion = pattern.Version,
                RevisionId = pattern.ActiveRevision!.Id,
                Reason = "Ready for sales",
            }, Cancellation).ConfigureAwait(true);

            // 2. Create Draft Sales Invoice
            var createCmd = new CreateSalesInvoiceCommand(
                PatternRevisionId: patternRevision.ActiveRevision!.Id,
                CustomerId: customer.Id,
                WarehouseId: warehouse.Id,
                InvoiceDateUtc: DateTimeOffset.UtcNow,
                PaymentMethod: PaymentMethod.Credit,
                CurrencyCode: "USD",
                ExchangeRate: 1.0m,
                CostCenterId: null,
                Notes: "صبة موقع المشروع رقم 4",
                Lines:
                [
                    new CreateSalesInvoiceLineCommand(
                        ItemId: item.Id,
                        UnitId: unit.Id,
                        Quantity: 12.0000m,
                        UnitPrice: 65.0000m,
                        DiscountPercentage: 5.0000m,
                        DiscountAmount: 39.0000m,
                        TaxPercentage: 16.0000m,
                        TaxAmount: 118.5600m,
                        PumpCost: 50.0000m,
                        TransportCost: 30.0000m,
                        CostCenterId: null,
                        WarehouseId: warehouse.Id,
                        Notes: "خرسانة مسلحة سقف أول",
                        ConcreteGrade: "C30",
                        DisplayOrder: 1)
                ]);

            var created = await salesService.CreateInvoiceAsync(scenario.CompanyA, scenario.BranchA1, scenario.Actor, createCmd, Cancellation).ConfigureAwait(true);

            Assert.NotEqual(Guid.Empty, created.Id);
            Assert.Equal(SalesInvoiceStatus.Draft, created.Status);
            Assert.Equal(780.0000m, created.SubTotal);
            Assert.Equal(39.0000m, created.DiscountTotal);
            Assert.Equal(118.5600m, created.TaxTotal);
            Assert.Equal(80.0000m, created.AdditionsTotal);
            Assert.Equal(939.5600m, created.GrandTotal);

            // 3. Query Invoice
            var queried = await salesService.GetInvoiceAsync(scenario.CompanyA, scenario.BranchA1, created.Id, Cancellation).ConfigureAwait(true);
            Assert.Equal(created.Id, queried.Id);
            Assert.Single(queried.Lines);
            Assert.Equal("C30", queried.Lines[0].ConcreteGrade);
            var foreignCenter = await LedgerScenario.Chart(context).CreateCostCenterAsync(scenario.Master(scenario.CompanyB),
                new PetraERP.Application.GeneralLedger.ChartOfAccounts.CostCenterInput { Code = "SECRET", NameArabic = "مركز شركة أخرى" }, Cancellation).ConfigureAwait(true);
            await Assert.ThrowsAsync<BusinessRuleException>(() => salesService.CreateInvoiceAsync(scenario.CompanyA, scenario.BranchA1, scenario.Actor,
                createCmd with { CostCenterId = foreignCenter.Id }, Cancellation)).ConfigureAwait(true);
            await Assert.ThrowsAsync<BusinessRuleException>(() => salesService.UpdateInvoiceAsync(scenario.CompanyA, scenario.BranchA1, created.Id,
                new UpdateSalesInvoiceCommand(customer.Id, warehouse.Id, CostCenterId: foreignCenter.Id, Lines: createCmd.Lines)
                { ExpectedVersion = ConcurrencyToken.FromBytes(queried.RowVersion) }, Cancellation)).ConfigureAwait(true);
            await Assert.ThrowsAsync<BusinessRuleException>(() => salesService.CreateInvoiceAsync(scenario.CompanyA, scenario.BranchA1, scenario.Actor,
                createCmd with { Lines = createCmd.Lines!.Select(line => line with { CostCenterId = foreignCenter.Id }).ToArray() }, Cancellation)).ConfigureAwait(true);
            var otherWarehouse = await invService.CreateWarehouseAsync(scenario.CompanyB,
                new CreateWarehouseCommand(scenario.BranchB1, "OTHER-WH", "مستودع شركة أخرى", null, null), Cancellation).ConfigureAwait(true);
            await Assert.ThrowsAsync<BusinessRuleException>(() => salesService.CreateInvoiceAsync(scenario.CompanyA, scenario.BranchA1, scenario.Actor,
                createCmd with { Lines = createCmd.Lines!.Select(line => line with { WarehouseId = otherWarehouse.Id }).ToArray() }, Cancellation)).ConfigureAwait(true);
            var preserved = await salesService.GetInvoiceAsync(scenario.CompanyA, scenario.BranchA1, created.Id, Cancellation).ConfigureAwait(true);
            Assert.Null(preserved.CostCenterId); Assert.Single(preserved.Lines);

            // 4. A stale version never overwrites a newer draft or approves an unseen state.
            var readVersion = ConcurrencyToken.FromBytes(preserved.RowVersion);
            var edited = await salesService.UpdateInvoiceAsync(scenario.CompanyA, scenario.BranchA1, created.Id,
                new UpdateSalesInvoiceCommand(customer.Id, warehouse.Id, CurrencyCode: "SYP", Notes: "النسخة الأحدث", ExchangeRate: 1.23456789m, Lines: createCmd.Lines)
                { ExpectedVersion = readVersion }, Cancellation).ConfigureAwait(true);
            Assert.Equal(1.23456789m, edited.ExchangeRate);
            Assert.False(readVersion.Matches(edited.RowVersion));
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => salesService.UpdateInvoiceAsync(scenario.CompanyA, scenario.BranchA1, created.Id,
                new UpdateSalesInvoiceCommand(customer.Id, warehouse.Id, Notes: "نسخة قديمة", Lines: createCmd.Lines)
                { ExpectedVersion = readVersion }, Cancellation)).ConfigureAwait(true);
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => salesService.ApproveInvoiceAsync(scenario.CompanyA, scenario.BranchA1, created.Id,
                scenario.Actor, readVersion, Cancellation)).ConfigureAwait(true);
            context.ChangeTracker.Clear();
            var kept = await salesService.GetInvoiceAsync(scenario.CompanyA, scenario.BranchA1, created.Id, Cancellation).ConfigureAwait(true);
            Assert.Equal("النسخة الأحدث", kept.Notes); Assert.Equal(SalesInvoiceStatus.Draft, kept.Status);

            // 5. Approve Workflow
            var approved = await salesService.ApproveInvoiceAsync(scenario.CompanyA, scenario.BranchA1, created.Id, scenario.Actor,
                ConcurrencyToken.FromBytes(kept.RowVersion), Cancellation).ConfigureAwait(true);
            Assert.Equal(SalesInvoiceStatus.Approved, approved.Status);
            Assert.NotNull(approved.ApprovedAtUtc);
        }
    }

    [Theory(Explicit = true)]
    [InlineData(AccountingPeriodState.Open, "inventory.negative_stock_blocked")]
    [InlineData(AccountingPeriodState.SoftClosed, "sales.period_closed")]
    [InlineData(AccountingPeriodState.Closed, "sales.period_closed")]
    [Trait("Category", "SqlServer")]
    public async Task SalesInvoiceNegativeStockBlockedWhenInsufficient(AccountingPeriodState periodState, string expectedCode)
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var scenario = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            using var context = database.Context();

            var invService = new SqlInventoryService(context);
            var partiesService = new SqlPartyService(context);
            var patternsService = new SqlPatternService(context);
            var salesService = new SqlSalesInvoiceService(context, patternsService, TimeProvider.System);

            var unit = await invService.CreateUnitAsync(scenario.CompanyA, new CreateUnitCommand("BAG", "كيس", "Bag", "bag", 0), Cancellation).ConfigureAwait(true);
            var warehouse = await invService.CreateWarehouseAsync(scenario.CompanyA, new CreateWarehouseCommand(scenario.BranchA1, "WH-CEMENT", "مستودع الإسمنت", "Cement Yard", null), Cancellation).ConfigureAwait(true);
            var item = await invService.CreateItemAsync(scenario.CompanyA, new CreateItemCommand("CEM-50", "إسمنت بورتلاندي 50كغ", "Portland Cement 50kg", null, unit.Id, warehouse.Id, CostMethod.WeightedAverage, 0m, 500m, 10m), Cancellation).ConfigureAwait(true);

            var customer = await partiesService.CreatePartyAsync(scenario.CompanyA, new CreatePartyCommand(
                Code: "CUST-002",
                NameArabic: "مقاولات الشرق",
                NameEnglish: "Al Sharq Contracting",
                Role: PartyRole.Customer,
                CreditLimit: 0m,
                PaymentTermDays: 30,
                PolicyKind: CreditPolicyKind.NoLimit), Cancellation).ConfigureAwait(true);

            var revAcc2 = await scenario.AccountAsync(scenario.CompanyA, "4111", AccountTypeCode.Revenue, true).ConfigureAwait(true);
            var invAcc2 = await scenario.AccountAsync(scenario.CompanyA, "1141", AccountTypeCode.Assets, true).ConfigureAwait(true);
            var cogsAcc2 = await scenario.AccountAsync(scenario.CompanyA, "5111", AccountTypeCode.Expenses, true).ConfigureAwait(true);
            var scope = PatternScope.Create(scenario.CompanyA, scenario.BranchA1, scenario.Actor);
            var patternCmd = new CreateDocumentPatternCommand
            {
                OperationKind = BusinessOperationKind.SalesInvoice,
                Code = "SINV-2",
                NameArabic = "فاتورة مواد",
                NameEnglish = "Material Invoice",
                ShortCode = "INV",
                Icon = "invoice",
                Group = "Sales",
                IsDefault = true,
                BranchIds = [scenario.BranchA1],
                InitialConfiguration = new PatternConfiguration
                {
                    AccountMappings = [new() { Role = "Revenue", AccountId = revAcc2.Id }, new() { Role = "Inventory", AccountId = invAcc2.Id }, new() { Role = "COGS", AccountId = cogsAcc2.Id }],
                    NumberingPolicy = new PatternNumberingPolicy
                    {
                        Prefix = "INV-",
                        MinimumDigits = 4,
                        Scope = PatternNumberingScope.BranchAndFiscalYear,
                    }
                }
            };
            var pattern = await patternsService.CreatePatternAsync(scope, patternCmd, Cancellation).ConfigureAwait(true);
            var patternRev = await patternsService.PublishRevisionAsync(scope, pattern.Id, new PublishPatternRevisionCommand
            {
                ExpectedVersion = pattern.Version,
                RevisionId = pattern.ActiveRevision!.Id,
                Reason = "Publish initial revision",
            }, Cancellation).ConfigureAwait(true);

            var draft = await salesService.CreateInvoiceAsync(scenario.CompanyA, scenario.BranchA1, scenario.Actor, new CreateSalesInvoiceCommand(
                PatternRevisionId: patternRev.ActiveRevision!.Id,
                CustomerId: customer.Id,
                WarehouseId: warehouse.Id,
                InvoiceDateUtc: DateTimeOffset.UtcNow,
                PaymentMethod: PaymentMethod.Cash,
                CurrencyCode: "USD",
                ExchangeRate: 1.0m,
                CostCenterId: null,
                Notes: null,
                Lines:
                [
                    new CreateSalesInvoiceLineCommand(item.Id, unit.Id, 10m, 5m, 0m, 0m, 16m, 8m, 0m, 0m, null, warehouse.Id, null, null, 1)
                ]), Cancellation).ConfigureAwait(true);

            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE [ledger].[AccountingPeriods] SET [State]={(byte)periodState} WHERE [CompanyId]={scenario.CompanyA}", Cancellation).ConfigureAwait(true);
            await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO [security].[RolePermissions]([RoleId],[PermissionCode]) SELECT [RoleId],N'Sales.Invoices.Post' FROM [security].[UserRoles] WHERE [UserId]={scenario.Actor}", Cancellation).ConfigureAwait(true);
            // Open=0 reaches stock validation; SoftClosed=1 and Closed=2 must reject before any effects.
            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                salesService.PostInvoiceAsync(scenario.CompanyA, scenario.BranchA1, draft.Id, scenario.Actor, new PostSalesInvoiceCommand(Guid.NewGuid().ToString("N"), "Posting attempt"), Cancellation));

            Assert.Equal(expectedCode, ex.Code);
            if (periodState == AccountingPeriodState.Open)
            {
                Assert.Equal("lines", ex.Field);
                Assert.Contains("CEM-50", ex.Message, StringComparison.Ordinal);
                Assert.Contains("إسمنت بورتلاندي 50كغ", ex.Message, StringComparison.Ordinal);
                Assert.Contains("WH-CEMENT", ex.Message, StringComparison.Ordinal);
                Assert.Contains("المطلوب 10", ex.Message, StringComparison.Ordinal);
                Assert.Contains("المتاح 0", ex.Message, StringComparison.Ordinal);
            }
            context.ChangeTracker.Clear();
            Assert.Empty(await context.StockMovements.ToListAsync(Cancellation).ConfigureAwait(true));
            Assert.Equal((int)SalesInvoiceStatus.Draft, (await context.SalesInvoices.SingleAsync(Cancellation).ConfigureAwait(true)).Status);
        }
    }
}
