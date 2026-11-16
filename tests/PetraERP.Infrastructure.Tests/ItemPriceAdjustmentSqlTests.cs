using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Inventory;
using PetraERP.Domain.Inventory;
using PetraERP.Infrastructure.Inventory;
using Xunit;

namespace PetraERP.Infrastructure.Tests;

/// <summary>Real SQL Server check of the bulk price adjustment ("تعديل أسعار بطاقات المواد"). Explicit: needs PETRAERP_SQL_TEST_SERVER.</summary>
public sealed class ItemPriceAdjustmentSqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task AdjustmentPreviewsFirstThenChangesOnlyGeneralPricesOfTheChosenGroupAndType()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var scenario = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            using var context = database.Context();
            var service = new SqlInventoryService(context);
            var piece = await service.CreateUnitAsync(scenario.CompanyA, new CreateUnitCommand("PCS", "قطعة", null, "pc", 0), Cancellation).ConfigureAwait(true);
            var parent = await service.CreateItemGroupAsync(scenario.CompanyA, new CreateItemGroupCommand("G1", "مجموعة", null, null), Cancellation).ConfigureAwait(true);
            var child = await service.CreateItemGroupAsync(scenario.CompanyA, new CreateItemGroupCommand("G11", "فرعية", null, parent.Id), Cancellation).ConfigureAwait(true);
            var other = await service.CreateItemGroupAsync(scenario.CompanyA, new CreateItemGroupCommand("G2", "أخرى", null, null), Cancellation).ConfigureAwait(true);

            async Task<Guid> ItemAsync(string code, Guid group) => (await service.CreateItemAsync(scenario.CompanyA, new CreateItemCommand(
                code, "مادة " + code, null, group, piece.Id, null, CostMethod.WeightedAverage, null, null, null,
                Prices: [new CreateItemPriceCommand(piece.Id, "WHOLESALE", 100m, "USD", 0m), new CreateItemPriceCommand(piece.Id, "RETAIL", 120m, "USD", 0m)]),
                Cancellation).ConfigureAwait(true)).Id;
            var inParent = await ItemAsync("I1", parent.Id).ConfigureAwait(true);
            var inChild = await ItemAsync("I2", child.Id).ConfigureAwait(true);
            var inOther = await ItemAsync("I3", other.Id).ConfigureAwait(true);

            decimal PriceOf(Guid item, string list) => context.ItemPrices.AsNoTracking().Single(p => p.ItemId == item && p.PriceListCode == list).Price;

            // Rejected: zero, too large a drop, bad rounding.
            await Assert.ThrowsAsync<BusinessRuleException>(() => service.AdjustItemPricesAsync(scenario.CompanyA, new(null, null, 0m, 2, true), Cancellation)).ConfigureAwait(true);
            await Assert.ThrowsAsync<BusinessRuleException>(() => service.AdjustItemPricesAsync(scenario.CompanyA, new(null, null, -95m, 2, true), Cancellation)).ConfigureAwait(true);
            await Assert.ThrowsAsync<BusinessRuleException>(() => service.AdjustItemPricesAsync(scenario.CompanyA, new(null, null, 10m, 5, true), Cancellation)).ConfigureAwait(true);

            // A preview counts but stores nothing: the parent group includes its sub-group, not the other group.
            var preview = await service.AdjustItemPricesAsync(scenario.CompanyA, new(parent.Id, "WHOLESALE", 10m, 2, true), Cancellation).ConfigureAwait(true);
            Assert.Equal(new ItemPriceAdjustmentResult(2, 2, false), preview);
            Assert.Equal(100m, PriceOf(inParent, "WHOLESALE"));

            var applied = await service.AdjustItemPricesAsync(scenario.CompanyA, new(parent.Id, "WHOLESALE", 10m, 2, false), Cancellation).ConfigureAwait(true);
            Assert.Equal(new ItemPriceAdjustmentResult(2, 2, true), applied);
            Assert.Equal(110m, PriceOf(inParent, "WHOLESALE"));
            Assert.Equal(110m, PriceOf(inChild, "WHOLESALE"));
            Assert.Equal(100m, PriceOf(inOther, "WHOLESALE"));
            Assert.Equal(120m, PriceOf(inParent, "RETAIL"));

            // All groups, all price types, a drop with rounding to whole numbers.
            var all = await service.AdjustItemPricesAsync(scenario.CompanyA, new(null, null, -50m, 0, false), Cancellation).ConfigureAwait(true);
            Assert.Equal(new ItemPriceAdjustmentResult(3, 6, true), all);
            Assert.Equal(55m, PriceOf(inParent, "WHOLESALE"));
            Assert.Equal(50m, PriceOf(inOther, "WHOLESALE"));
            Assert.Equal(60m, PriceOf(inOther, "RETAIL"));

            await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.AdjustItemPricesAsync(scenario.CompanyA, new(Guid.NewGuid(), null, 5m, 2, true), Cancellation)).ConfigureAwait(true);
        }
    }
}
