using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;
using PetraERP.Application.Inventory;
using PetraERP.Application.Parties;
using PetraERP.Application.Patterns;
using PetraERP.Application.Sales;
using PetraERP.Domain.Inventory;
using PetraERP.Domain.Parties;
using PetraERP.Domain.Patterns;
using PetraERP.Domain.Sales;
using PetraERP.Infrastructure.Inventory;
using PetraERP.Infrastructure.Parties;
using PetraERP.Infrastructure.Patterns;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Sales;
using Xunit;

namespace PetraERP.Infrastructure.Tests;

/// <summary>
/// Real SQL Server evidence for the sales-list sort and navigation contract: a 10,000-row branch with deliberately massive ties
/// (40 distinct dates, 5 distinct created-at values, random uniqueidentifiers) so that only the (date, created-at, id) tie-breaker
/// separates rows. Proves that Guid.CompareTo is translated by EF Core into the same uniqueidentifier ordering SQL Server uses for ORDER BY.
/// </summary>
public sealed class SalesInvoiceNavigationSqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;
    private const int RowCount = 10_000;

    internal sealed record Seed(LedgerScenario Scenario, Guid PatternRevisionId, Guid[] Customers, Guid[] Warehouses);

    internal static Task<Seed> SeedForMeasurementAsync(SqlTestDatabase database, PetraDbContext context) => SeedMasterDataAsync(database, context);

    private static async Task<Seed> SeedMasterDataAsync(SqlTestDatabase database, PetraDbContext context)
    {
        var scenario = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
        var invService = new SqlInventoryService(context);
        var partiesService = new SqlPartyService(context);
        var patternsService = new SqlPatternService(context);
        var warehouses = new List<Guid>();
        foreach (var code in new[] { "WH-A", "WH-B" })
            warehouses.Add((await invService.CreateWarehouseAsync(scenario.CompanyA, new CreateWarehouseCommand(scenario.BranchA1, code, "مستودع " + code, code, null), Cancellation).ConfigureAwait(true)).Id);
        var customers = new List<Guid>();
        foreach (var (code, name) in new[] { ("C-1", "أحمد للمقاولات"), ("C-2", "بشار للتجارة"), ("C-3", "ياسر للإنشاءات") })
            customers.Add((await partiesService.CreatePartyAsync(scenario.CompanyA, new CreatePartyCommand(Code: code, NameArabic: name, NameEnglish: code, Role: PartyRole.Customer, CreditLimit: 0m, PaymentTermDays: 30, PolicyKind: CreditPolicyKind.NoLimit), Cancellation).ConfigureAwait(true)).Id);

        var revAcc = await scenario.AccountAsync(scenario.CompanyA, "4111", AccountTypeCode.Revenue, true).ConfigureAwait(true);
        var invAcc = await scenario.AccountAsync(scenario.CompanyA, "1141", AccountTypeCode.Assets, true).ConfigureAwait(true);
        var cogsAcc = await scenario.AccountAsync(scenario.CompanyA, "5111", AccountTypeCode.Expenses, true).ConfigureAwait(true);
        var scope = PatternScope.Create(scenario.CompanyA, scenario.BranchA1, scenario.Actor);
        var pattern = await patternsService.CreatePatternAsync(scope, new CreateDocumentPatternCommand
        {
            OperationKind = BusinessOperationKind.SalesInvoice, Code = "SNAV", NameArabic = "فاتورة", NameEnglish = "Invoice", ShortCode = "INV", Icon = "invoice",
            Group = "Sales", IsDefault = true, BranchIds = [scenario.BranchA1],
            InitialConfiguration = new PatternConfiguration
            {
                AccountMappings = [new() { Role = "Revenue", AccountId = revAcc.Id }, new() { Role = "Inventory", AccountId = invAcc.Id }, new() { Role = "COGS", AccountId = cogsAcc.Id }],
                NumberingPolicy = new PatternNumberingPolicy { Prefix = "INV-", MinimumDigits = 5, Scope = PatternNumberingScope.BranchAndFiscalYear },
            },
        }, Cancellation).ConfigureAwait(true);
        var published = await patternsService.PublishRevisionAsync(scope, pattern.Id, new PublishPatternRevisionCommand
        { ExpectedVersion = pattern.Version, RevisionId = pattern.ActiveRevision!.Id, Reason = "nav" }, Cancellation).ConfigureAwait(true);
        return new Seed(scenario, published.ActiveRevision!.Id, [.. customers], [.. warehouses]);
    }

    private static async Task InsertRowsAsync(PetraDbContext context, Seed seed, int count)
    {
        var random = new Random(20261028);
        var baseDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var createdValues = Enumerable.Range(0, 5).Select(i => baseDate.AddMinutes(i)).ToArray();
        var currencies = new[] { "USD", "SYP", "EUR" };
        for (var offset = 0; offset < count; offset += 1000)
        {
            var batch = new List<SalesInvoiceRow>();
            for (var i = offset; i < Math.Min(count, offset + 1000); i++)
            {
                var status = random.Next(0, 5);
                batch.Add(new SalesInvoiceRow
                {
                    Id = Guid.NewGuid(),
                    CompanyId = seed.Scenario.CompanyA, BranchId = seed.Scenario.BranchA1,
                    PatternRevisionId = seed.PatternRevisionId,
                    CustomerId = seed.Customers[random.Next(seed.Customers.Length)],
                    WarehouseId = seed.Warehouses[random.Next(seed.Warehouses.Length)],
                    InvoiceNumber = status == 0 ? null : "INV-" + (((i + 1) * 7919) % 100003).ToString("D6", System.Globalization.CultureInfo.InvariantCulture),
                    InvoiceDateUtc = baseDate.AddDays(random.Next(0, 40)),
                    PaymentMethod = random.Next(1, 3),
                    CurrencyCode = currencies[random.Next(currencies.Length)],
                    Status = status,
                    GrandTotal = random.Next(1, 30) * 10m,
                    CreatedAtUtc = createdValues[random.Next(createdValues.Length)],
                    UpdatedAtUtc = baseDate,
                });
            }
            context.SalesInvoices.AddRange(batch);
            await context.SaveChangesAsync(Cancellation).ConfigureAwait(true);
            context.ChangeTracker.Clear();
        }
    }

    private static async Task<List<SalesInvoiceSummaryResult>> ListAllAsync(SqlSalesInvoiceService service, Seed seed, SalesInvoiceSearchQuery filter, int pageSize)
    {
        var all = new List<SalesInvoiceSummaryResult>();
        for (var page = 1; ; page++)
        {
            var result = await service.GetInvoicesAsync(seed.Scenario.CompanyA, seed.Scenario.BranchA1, filter with { Page = page, PageSize = pageSize }, Cancellation).ConfigureAwait(true);
            all.AddRange(result.Items);
            if (all.Count >= result.TotalCount || result.Items.Count == 0) return all;
        }
    }

    private static async Task<List<Guid>> WalkAsync(SqlSalesInvoiceService service, Seed seed, SalesInvoiceSearchQuery filter,
        SalesInvoiceNavigationDirection start, SalesInvoiceNavigationDirection step)
    {
        var ids = new List<Guid>();
        var id = await service.GetNeighborInvoiceIdAsync(seed.Scenario.CompanyA, seed.Scenario.BranchA1, null, start, filter, Cancellation).ConfigureAwait(true);
        while (id is { } current)
        {
            ids.Add(current);
            Assert.True(ids.Count <= RowCount, "navigation does not terminate");
            id = await service.GetNeighborInvoiceIdAsync(seed.Scenario.CompanyA, seed.Scenario.BranchA1, current, step, filter, Cancellation).ConfigureAwait(true);
        }
        return ids;
    }

    private static IComparable Key(SalesInvoiceSummaryResult r, SalesInvoiceSortField field) => field switch
    {
        SalesInvoiceSortField.Date => r.InvoiceDateUtc,
        SalesInvoiceSortField.GrandTotal => r.GrandTotal,
        SalesInvoiceSortField.Status => (int)r.Status,
        SalesInvoiceSortField.PaymentMethod => (int)r.PaymentMethod,
        _ => 0,
    };

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task TenThousandInvoicesSortPageAndNavigateDeterministicallyOnEveryField()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            using var context = database.Context();
            var seed = await SeedMasterDataAsync(database, context).ConfigureAwait(true);
            await InsertRowsAsync(context, seed, RowCount).ConfigureAwait(true);
            var service = new SqlSalesInvoiceService(context, new SqlPatternService(context), TimeProvider.System);
            var noFilter = new SalesInvoiceSearchQuery();
            // A narrow scope (three dates, one status) keeps full previous/next walks affordable while still containing heavy ties.
            var narrow = new SalesInvoiceSearchQuery(Status: SalesInvoiceStatus.Posted,
                FromDateUtc: new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero), ToDateUtc: new DateTimeOffset(2026, 1, 12, 0, 0, 0, TimeSpan.Zero));

            foreach (SalesInvoiceSortField field in Enum.GetValues<SalesInvoiceSortField>())
            foreach (var descending in new[] { false, true })
            {
                var sort = new SalesInvoiceSort(field, descending);
                var label = $"{field}/{(descending ? "desc" : "asc")}";

                // 1. Full 10k result paged over >2 server pages: every row exactly once, key order monotonic across page boundaries.
                var full = await ListAllAsync(service, seed, noFilter with { Sort = sort }, 100).ConfigureAwait(true);
                Assert.True(full.Count == RowCount, $"{label}: expected {RowCount} rows, got {full.Count}");
                Assert.True(full.Select(r => r.Id).Distinct().Count() == RowCount, $"{label}: a row repeated across pages");
                for (var i = 1; i < full.Count; i++)
                {
                    var c = Key(full[i - 1], field).CompareTo(Key(full[i], field));
                    Assert.True(descending ? c >= 0 : c <= 0, $"{label}: key order broken at {i}");
                }

                // 2. A different page size must slice the SAME order (stable pagination).
                var other = await ListAllAsync(service, seed, noFilter with { Sort = sort }, 137).ConfigureAwait(true);
                Assert.True(full.Select(r => r.Id).SequenceEqual(other.Select(r => r.Id)), $"{label}: order differs between page sizes");

                // 3. Navigation agrees with the list order exactly (First -> Next ... and Last -> Previous ...).
                var scopedList = (await ListAllAsync(service, seed, narrow with { Sort = sort }, 100).ConfigureAwait(true)).Select(r => r.Id).ToList();
                Assert.NotEmpty(scopedList);
                var forward = await WalkAsync(service, seed, narrow with { Sort = sort }, SalesInvoiceNavigationDirection.First, SalesInvoiceNavigationDirection.Next).ConfigureAwait(true);
                Assert.True(scopedList.SequenceEqual(forward), $"{label}: First/Next walk differs from list order");
                var backward = await WalkAsync(service, seed, narrow with { Sort = sort }, SalesInvoiceNavigationDirection.Last, SalesInvoiceNavigationDirection.Previous).ConfigureAwait(true);
                Assert.True(scopedList.AsEnumerable().Reverse().SequenceEqual(backward), $"{label}: Last/Previous walk differs from reversed list order");
            }

            // 4. Without a sort the list is newest first and navigation walks the exact reverse (oldest first).
            var defaultList = (await ListAllAsync(service, seed, narrow, 100).ConfigureAwait(true)).Select(r => r.Id).ToList();
            var defaultWalk = await WalkAsync(service, seed, narrow, SalesInvoiceNavigationDirection.First, SalesInvoiceNavigationDirection.Next).ConfigureAwait(true);
            Assert.True(defaultList.AsEnumerable().Reverse().SequenceEqual(defaultWalk), "default navigation is not the reverse of the default list");
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task EqualDateAndCreatedAtRowsAreSeparatedOnlyByTheGuidTieBreaker()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            using var context = database.Context();
            var seed = await SeedMasterDataAsync(database, context).ConfigureAwait(true);
            var instant = new DateTimeOffset(2026, 3, 3, 10, 0, 0, TimeSpan.Zero);
            // Ids chosen so that .NET Guid order and SQL Server uniqueidentifier order DISAGREE (SQL compares the last 6 bytes first).
            var ids = new[]
            {
                Guid.Parse("00000000-0000-0000-0000-000000000002"), Guid.Parse("ffffffff-0000-0000-0000-000000000001"),
                Guid.Parse("00000000-0000-0000-0000-000000000003"), Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Guid.Parse("00000001-0000-0000-0000-000000000000"),
            };
            context.SalesInvoices.AddRange(ids.Select(id => new SalesInvoiceRow
            {
                Id = id, CompanyId = seed.Scenario.CompanyA, BranchId = seed.Scenario.BranchA1, PatternRevisionId = seed.PatternRevisionId,
                CustomerId = seed.Customers[0], WarehouseId = seed.Warehouses[0], InvoiceDateUtc = instant, CreatedAtUtc = instant, UpdatedAtUtc = instant,
                CurrencyCode = "USD", Status = 2, GrandTotal = 100m,
            }));
            await context.SaveChangesAsync(Cancellation).ConfigureAwait(true);
            context.ChangeTracker.Clear();
            var service = new SqlSalesInvoiceService(context, new SqlPatternService(context), TimeProvider.System);

            foreach (var sort in new SalesInvoiceSort?[] { null, new(SalesInvoiceSortField.Date, false), new(SalesInvoiceSortField.GrandTotal, true), new(SalesInvoiceSortField.Customer, false) })
            {
                var filter = new SalesInvoiceSearchQuery { Sort = sort };
                var listed = (await ListAllAsync(service, seed, filter, 2).ConfigureAwait(true)).Select(r => r.Id).ToList();
                Assert.Equal(5, listed.Count);
                var forward = await WalkAsync(service, seed, filter, SalesInvoiceNavigationDirection.First, SalesInvoiceNavigationDirection.Next).ConfigureAwait(true);
                var navOrder = sort is null ? listed.AsEnumerable().Reverse().ToList() : listed;
                Assert.Equal(navOrder, forward);
                Assert.Equal(5, forward.Distinct().Count());
            }
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task TwoClientsCreatingInvoicesConcurrentlyNeverLoseOrDuplicateRowsInTheListOrNavigation()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            using var setup = database.Context();
            var seed = await SeedMasterDataAsync(database, setup).ConfigureAwait(true);
            var invService = new SqlInventoryService(setup);
            var unit = await invService.CreateUnitAsync(seed.Scenario.CompanyA, new CreateUnitCommand("M3", "متر مكعب", "Cubic Meter", "m3", 2), Cancellation).ConfigureAwait(true);
            var item = await invService.CreateItemAsync(seed.Scenario.CompanyA, new CreateItemCommand("CONC-30", "خرسانة", "Concrete", null, unit.Id, seed.Warehouses[0], CostMethod.WeightedAverage, 0m, 1000m, 50m), Cancellation).ConfigureAwait(true);
            CreateSalesInvoiceCommand Command(int n) => new(
                PatternRevisionId: seed.PatternRevisionId, CustomerId: seed.Customers[n % seed.Customers.Length], WarehouseId: seed.Warehouses[0],
                InvoiceDateUtc: new DateTimeOffset(2026, 3, 3, 0, 0, 0, TimeSpan.Zero), PaymentMethod: PaymentMethod.Credit, CurrencyCode: "USD", ExchangeRate: 1m,
                CostCenterId: null, Notes: "c" + n,
                Lines: [new CreateSalesInvoiceLineCommand(ItemId: item.Id, UnitId: unit.Id, Quantity: 1m, UnitPrice: 10m, DiscountPercentage: 0m, DiscountAmount: 0m,
                    TaxPercentage: 0m, TaxAmount: 0m, PumpCost: 0m, TransportCost: 0m, CostCenterId: null, WarehouseId: seed.Warehouses[0], Notes: null, ConcreteGrade: null, DisplayOrder: 1)]);

            async Task<List<Guid>> ClientAsync(int offset)
            {
                using var context = database.Context();
                var service = new SqlSalesInvoiceService(context, new SqlPatternService(context), TimeProvider.System);
                var created = new List<Guid>();
                for (var n = 0; n < 15; n++)
                    created.Add((await service.CreateInvoiceAsync(seed.Scenario.CompanyA, seed.Scenario.BranchA1, seed.Scenario.Actor, Command(offset + n), Cancellation).ConfigureAwait(true)).Id);
                return created;
            }

            var results = await Task.WhenAll(ClientAsync(0), ClientAsync(100)).ConfigureAwait(true);
            var expected = results.SelectMany(r => r).ToHashSet();
            Assert.Equal(30, expected.Count);

            using var reader = database.Context();
            var readService = new SqlSalesInvoiceService(reader, new SqlPatternService(reader), TimeProvider.System);
            foreach (var sort in new SalesInvoiceSort?[] { null, new(SalesInvoiceSortField.Customer, false), new(SalesInvoiceSortField.Date, true) })
            {
                var filter = new SalesInvoiceSearchQuery { Sort = sort };
                var listed = (await ListAllAsync(readService, seed, filter, 7).ConfigureAwait(true)).Select(r => r.Id).ToList();
                Assert.Equal(30, listed.Count);
                Assert.True(expected.SetEquals(listed));
                var walked = await WalkAsync(readService, seed, filter, SalesInvoiceNavigationDirection.First, SalesInvoiceNavigationDirection.Next).ConfigureAwait(true);
                Assert.True(expected.SetEquals(walked) && walked.Count == 30);
            }
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task NavigationHandlesDeletedFilteredOutAndForeignOrigins()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            using var context = database.Context();
            var seed = await SeedMasterDataAsync(database, context).ConfigureAwait(true);
            await InsertRowsAsync(context, seed, 300).ConfigureAwait(true);
            var service = new SqlSalesInvoiceService(context, new SqlPatternService(context), TimeProvider.System);
            var company = seed.Scenario.CompanyA;
            var branch = seed.Scenario.BranchA1;

            // Unknown origin => explicit not-found, never a guessed position.
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.GetNeighborInvoiceIdAsync(company, branch, Guid.NewGuid(), SalesInvoiceNavigationDirection.Next, null, Cancellation)).ConfigureAwait(true);

            // A row removed after the list was loaded (deleted origin) => same explicit outcome.
            var victim = (await ListAllAsync(service, seed, new SalesInvoiceSearchQuery(), 50).ConfigureAwait(true))[10].Id;
            await context.SalesInvoices.Where(i => i.Id == victim).ExecuteDeleteAsync(Cancellation).ConfigureAwait(true);
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.GetNeighborInvoiceIdAsync(company, branch, victim, SalesInvoiceNavigationDirection.Previous, null, Cancellation)).ConfigureAwait(true);

            // Origin outside the filter: it anchors the position, and the neighbour always belongs to the filtered set.
            var postedOnly = new SalesInvoiceSearchQuery(Status: SalesInvoiceStatus.Posted) { Sort = new(SalesInvoiceSortField.GrandTotal, false) };
            var postedIds = (await ListAllAsync(service, seed, postedOnly, 50).ConfigureAwait(true)).Select(r => r.Id).ToHashSet();
            var outside = (await ListAllAsync(service, seed, new SalesInvoiceSearchQuery { Sort = postedOnly.Sort }, 50).ConfigureAwait(true)).First(r => !postedIds.Contains(r.Id)).Id;
            var next = await service.GetNeighborInvoiceIdAsync(company, branch, outside, SalesInvoiceNavigationDirection.Next, postedOnly, Cancellation).ConfigureAwait(true);
            var previous = await service.GetNeighborInvoiceIdAsync(company, branch, outside, SalesInvoiceNavigationDirection.Previous, postedOnly, Cancellation).ConfigureAwait(true);
            Assert.True(next is null || postedIds.Contains(next.Value));
            Assert.True(previous is null || postedIds.Contains(previous.Value));

            // Another branch's/company's invoice is never reachable as an origin.
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.GetNeighborInvoiceIdAsync(seed.Scenario.CompanyB, seed.Scenario.BranchB1, postedIds.First(), SalesInvoiceNavigationDirection.Next, null, Cancellation)).ConfigureAwait(true);
        }
    }
}
