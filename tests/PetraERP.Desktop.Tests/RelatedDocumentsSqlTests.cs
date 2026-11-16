using PetraERP.Api.Tests;
using PetraERP.ApiClient;
using PetraERP.Desktop.Administration;
using PetraERP.Desktop.Connection;
using PetraERP.Desktop.GeneralLedger;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Productivity;
using PetraERP.Desktop.Relationships;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Tests;

public sealed class RelatedDocumentsSqlTests
{
    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task SupplierCardReadsAndOpensItsRealPurchaseThroughGeneratedClientApiAndSqlAndReturnsToSameTab()
    {
        var host = await ApiSqlHost.CreateBootstrapWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            using var session = new DesktopSession(new Uri("https://localhost/"), host.Factory.Server.CreateHandler(), host.Factory.Server.CreateHandler());
            await session.LoginAsync(ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword, Cancellation).ConfigureAwait(true);
            session.SelectWorkspace(host.WorkspaceId, host.CompanyId, host.FiscalYearId);
            var company = host.CompanyId;
            var branch = host.BranchId;
            async Task<Guid> AccountAsync(string code, AccountType type) => (await session.Api.CreateAccountAsync(company, branch,
                new() { Code = code, NameArabic = code, AccountType = type, PostingAllowed = true }, Cancellation).ConfigureAwait(true)).Id;
            var inventory = await AccountAsync("1200", AccountType.Assets).ConfigureAwait(true);
            var payable = await AccountAsync("2200", AccountType.Liabilities).ConfigureAwait(true);
            var cash = await AccountAsync("1100", AccountType.Assets).ConfigureAwait(true);
            // Prepare master data through the real generated client and authorized annual HTTP routes.
            var party = await session.Api.CreatePartyAsync(company, branch, new CreatePartyRequest
            { Code = "S-CEMENT", NameArabic = "مورد الإسمنت العربي", Role = PartyRole.Supplier, PayableAccountId = payable }, Cancellation).ConfigureAwait(true);
            var unit = await session.Api.CreateUnitAsync(company, branch, new CreateUnitRequest
            { Code = "KG", NameArabic = "كيلوغرام", Symbol = "kg", DecimalPlaces = 4 }, Cancellation).ConfigureAwait(true);
            var warehouse = await session.Api.CreateWarehouseAsync(company, branch, new CreateWarehouseRequest
            { BranchId = branch, Code = "WH", NameArabic = "مستودع الإسمنت" }, Cancellation).ConfigureAwait(true);
            var item = await session.Api.CreateItemAsync(company, branch, new CreateItemRequest
            { Code = "CEMENT", NameArabic = "إسمنت", BaseUnitId = unit.Id!.Value, DefaultWarehouseId = warehouse.Id!.Value, CostMethod = CostMethod.WeightedAverage }, Cancellation).ConfigureAwait(true);
            var pattern = await session.Api.CreateDocumentPatternAsync(company, branch, new CreateDocumentPatternRequest
            {
                Code = "PI", ShortCode = "PI", NameArabic = "شراء إسمنت", OperationKind = BusinessOperationKind.PurchaseInvoice, BranchIds = [branch],
                InitialConfiguration = new PatternConfigurationDto
                {
                    AccountMappings = [new() { Role = "Inventory", AccountId = inventory }, new() { Role = "Payable", AccountId = payable }, new() { Role = "Cash", AccountId = cash }],
                },
            }, Cancellation).ConfigureAwait(true);
            pattern = await session.Api.PublishPatternRevisionAsync(company, branch, pattern.Id!.Value, new PublishPatternRevisionRequest
            { ExpectedVersion = pattern.Version, RevisionId = pattern.ActiveRevision!.Id!.Value, Reason = "نشر للاختبار" }, Cancellation).ConfigureAwait(true);
            var purchase = await session.Api.CreatePurchaseInvoiceAsync(company, branch, new SavePurchaseInvoiceRequest
            {
                PatternRevisionId = pattern.ActiveRevision!.Id!.Value, SupplierId = party.Id!.Value, WarehouseId = warehouse.Id!.Value,
                InvoiceDateUtc = new DateTimeOffset(2026, 10, 25, 8, 0, 0, TimeSpan.Zero), PaymentMethod = PaymentMethod.Credit,
                CurrencyCode = "JOD", ExchangeRate = "1", SupplierReference = "مرجع المورد", Notes = "مشتريات اختبارية",
                Lines = [new() { ItemId = item.Id!.Value, UnitId = unit.Id!.Value, Quantity = "2.5", UnitCost = "10" }],
            }, Cancellation).ConfigureAwait(true);

            var fixture = new ShellFixture(); var loc = new LocalizationService("ar"); var operation = new DesktopOperation(loc);
            var navigator = new ShellNavigator(); var ledger = new GeneralLedgerClient(session);
            using var admin = new AdministrationViewModel(new AdministrationClient(session), fixture, operation, loc);
            using var journalList = new GeneralLedgerViewModel(ledger, fixture, operation, loc);
            var pages = TestPages.Create(admin, journalList, ledger, fixture, operation, loc, navigator,
                new ChartOfAccountsClient(session), new FiscalCalendarClient(session), new LedgerReportsClient(session), new PatternsClient(session),
                new PartiesClient(session), new InventoryClient(session), new SalesInvoiceClient(session), new SalesDocumentLinksClient(session),
                new TreasuryVouchersClient(session), new PurchaseInvoicesClient(session), new RelatedDocumentsClient(session));
            using var workspace = new WorkspaceViewModel(new WorkspaceClient(session), loc, fixture);
            using var shell = new DesktopViewModel(new DesktopClient(session), fixture, fixture, loc, fixture, new(loc), operation, pages,
                workspace, navigator, new NotificationCenter(loc), TimeProvider.System);
            shell.UserName = ApiSqlHost.AdminUser; shell.Password = ApiSqlHost.AdminPassword;
            await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true); Assert.True(shell.ContextReady);
            await shell.NavigateToAsync("party-card", new OpenPartyCardRequest(party.Id!.Value)).ConfigureAwait(true);
            var card = Assert.IsType<Parties.PartyCardViewModel>(shell.CurrentView);
            await card.PurchaseLinks!.RefreshCommand.ExecuteAsync(null).ConfigureAwait(true);
            var row = Assert.Single(card.PurchaseLinks.Items); Assert.Equal(purchase.Id, row.Id); Assert.Equal("25.0000", row.Amount);
            card.PurchaseLinks.Selected = row; await card.PurchaseLinks.OpenCommand.ExecuteAsync(null).ConfigureAwait(true);
            var purchasePage = Assert.IsType<Purchases.PurchasesViewModel>(shell.CurrentView);
            Assert.Equal(purchase.Id, purchasePage.Current!.Id); Assert.Equal(party.Id!.Value, purchasePage.Current.SupplierId);
            await purchasePage.OpenSupplierCommand.ExecuteAsync(null).ConfigureAwait(true); Assert.Same(card, shell.CurrentView);
            Assert.Single(shell.Documents.Documents, d => ReferenceEquals(d.Content, card));
            Assert.Null(operation.ErrorKey);
        }
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;
}
