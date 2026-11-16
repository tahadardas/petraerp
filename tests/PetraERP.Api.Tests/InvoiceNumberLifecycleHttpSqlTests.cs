using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PetraERP.Application.Security;
using PetraERP.Application.Workspaces;
using PetraERP.Contracts.Inventory;
using PetraERP.Contracts.Ledger;
using PetraERP.Contracts.Parties;
using PetraERP.Contracts.Patterns;
using PetraERP.Contracts.Purchases;
using PetraERP.Contracts.Sales;
using PetraERP.Contracts.Workspaces;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Persistence.Actors;
using PetraERP.Infrastructure.Persistence.Registry;
using PetraERP.Infrastructure.Workspaces;

namespace PetraERP.Api.Tests;

public sealed partial class LedgerManagementHttpSqlTests
{
    [Theory(Explicit = true)]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "SqlServer")]
    public async Task IndependentSalesNumberSurvivesPostingRollbackAndReplaysWithoutDuplicateFinancialEffects(bool failAfterAllocation)
    {
        var fault = new SalesJournalSqlFault();
        var host = await ApiSqlHost.CreateWithWorkspaceAsync(overrides: services =>
            services.AddScoped<IAccountingDbContextFactory>(provider => new SalesFaultAccountingFactory(
                provider.GetRequiredService<IFiscalDatabaseResolver>(), provider.GetRequiredService<IOptions<FiscalDatabaseOptions>>(),
                provider.GetRequiredService<IAccountingActorAuthority>(), provider.GetRequiredService<SystemDbContext>(), fault))).ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            using var client = host.Factory.CreateClient();
            var token = (await ApiSqlHost.LoginAsync(client, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true)).AccessToken;
            var fixture = await PrepareNumberedSalesAsync(host, client, token).ConfigureAwait(true);
            var root = $"/api/v1/companies/{host.CompanyId}/branches/{host.BranchId}";
            const string requestKey = "full-number-lifecycle";
            // The sales command derives its durable allocator identity from the public idempotency key.
            var allocationKey = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"PetraERP.Sales.post:{requestKey}"))[..16]);
            string? explicitlyAllocated = null;
            if (!failAfterAllocation)
            {
                var allocated = await SendAsync<NextNumberResponse>(host, client, HttpMethod.Post,
                    root + $"/patterns/{fixture.PatternId}/next-number?fiscalYearId={host.FiscalYearId}&accountingDate=2026-01-20&allocationKey={allocationKey}", token).ConfigureAwait(true);
                Assert.Equal(HttpStatusCode.OK, allocated.Status);
                explicitlyAllocated = allocated.Body!.FormattedNumber;
            }
            var created = await SendAsync<SalesInvoiceDetailDto>(host, client, HttpMethod.Post, root + "/sales/invoices", token,
                new CreateSalesInvoiceRequest
                {
                    PatternRevisionId = fixture.RevisionId, CustomerId = fixture.CustomerId, WarehouseId = fixture.WarehouseId,
                    InvoiceDateUtc = new DateTimeOffset(2026, 1, 20, 12, 0, 0, TimeSpan.Zero), CurrencyCode = "JOD", ExchangeRate = "1", PaymentMethod = PaymentMethod.Credit,
                    Lines = [new() { ItemId = fixture.ItemId, UnitId = fixture.UnitId, Quantity = "2", UnitPrice = "10", DisplayOrder = 1 }],
                }).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.Created, created.Status);
            var id = created.Body!.Id;
            var path = root + $"/sales/invoices/{id}";
            var approved = await SendAsync<SalesInvoiceDetailDto>(host, client, HttpMethod.Post, path + "/approve", token,
                new ApproveSalesInvoiceRequest { ExpectedVersion = created.Body.Version }).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, approved.Status);
            var postRequest = new PostSalesInvoiceRequest { IdempotencyKey = requestKey, Notes = "دورة اختبار الرقم والقيد والمخزون" };
            if (failAfterAllocation)
            {
                fault.Arm();
                var failure = await SendAsync<SalesInvoiceDetailDto>(host, client, HttpMethod.Post, path + "/post", token, postRequest).ConfigureAwait(true);
                Assert.True(failure.Status == HttpStatusCode.InternalServerError, $"Expected controlled SQL fault (500); got HTTP {(int)failure.Status}, problem code: {failure.Code ?? "<none>"}.");
                Assert.Equal(1, fault.Hits);
                using var annual = host.AnnualContext();
                var kept = await annual.SalesInvoices.AsNoTracking().SingleAsync(i => i.Id == id, Cancellation).ConfigureAwait(true);
                Assert.Equal((int)SalesInvoiceStatus.Approved, kept.Status);
                Assert.Null(kept.InvoiceNumber);
                Assert.Null(kept.JournalEntryId);
                Assert.Equal(0, await annual.StockMovements.CountAsync(m => m.DocumentId == id, Cancellation).ConfigureAwait(true));
                Assert.Equal(1, await annual.JournalEntries.CountAsync(Cancellation).ConfigureAwait(true)); // Only the inbound purchase.
            }
            using var central = host.CreateContext();
            // The reservation commits independently; a failed financial transaction leaves a traceable gap.
            if (failAfterAllocation)
            {
                var reserved = await central.Set<DocumentNumberReservationRow>().AsNoTracking()
                    .SingleAsync(r => r.CompanyId == host.CompanyId && r.AllocationKey == allocationKey, Cancellation).ConfigureAwait(true);
                explicitlyAllocated = reserved.Number;
                var other = await SendAsync<NextNumberResponse>(host, client, HttpMethod.Post,
                    root + $"/patterns/{fixture.PatternId}/next-number?fiscalYearId={host.FiscalYearId}&accountingDate=2026-01-20&allocationKey={Guid.NewGuid()}", token).ConfigureAwait(true);
                Assert.Equal(HttpStatusCode.OK, other.Status);
                Assert.NotEqual(explicitlyAllocated, other.Body!.FormattedNumber);
                Assert.True(other.Body.SequenceNumber > reserved.Sequence);
            }
            var posted = await SendAsync<SalesInvoiceDetailDto>(host, client, HttpMethod.Post, path + "/post", token, postRequest).ConfigureAwait(true);
            Assert.True(posted.Status == HttpStatusCode.OK, $"Expected posted invoice (200); got HTTP {(int)posted.Status}, problem code: {posted.Code ?? "<none>"}.");
            Assert.Equal(SalesInvoiceStatus.Posted, posted.Body!.Status);
            Assert.Equal(explicitlyAllocated, posted.Body.InvoiceNumber);
            var replay = await SendAsync<SalesInvoiceDetailDto>(host, client, HttpMethod.Post, path + "/post", token, postRequest).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, replay.Status);
            Assert.Equal(posted.Body.InvoiceNumber, replay.Body!.InvoiceNumber);
            Assert.Equal(posted.Body.JournalEntryId, replay.Body.JournalEntryId);
            using var verified = host.AnnualContext();
            var journal = await verified.JournalEntries.AsNoTracking().SingleAsync(j => j.Id == posted.Body.JournalEntryId, Cancellation).ConfigureAwait(true);
            Assert.Equal((byte)PetraERP.Domain.GeneralLedger.JournalEntryState.Posted, journal.State);
            var lines = await verified.JournalLines.AsNoTracking().Where(l => l.JournalId == journal.Id).ToListAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(20m, lines.Single(l => l.AccountId == fixture.ReceivableId).BaseDebit);
            Assert.Equal(lines.Sum(l => l.BaseDebit), lines.Sum(l => l.BaseCredit));
            var movement = await verified.StockMovements.AsNoTracking().SingleAsync(m => m.DocumentId == id, Cancellation).ConfigureAwait(true);
            Assert.Equal(-2m, movement.BaseQuantity);
            Assert.Equal(8m, movement.TotalCost); // Purchase receipt: ten units at four JOD; WAC is four.
            Assert.Equal(posted.Body.InvoiceNumber, movement.DocumentNumber);
            Assert.Equal(8m, await verified.StockMovements.Where(m => m.ItemId == fixture.ItemId).SumAsync(m => m.BaseQuantity, Cancellation).ConfigureAwait(true));
            Assert.Equal(2, await verified.JournalEntries.CountAsync(Cancellation).ConfigureAwait(true));
            Assert.Equal(1, await verified.PostingRequests.CountAsync(p => p.JournalId == journal.Id && p.CompletedAtUtc != null, Cancellation).ConfigureAwait(true));
            Assert.Equal(1, await central.Set<DocumentNumberReservationRow>().CountAsync(r => r.CompanyId == host.CompanyId && r.AllocationKey == allocationKey, Cancellation).ConfigureAwait(true));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task InstallationDatabaseManagerWithoutTargetCompanyScopeCannotProvisionYearOrExecuteMigration()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            var foreign = await host.ProvisionAdditionalWorkspaceAsync("FOREIGN", 2026, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)).ConfigureAwait(true);
            await CreateLimitedUserAsync(host, host.CompanyId, host.BranchId, PermissionCatalog.DatabaseManage).ConfigureAwait(true);
            using var client = host.Factory.CreateClient();
            var token = (await ApiSqlHost.LoginAsync(client, "limited", LimitedPassword).ConfigureAwait(true)).AccessToken;
            using var db = host.CreateContext();
            Assert.True(await db.RolePermissions.AnyAsync(r => r.PermissionCode == PermissionCatalog.DatabaseManage
                && db.Roles.Any(role => role.Id == r.RoleId && role.Code == "LIMITED"), Cancellation).ConfigureAwait(true));
            var before = await db.FiscalDatabases.CountAsync(r => r.CompanyId == foreign.CompanyId, Cancellation).ConfigureAwait(true);
            using var yearRequest = ApiSqlHost.Authorized(HttpMethod.Post, "/api/v1/workspaces/fiscal-years", token,
                JsonContent.Create(new CreateFiscalYearWorkspaceRequest
                {
                    CompanyId = foreign.CompanyId, YearNumber = 2027, YearCode = "FY2027", StartDate = new DateOnly(2027, 1, 1), EndDate = new DateOnly(2027, 12, 31),
                    CopyChartOfAccounts = false, CopyWarehouses = false, CopyPatterns = false,
                }, options: ApiJson.Options));
            using var yearResponse = await client.SendAsync(yearRequest, Cancellation).ConfigureAwait(true);
            await ProblemAssertions.AssertProblemAsync(yearResponse, HttpStatusCode.Forbidden, "auth.company_scope_denied").ConfigureAwait(true);
            using var migrationRequest = ApiSqlHost.Authorized(HttpMethod.Post,
                $"/api/v1/workspaces/migration/execute?companyId={foreign.CompanyId}&fiscalYearId={foreign.FiscalYearId}&dryRun=true", token);
            using var migrationResponse = await client.SendAsync(migrationRequest, Cancellation).ConfigureAwait(true);
            await ProblemAssertions.AssertProblemAsync(migrationResponse, HttpStatusCode.Forbidden, "auth.company_scope_denied").ConfigureAwait(true);
            Assert.Equal(before, await db.FiscalDatabases.CountAsync(r => r.CompanyId == foreign.CompanyId, Cancellation).ConfigureAwait(true));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ContinuingFiscalYearHttpResponsePreservesPendingOpeningBalancesAndCreatesNoFinancialHistory()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            using var client = host.Factory.CreateClient();
            var token = (await ApiSqlHost.LoginAsync(client, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true)).AccessToken;
            using var request = ApiSqlHost.Authorized(HttpMethod.Post, "/api/v1/workspaces/fiscal-years", token,
                JsonContent.Create(new CreateFiscalYearWorkspaceRequest
                {
                    CompanyId = host.CompanyId, YearNumber = 2027, YearCode = "FY2027", StartDate = new DateOnly(2027, 1, 1), EndDate = new DateOnly(2027, 12, 31),
                    CopyChartOfAccounts = false, CopyWarehouses = false, CopyPatterns = false,
                }, options: ApiJson.Options));
            using var response = await client.SendAsync(request, Cancellation).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<ProvisioningOperationResponse>(ApiJson.Options, Cancellation).ConfigureAwait(true);
            Assert.NotNull(result);
            await host.Database.RegisterAnnualDatabaseAsync(result.TargetDatabaseName).ConfigureAwait(true);
            Assert.Equal("OpeningBalancesPending", result.ReadinessState);
            Assert.False(string.IsNullOrWhiteSpace(result.Notice));
            using var central = host.CreateContext();
            var registry = await central.FiscalDatabases.AsNoTracking().SingleAsync(r => r.FiscalYearId == result.FiscalYearId, Cancellation).ConfigureAwait(true);
            Assert.Equal(host.CompanyId, registry.CompanyId);
            Assert.Equal(result.ReadinessState, registry.State);
            Assert.False(registry.IsDefault);
            using var listRequest = ApiSqlHost.Authorized(HttpMethod.Get, "/api/v1/me/workspaces", token);
            using var listResponse = await client.SendAsync(listRequest, Cancellation).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
            var list = await listResponse.Content.ReadFromJsonAsync<WorkspaceListResponse>(ApiJson.Options, Cancellation).ConfigureAwait(true);
            Assert.NotNull(list);
            var listed = Assert.Single(list.Items, w => w.FiscalYearId == result.FiscalYearId);
            Assert.Equal("OpeningBalancesPending", listed.State);
            Assert.Equal(registry.Id, listed.WorkspaceId);
            var annualConnection = new SqlConnectionStringBuilder(host.Database.ConnectionString) { InitialCatalog = result.TargetDatabaseName }.ConnectionString;
            using var annual = new PetraDbContext(new DbContextOptionsBuilder<PetraDbContext>()
                .UseSqlServer(annualConnection, sql => sql.UseCompatibilityLevel(host.Database.CompatibilityLevel)).Options);
            Assert.Equal(0, await annual.JournalEntries.CountAsync(Cancellation).ConfigureAwait(true));
            Assert.Equal(0, await annual.StockMovements.CountAsync(Cancellation).ConfigureAwait(true));
            Assert.Equal(0, await annual.SalesInvoices.CountAsync(Cancellation).ConfigureAwait(true));
            Assert.Equal(0, await annual.PurchaseInvoices.CountAsync(Cancellation).ConfigureAwait(true));
            Assert.Equal(0, await annual.Vouchers.CountAsync(Cancellation).ConfigureAwait(true));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task PurchaseApprovalAnnualUpgradePreservesDocumentsAndBlocksUnsafeDowngrade()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            using var client = host.Factory.CreateClient();
            var token = (await ApiSqlHost.LoginAsync(client, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true)).AccessToken;
            await PrepareNumberedSalesAsync(host, client, token).ConfigureAwait(true);
            using var annual = host.AnnualContext();
            var source = await annual.PurchaseInvoices.AsNoTracking().Include(i => i.Lines).SingleAsync(Cancellation).ConfigureAwait(true);
            var root = $"/api/v1/companies/{host.CompanyId}/branches/{host.BranchId}/purchases/invoices";
            var created = await SendAsync<PurchaseDetailDto>(host, client, HttpMethod.Post, root, token,
                new SavePurchaseInvoiceRequest
                {
                    PatternRevisionId = source.PatternRevisionId, SupplierId = source.SupplierId, WarehouseId = source.WarehouseId,
                    InvoiceDateUtc = source.InvoiceDateUtc, CurrencyCode = "JOD", ExchangeRate = "1",
                    Lines = [new() { ItemId = source.Lines.Single().ItemId, UnitId = source.Lines.Single().UnitId, Quantity = "1", UnitCost = "4" }],
                }).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.Created, created.Status);
            var draft = created.Body!;
            var postedVersion = source.RowVersion.ToArray();
            var journalCount = await annual.JournalEntries.CountAsync(Cancellation).ConfigureAwait(true);
            var stockCount = await annual.StockMovements.CountAsync(Cancellation).ConfigureAwait(true);
            using var scope = host.Factory.Services.CreateScope();
            using var migrationContext = await scope.ServiceProvider.GetRequiredService<IAccountingDbContextFactory>()
                .CreateDbContextAsync(host.WorkspaceId, Cancellation).ConfigureAwait(true);
            var migrator = migrationContext.GetService<IMigrator>();
            const string Previous = "20261025120000_CompanyPolicies";
            await migrator.MigrateAsync(Previous, Cancellation).ConfigureAwait(true);
            Assert.Equal(Previous, (await migrationContext.Database.GetAppliedMigrationsAsync(Cancellation).ConfigureAwait(true)).Last());
            await migrationContext.Database.MigrateAsync(Cancellation).ConfigureAwait(true);
            var kept = await annual.PurchaseInvoices.AsNoTracking().SingleAsync(i => i.Id == source.Id, Cancellation).ConfigureAwait(true);
            Assert.Equal(source.InvoiceNumber, kept.InvoiceNumber);
            Assert.Equal(source.JournalEntryId, kept.JournalEntryId);
            Assert.Equal(source.GrandTotal, kept.GrandTotal);
            Assert.Equal(postedVersion, kept.RowVersion);
            var keptDraft = await annual.PurchaseInvoices.AsNoTracking().SingleAsync(i => i.Id == draft.Id, Cancellation).ConfigureAwait(true);
            Assert.Equal((int)PurchaseInvoiceStatus.Draft, keptDraft.Status);
            Assert.Equal(draft.Version, Convert.ToBase64String(keptDraft.RowVersion));
            var invalid = await Assert.ThrowsAsync<SqlException>(() => annual.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [purchases].[Invoices] SET [Status]=2 WHERE [Id]={draft.Id}", Cancellation)).ConfigureAwait(true);
            Assert.Equal(547, invalid.Number);
            Assert.Contains("CK_PurchaseInvoices_Posted", invalid.Message, StringComparison.Ordinal);
            var approved = await SendAsync<PurchaseDetailDto>(host, client, HttpMethod.Post, root + $"/{draft.Id}/approve", token,
                new ApprovePurchaseInvoiceRequest { ExpectedVersion = draft.Version }).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, approved.Status);
            Assert.Equal(PurchaseInvoiceStatus.Approved, approved.Body!.Status);
            Assert.Null(approved.Body.InvoiceNumber);
            Assert.Null(approved.Body.JournalEntryId);
            var blocked = await Assert.ThrowsAsync<SqlException>(() => migrator.MigrateAsync(Previous, Cancellation)).ConfigureAwait(true);
            Assert.Equal(51042, blocked.Number);
            // The refused downgrade stops the annual database at an intermediate schema, so the EF model — which knows the columns the
            // later migrations add — cannot read these tables here. The preservation proof asks the columns every version has.
            var after = await annual.Database.SqlQueryRaw<PreservationProbe>(
                """
                SELECT i.[Status] AS [Status], i.[RowVersion] AS [Version],
                       (SELECT COUNT(*) FROM [ledger].[JournalEntries]) AS [Journals],
                       (SELECT COUNT(*) FROM [inventory].[StockMovements]) AS [Movements]
                FROM [purchases].[Invoices] i
                WHERE i.[Id] = {0}
                """, draft.Id).SingleAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal((int)PurchaseInvoiceStatus.Approved, after.Status);
            Assert.Equal(approved.Body.Version, Convert.ToBase64String(after.Version));
            Assert.Equal(journalCount, after.Journals);
            Assert.Equal(stockCount, after.Movements);
            Assert.Equal("20261025210000_PurchaseApprovalStates", (await migrationContext.Database.GetAppliedMigrationsAsync(Cancellation).ConfigureAwait(true)).Last());
            await migrationContext.Database.MigrateAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(migrationContext.Database.GetMigrations().Last(), (await migrationContext.Database.GetAppliedMigrationsAsync(Cancellation).ConfigureAwait(true)).Last());
        }
    }

    private sealed record NumberedSaleFixture(Guid PatternId, Guid RevisionId, Guid CustomerId, Guid WarehouseId, Guid ItemId, Guid UnitId, Guid ReceivableId, Guid PurchaseRevisionId = default, Guid SupplierId = default);

    /// <summary>Document state read straight from the annual database, valid at every migration point.</summary>
    private sealed record PreservationProbe(int Status, byte[] Version, int Journals, int Movements);

    private static async Task<NumberedSaleFixture> PrepareNumberedSalesAsync(ApiSqlHost host, HttpClient client, string token, string openingStock = "10")
    {
        var root = $"/api/v1/companies/{host.CompanyId}/branches/{host.BranchId}";
        async Task<T> CreateAsync<T>(string path, object payload)
        {
            var response = await SendAsync<T>(host, client, HttpMethod.Post, root + path, token, payload).ConfigureAwait(true);
            Assert.True(response.Status is HttpStatusCode.OK or HttpStatusCode.Created, $"{path}: {response.Status} / {response.Code}");
            return response.Body!;
        }
        async Task<Guid> AccountAsync(string code, AccountType type) => (await CreateAsync<AccountResponse>("/ledger/chart/accounts", new AccountRequest
        {
            Code = code, NameArabic = "حساب اختبار " + code, AccountType = type, PostingAllowed = true,
            CostCenterPolicy = CostCenterPolicy.Optional, CurrencyMode = AccountCurrencyMode.BaseCurrencyOnly,
        }).ConfigureAwait(true)).Id;
        var receivable = await AccountAsync("19001", AccountType.Assets).ConfigureAwait(true);
        var inventory = await AccountAsync("19002", AccountType.Assets).ConfigureAwait(true);
        var payable = await AccountAsync("29001", AccountType.Liabilities).ConfigureAwait(true);
        var revenue = await AccountAsync("49001", AccountType.Revenue).ConfigureAwait(true);
        var cogs = await AccountAsync("59001", AccountType.Expenses).ConfigureAwait(true);
        var unit = await CreateAsync<UnitOfMeasureDto>("/inventory/units", new CreateUnitRequest { Code = "LIFE-EA", NameArabic = "قطعة اختبار", Symbol = "قطعة", DecimalPlaces = 0, Dimension = UnitDimension.Count }).ConfigureAwait(true);
        var warehouse = await CreateAsync<WarehouseDto>("/inventory/warehouses", new CreateWarehouseRequest { BranchId = host.BranchId, Code = "LIFE-WH", NameArabic = "مستودع الاختبار" }).ConfigureAwait(true);
        var item = await CreateAsync<ItemDetailDto>("/inventory/items", new CreateItemRequest { Code = "LIFE-ITEM", NameArabic = "صنف دورة الرقم", BaseUnitId = unit.Id, DefaultWarehouseId = warehouse.Id }).ConfigureAwait(true);
        var customer = await CreateAsync<PartyDetailDto>("/parties", new CreatePartyRequest { Code = "LIFE-CUSTOMER", NameArabic = "عميل الاختبار", Role = PartyRole.Customer, ReceivableAccountId = receivable }).ConfigureAwait(true);
        var supplier = await CreateAsync<PartyDetailDto>("/parties", new CreatePartyRequest { Code = "LIFE-SUPPLIER", NameArabic = "مورد الاختبار", Role = PartyRole.Supplier, PayableAccountId = payable }).ConfigureAwait(true);
        async Task<DocumentPatternResponse> PatternAsync(string code, BusinessOperationKind kind, List<PatternAccountMappingDto> mappings)
        {
            var pattern = await CreateAsync<DocumentPatternResponse>("/patterns", new CreateDocumentPatternRequest
            {
                Code = code, NameArabic = "نمط " + code, ShortCode = code, OperationKind = kind, BranchIds = [host.BranchId],
                InitialConfiguration = new PatternConfigurationDto
                {
                    AccountMappings = mappings,
                    NumberingPolicy = new() { Prefix = code + "-", MinimumDigits = 4, Scope = PatternNumberingScope.BranchAndFiscalYear },
                    ApprovalPolicy = new() { RequiresApproval = true },
                },
            }).ConfigureAwait(true);
            return await CreateAsync<DocumentPatternResponse>($"/patterns/{pattern.Id}/publish", new PublishPatternRevisionRequest
            { ExpectedVersion = pattern.Version, RevisionId = pattern.ActiveRevision!.Id, Reason = "نشر نمط دورة التكامل" }).ConfigureAwait(true);
        }
        var purchasePattern = await PatternAsync("LP", BusinessOperationKind.PurchaseInvoice,
            [new() { Role = "Inventory", AccountId = inventory }, new() { Role = "Payable", AccountId = payable }]).ConfigureAwait(true);
        var salesPattern = await PatternAsync("LS", BusinessOperationKind.SalesInvoice,
            [new() { Role = "Receivable", AccountId = receivable }, new() { Role = "Revenue", AccountId = revenue }, new() { Role = "Inventory", AccountId = inventory }, new() { Role = "COGS", AccountId = cogs }]).ConfigureAwait(true);
        var purchase = await CreateAsync<PurchaseDetailDto>("/purchases/invoices", new SavePurchaseInvoiceRequest
        {
            PatternRevisionId = purchasePattern.ActiveRevision!.Id, SupplierId = supplier.Id, WarehouseId = warehouse.Id,
            InvoiceDateUtc = new DateTimeOffset(2026, 1, 10, 12, 0, 0, TimeSpan.Zero), CurrencyCode = "JOD", ExchangeRate = "1",
            Lines = [new() { ItemId = item.Id, UnitId = unit.Id, Quantity = openingStock, UnitCost = "4" }],
        }).ConfigureAwait(true);
        purchase = await CreateAsync<PurchaseDetailDto>($"/purchases/invoices/{purchase.Id}/approve", new ApprovePurchaseInvoiceRequest { ExpectedVersion = purchase.Version }).ConfigureAwait(true);
        purchase = await CreateAsync<PurchaseDetailDto>($"/purchases/invoices/{purchase.Id}/post", new PostPurchaseInvoiceRequest { ExpectedVersion = purchase.Version, IdempotencyKey = "lifecycle-inbound" }).ConfigureAwait(true);
        Assert.Equal(PurchaseInvoiceStatus.Posted, purchase.Status);
        return new(salesPattern.Id, salesPattern.ActiveRevision!.Id, customer.Id, warehouse.Id, item.Id, unit.Id, receivable, purchasePattern.ActiveRevision!.Id, supplier.Id);
    }

    // Fault injection executes a real SQL THROW after stock was staged, using the production EF transaction.
    private sealed class SalesJournalSqlFault : DbCommandInterceptor
    {
        private bool armed;
        public int Hits { get; private set; }
        public void Arm() => armed = true;
        private void Inspect(DbCommand command)
        {
            if (!armed || !command.CommandText.Contains("INSERT INTO [ledger].[JournalEntries]", StringComparison.Ordinal)) return;
            armed = false;
            Hits++;
            command.CommandText = "THROW 51098, N'Controlled sales journal SQL fault', 1;";
        }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Inspect(command); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Inspect(command); return ValueTask.FromResult(result); }
    }

    private sealed class SalesFaultAccountingFactory(IFiscalDatabaseResolver resolver, IOptions<FiscalDatabaseOptions> options,
        IAccountingActorAuthority authority, SystemDbContext identity, SalesJournalSqlFault fault) : IAccountingDbContextFactory
    {
        public async Task<AccountingDbContext> CreateDbContextAsync(Guid workspaceId, CancellationToken cancellationToken = default)
            => CreateDbContext((await resolver.ResolveAsync(workspaceId, cancellationToken).ConfigureAwait(false)).ConnectionString);
        public async Task<AccountingDbContext> CreateDbContextAsync(Guid companyId, Guid fiscalYearId, CancellationToken cancellationToken = default)
            => CreateDbContext((await resolver.ResolveByCompanyAndYearAsync(companyId, fiscalYearId, cancellationToken).ConfigureAwait(false)).ConnectionString);
        public AccountingDbContext CreateDbContext(string connectionString)
        {
            var context = new AccountingDbContext(new DbContextOptionsBuilder<AccountingDbContext>()
                .UseSqlServer(connectionString, sql => sql.UseCompatibilityLevel(options.Value.CompatibilityLevel).CommandTimeout(options.Value.CommandTimeoutSeconds))
                .AddInterceptors(fault).Options, authority);
            context.BindIdentity(identity);
            return context;
        }
    }
}
