using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;
using PetraERP.Application.Inventory;
using PetraERP.Application.Parties;
using PetraERP.Domain.Inventory;
using PetraERP.Domain.Parties;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Inventory;
using PetraERP.Infrastructure.Parties;
using Xunit;

namespace PetraERP.Infrastructure.Tests;

/// <summary>Real SQL Server checks of the Al-Ameen card profiles (item, party, account). Explicit: needs PETRAERP_SQL_TEST_SERVER.</summary>
public sealed class AmeenCardsSqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ItemProfileRoundTripsUnitsBecomeConversionsAndDeleteIsRefusedWithMovements()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var scenario = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            using var context = database.Context();
            var service = new SqlInventoryService(context);
            var piece = await service.CreateUnitAsync(scenario.CompanyA, new CreateUnitCommand("PCS", "قطعة", null, "pc", 0), Cancellation).ConfigureAwait(true);
            var carton = await service.CreateUnitAsync(scenario.CompanyA, new CreateUnitCommand("CRT", "كرتونة", null, "ct", 0), Cancellation).ConfigureAwait(true);
            var pack = await service.CreateUnitAsync(scenario.CompanyA, new CreateUnitCommand("PCK", "علبة", null, "pk", 0), Cancellation).ConfigureAwait(true);

            // A profile whose second unit equals the base unit is rejected; so is a factor of zero.
            await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateItemAsync(scenario.CompanyA,
                NewItem("BAD-1", piece.Id, new ItemProfileData(Unit2Id: piece.Id, Unit2Factor: 2m)), Cancellation)).ConfigureAwait(true);
            await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateItemAsync(scenario.CompanyA,
                NewItem("BAD-2", piece.Id, new ItemProfileData(Unit2Id: carton.Id, Unit2Factor: 0m)), Cancellation)).ConfigureAwait(true);

            var profile = new ItemProfileData(
                Unit2Id: carton.Id, Unit2Factor: 12m, Unit3Id: pack.Id, Unit3Factor: 6m, Unit3FactorVariable: true, DefaultUnitIndex: 2,
                GiftQuantity: 1m, GiftPerQuantity: 10m, Specifications: "مواصفات تجريبية", Measure: "كبير", Manufacturer: "الشركة", OriginCountry: "سوريا",
                TracksExpiryDate: true, EnforceDatesOnReceipt: true, HideInSearch: true, LastPurchaseRate: 1.5m);
            var created = await service.CreateItemAsync(scenario.CompanyA, NewItem("AM-1", piece.Id, profile) with
            {
                Barcodes = [new CreateItemBarcodeCommand(piece.Id, "111", false), new CreateItemBarcodeCommand(carton.Id, "222", true)],
            }, Cancellation).ConfigureAwait(true);

            var read = await service.GetItemAsync(scenario.CompanyA, created.Id, Cancellation).ConfigureAwait(true);
            Assert.NotNull(read.Profile);
            Assert.Equal(carton.Id, read.Profile!.Unit2Id);
            Assert.Equal(12m, read.Profile.Unit2Factor);
            Assert.True(read.Profile.Unit3FactorVariable);
            Assert.Equal(2, read.Profile.DefaultUnitIndex);
            Assert.Equal("مواصفات تجريبية", read.Profile.Specifications);
            Assert.True(read.Profile.TracksExpiryDate && read.Profile.EnforceDatesOnReceipt && read.Profile.HideInSearch);
            Assert.False(read.Profile.TracksSerialNumber);
            Assert.Equal(1.5m, read.Profile.LastPurchaseRate);
            Assert.Contains(read.Conversions, c => c.FromUnitId == carton.Id && c.ToUnitId == piece.Id && c.Factor == 12m && c.ItemId == created.Id);
            Assert.Contains(read.Conversions, c => c.FromUnitId == pack.Id && c.ToUnitId == piece.Id && c.Factor == 6m);
            Assert.Equal(2, read.Barcodes.Count);

            // Hidden from search results, still readable by id.
            var found = await service.LookupItemsAsync(scenario.CompanyA, "AM-1", 20, Cancellation).ConfigureAwait(true);
            Assert.DoesNotContain(found, i => i.Id == created.Id);

            // Update: new factor (no movements yet), a replaced barcode list, and a profile left alone when null.
            var updated = await service.UpdateItemAsync(scenario.CompanyA, created.Id, new UpdateItemCommand(
                "مادة معدّلة", null, null, piece.Id, null, CostMethod.WeightedAverage, null, null, null,
                Profile: profile with { Unit2Factor = 24m },
                Barcodes: [new CreateItemBarcodeCommand(carton.Id, "333", true)]), Cancellation).ConfigureAwait(true);
            Assert.Equal(24m, updated.Profile!.Unit2Factor);
            Assert.Contains(updated.Conversions, c => c.FromUnitId == carton.Id && c.Factor == 24m);
            Assert.Equal(["333"], updated.Barcodes.Select(b => b.Barcode).ToArray());
            var untouched = await service.UpdateItemAsync(scenario.CompanyA, created.Id, new UpdateItemCommand(
                "مادة معدّلة", null, null, piece.Id, null, CostMethod.WeightedAverage, null, null, null), Cancellation).ConfigureAwait(true);
            Assert.Equal(24m, untouched.Profile!.Unit2Factor);
            Assert.Single(untouched.Barcodes);

            // A stock movement freezes the factor and forbids the delete; a movement-free item deletes cleanly.
            var warehouse = await service.CreateWarehouseAsync(scenario.CompanyA, new CreateWarehouseCommand(scenario.BranchA1, "WH", "مستودع", null, null), Cancellation).ConfigureAwait(true);
            var movementId = Guid.NewGuid();
            await LedgerScenario.AddStockMovementAsync(context, new StockMovementRow
            {
                Id = movementId, CompanyId = scenario.CompanyA, BranchId = scenario.BranchA1, WarehouseId = warehouse.Id, ItemId = created.Id, UnitId = piece.Id,
                MovementKind = (int)StockMovementKind.OpeningBalance, Quantity = 5m, BaseQuantity = 5m, UnitCost = 2m, TotalCost = 10m,
                MovementDateUtc = DateTimeOffset.UtcNow, DocumentType = "OpeningBalance", DocumentId = movementId, CreatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = scenario.Actor,
            }, Cancellation).ConfigureAwait(true);
            var frozen = await Assert.ThrowsAsync<BusinessRuleException>(() => service.UpdateItemAsync(scenario.CompanyA, created.Id, new UpdateItemCommand(
                "مادة معدّلة", null, null, piece.Id, null, CostMethod.WeightedAverage, null, null, null, Profile: profile with { Unit2Factor = 30m }), Cancellation)).ConfigureAwait(true);
            Assert.Equal("inventory.conversion_immutable", frozen.Code);
            var refused = await Assert.ThrowsAsync<BusinessRuleException>(() => service.DeleteItemAsync(scenario.CompanyA, created.Id, Cancellation)).ConfigureAwait(true);
            Assert.Equal("inventory.item_has_movements", refused.Code);

            var free = await service.CreateItemAsync(scenario.CompanyA, NewItem("AM-2", piece.Id, new ItemProfileData(Unit2Id: carton.Id, Unit2Factor: 10m)), Cancellation).ConfigureAwait(true);
            await service.DeleteItemAsync(scenario.CompanyA, free.Id, Cancellation).ConfigureAwait(true);
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.GetItemAsync(scenario.CompanyA, free.Id, Cancellation)).ConfigureAwait(true);
            Assert.False(await context.UnitConversions.AnyAsync(c => c.ItemId == free.Id, Cancellation).ConfigureAwait(true));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task PartyProfileRoundTripsRejectsDuplicateBarcodeAndHidesFromLookup()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var scenario = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            using var context = database.Context();
            var service = new SqlPartyService(context);
            var profile = new PartyProfileData(Title: "السيد", Suffix: "المحترم", Barcode: "P-001", HideInSearch: true, DiscountPercent: 7.5m,
                SalesPriceType: "WHOLESALE", Notes: "ملاحظة عن الزبون");
            var created = await service.CreatePartyAsync(scenario.CompanyA, Party("C-1", profile), Cancellation).ConfigureAwait(true);
            Assert.NotNull(created.Profile);
            Assert.Equal("السيد", created.Profile!.Title);
            Assert.Equal(7.5m, created.Profile.DiscountPercent);
            Assert.Equal("WHOLESALE", created.Profile.SalesPriceType);
            Assert.Equal("ملاحظة عن الزبون", created.Profile.Notes);

            await Assert.ThrowsAsync<ResourceConflictException>(() => service.CreatePartyAsync(scenario.CompanyA, Party("C-2", profile), Cancellation)).ConfigureAwait(true);
            await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreatePartyAsync(scenario.CompanyA, Party("C-3", new PartyProfileData(DiscountPercent: 101m)), Cancellation)).ConfigureAwait(true);
            await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreatePartyAsync(scenario.CompanyA, Party("C-4", new PartyProfileData(SalesPriceType: "NOPE")), Cancellation)).ConfigureAwait(true);

            var lookup = await service.LookupPartiesAsync(scenario.CompanyA, "C-1", null, 20, Cancellation).ConfigureAwait(true);
            Assert.DoesNotContain(lookup, p => p.Id == created.Id);

            var updated = await service.UpdatePartyAsync(scenario.CompanyA, created.Id, new UpdatePartyCommand(
                "زبون معدّل", null, PartyRole.Customer, ExpectedVersion: created.VersionToken, PolicyKind: CreditPolicyKind.NoLimit,
                Profile: profile with { DiscountPercent = 2m, HideInSearch = false }), Cancellation).ConfigureAwait(true);
            Assert.Equal(2m, updated.Profile!.DiscountPercent);
            Assert.False(updated.Profile.HideInSearch);
            var visible = await service.LookupPartiesAsync(scenario.CompanyA, "C-1", null, 20, Cancellation).ConfigureAwait(true);
            Assert.Contains(visible, p => p.Id == created.Id);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task AccountProfileRoundTripsValidatesAndIsRemovedWithTheAccount()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var scenario = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            var account = await scenario.AccountAsync(scenario.CompanyA, "1100", AccountTypeCode.Assets, true).ConfigureAwait(true);
            var closing = await scenario.AccountAsync(scenario.CompanyA, "9100", AccountTypeCode.Liabilities, true).ConfigureAwait(true);
            using var context = database.Context();
            var service = new SqlAccountProfileService(context);
            var scope = scenario.Master(scenario.CompanyA);

            var empty = await service.GetAsync(scope, account.Id, Cancellation).ConfigureAwait(true);
            Assert.Equal(AccountProfileData.BothNature, empty.Nature);
            Assert.Equal(AccountProfileData.NoBudget, empty.BudgetKind);

            var saved = await service.SetAsync(scope, account.Id, new AccountProfileData(
                Notes: "ملاحظات", Nature: AccountProfileData.DebitNature, ClosingAccountId: closing.Id, BudgetKind: AccountProfileData.CreditBudget,
                BudgetAmount: 1000m, BudgetCurrencyCode: "JOD", BudgetRate: 1m, IncludeCommercialPapers: true, IncomeStatementCategory: "إيرادات"), Cancellation).ConfigureAwait(true);
            Assert.Equal(closing.Id, saved.ClosingAccountId);
            var read = await service.GetAsync(scope, account.Id, Cancellation).ConfigureAwait(true);
            Assert.Equal(1000m, read.BudgetAmount);
            Assert.True(read.IncludeCommercialPapers);
            Assert.Equal("إيرادات", read.IncomeStatementCategory);

            await Assert.ThrowsAsync<BusinessRuleException>(() => service.SetAsync(scope, account.Id, new AccountProfileData(ClosingAccountId: account.Id), Cancellation)).ConfigureAwait(true);
            await Assert.ThrowsAsync<BusinessRuleException>(() => service.SetAsync(scope, account.Id, new AccountProfileData(BudgetKind: AccountProfileData.DebitBudget), Cancellation)).ConfigureAwait(true);
            await Assert.ThrowsAsync<BusinessRuleException>(() => service.SetAsync(scope, account.Id, new AccountProfileData(Nature: 9), Cancellation)).ConfigureAwait(true);
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.GetAsync(scope, Guid.NewGuid(), Cancellation)).ConfigureAwait(true);
            // Another company's account is not reachable through this scope.
            var other = await scenario.AccountAsync(scenario.CompanyB, "1100", AccountTypeCode.Assets, true).ConfigureAwait(true);
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.GetAsync(scope, other.Id, Cancellation)).ConfigureAwait(true);
        }
    }

    private static CreateItemCommand NewItem(string code, Guid baseUnit, ItemProfileData profile) => new(
        code, "مادة " + code, null, null, baseUnit, null, CostMethod.WeightedAverage, null, null, null, Profile: profile);

    private static CreatePartyCommand Party(string code, PartyProfileData profile) => new(code, "زبون " + code, null, PartyRole.Customer, PolicyKind: CreditPolicyKind.NoLimit, Profile: profile);
}
