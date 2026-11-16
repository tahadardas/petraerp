using PetraERP.ApiClient;
using PetraERP.Desktop.Inventory;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Tests;

public sealed class ItemWorkstationTests
{
    private static ShellFixture CreateFixture(params string[] permissions)
    {
        var fixture = new ShellFixture();
        fixture.User.Permissions = permissions.Length > 0
            ? permissions
            : ["Inventory.Items.View", "Inventory.Items.Manage", "Inventory.Movements.View", "Inventory.Warehouses.View"];
        return fixture;
    }

    private static ItemDetailDto CreateSampleItem(
        Guid companyId,
        string code = "ITEM-001",
        string nameAr = "مصفاة زيت 116",
        string? nameEn = "Oil Filter 116",
        Guid? groupId = null,
        Guid? baseUnitId = null,
        Guid? defaultWarehouseId = null,
        CostMethod costMethod = CostMethod.WeightedAverage,
        string currentStock = "45.0000",
        bool isActive = true)
    {
        var itemId = Guid.NewGuid();
        return new ItemDetailDto
        {
            Id = itemId,
            CompanyId = companyId,
            GroupId = groupId,
            Code = code,
            NameArabic = nameAr,
            NameEnglish = nameEn,
            BaseUnitId = baseUnitId ?? Guid.NewGuid(),
            DefaultWarehouseId = defaultWarehouseId ?? Guid.NewGuid(),
            CostMethod = costMethod,
            MinStock = "10.0000",
            MaxStock = "100.0000",
            ReorderPoint = "20.0000",
            IsActive = isActive,
            CurrentStock = currentStock,
            Barcodes =
            [
                new ItemBarcodeDto { Id = Guid.NewGuid(), ItemId = itemId, Barcode = "6291041001", UnitCode = "PCS", IsDefault = true }
            ],
            Prices =
            [
                new ItemPriceDto { Id = Guid.NewGuid(), ItemId = itemId, PriceListCode = "RETAIL", Price = "15.5000", CurrencyCode = "JOD", MinQuantity = "1.0000" }
            ],
            Conversions =
            [
                new UnitConversionDto { Id = Guid.NewGuid(), CompanyId = companyId, ItemId = itemId, FromUnitCode = "BOX", ToUnitCode = "PCS", Factor = "12.000000" }
            ],
        };
    }

    [Fact]
    public async Task ItemsViewModelLoadsAndFiltersItems()
    {
        var fixture = CreateFixture();
        var accounting = new AccountingFixture();

        var g1 = new ItemGroupDto { Id = Guid.NewGuid(), Code = "GRP-01", NameArabic = "قطع غيار المحركات" };
        var g2 = new ItemGroupDto { Id = Guid.NewGuid(), Code = "GRP-02", NameArabic = "زيوت ومحروقات" };
        accounting.ItemGroups.AddRange([g1, g2]);

        var u1 = new UnitOfMeasureDto { Id = Guid.NewGuid(), Code = "PCS", NameArabic = "قطعة", Symbol = "قطعة" };
        var u2 = new UnitOfMeasureDto { Id = Guid.NewGuid(), Code = "LTR", NameArabic = "لتر", Symbol = "لتر" };
        accounting.Units.AddRange([u1, u2]);

        var item1 = CreateSampleItem(fixture.Company.Id, "OIL-116", "مصفاة زيت 116", "Oil Filter 116", groupId: g1.Id, baseUnitId: u1.Id);
        var item2 = CreateSampleItem(fixture.Company.Id, "DSL-001", "ديزل صناعي ممتاز", "Industrial Diesel", groupId: g2.Id, baseUnitId: u2.Id);
        var item3 = CreateSampleItem(fixture.Company.Id, "OLD-001", "مادة قديمة متوقفة", "Old Retired Part", groupId: g1.Id, baseUnitId: u1.Id, isActive: false);

        accounting.Items.AddRange([item1, item2, item3]);

        using var shell = fixture.Create(new AdministrationFixture(), accounting: accounting);
        shell.UserName = "admin";
        shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);

        var vm = shell.Pages.Items;
        await shell.NavigateToAsync("items", null).ConfigureAwait(true);

        Assert.Equal(3, vm.Items.Count);
        Assert.Equal(3, vm.VisibleItems.Count);

        // Filter by group
        vm.SelectedGroupId = g2.Id;
        Assert.Single(vm.VisibleItems);
        Assert.Equal("DSL-001", vm.VisibleItems[0].Code);

        // Reset group filter and search by Arabic term
        vm.SelectedGroupId = null;
        vm.Search = "مصفاة";
        Assert.Single(vm.VisibleItems);
        Assert.Equal("OIL-116", vm.VisibleItems[0].Code);

        // Search by code
        vm.Search = "DSL";
        Assert.Single(vm.VisibleItems);
        Assert.Equal("DSL-001", vm.VisibleItems[0].Code);

        // Filter active only
        vm.Search = string.Empty;
        vm.ActiveOnly = true;
        Assert.Equal(2, vm.VisibleItems.Count);
        Assert.DoesNotContain(vm.VisibleItems, i => i.Code == "OLD-001");
    }

    [Fact]
    public async Task ItemsViewModelCanToggleActiveState()
    {
        var fixture = CreateFixture();
        var accounting = new AccountingFixture();
        var item1 = CreateSampleItem(fixture.Company.Id, "OIL-116", "مصفاة زيت 116", isActive: true);
        accounting.Items.Add(item1);

        using var shell = fixture.Create(new AdministrationFixture(), accounting: accounting);
        shell.UserName = "admin";
        shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);

        var vm = shell.Pages.Items;
        await shell.NavigateToAsync("items", null).ConfigureAwait(true);

        vm.SelectedItem = vm.VisibleItems[0];
        Assert.True(vm.SelectedItem.IsActive);

        // Toggle to inactive
        await vm.ToggleActiveCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.False(vm.SelectedItem.IsActive);

        // Toggle back to active
        await vm.ToggleActiveCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.True(vm.SelectedItem.IsActive);
    }

    [Fact]
    public async Task ItemCardViewModelLoadsExistingItemWithAllTabs()
    {
        var fixture = CreateFixture();
        var accounting = new AccountingFixture();

        var unit = new UnitOfMeasureDto { Id = Guid.NewGuid(), Code = "PCS", NameArabic = "قطعة", Symbol = "قطعة" };
        var group = new ItemGroupDto { Id = Guid.NewGuid(), Code = "G-01", NameArabic = "فلاتر ومصفيات" };
        var wh = new WarehouseDto { Id = Guid.NewGuid(), Code = "WH-01", NameArabic = "المستودع الرئيسي" };
        accounting.Units.Add(unit);
        accounting.ItemGroups.Add(group);
        accounting.Warehouses.Add(wh);

        var item = CreateSampleItem(fixture.Company.Id, "ITEM-FLT", "فلتر هواء محرك", "Engine Air Filter",
            groupId: group.Id, baseUnitId: unit.Id, defaultWarehouseId: wh.Id, currentStock: "120.0000");
        accounting.Items.Add(item);

        accounting.StockMovements.Add(new StockMovementDto
        {
            Id = Guid.NewGuid(),
            ItemId = item.Id,
            WarehouseId = wh.Id,
            WarehouseCode = "WH-01",
            MovementKind = StockMovementKind.PurchaseReceipt,
            Quantity = "120.0000",
            UnitCode = "PCS",
            UnitCost = "10.0000",
            TotalCost = "1200.0000",
            MovementDateUtc = DateTimeOffset.UtcNow,
            DocumentNumber = "REC-001",
        });

        using var shell = fixture.Create(new AdministrationFixture(), accounting: accounting);
        shell.UserName = "admin";
        shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);

        await shell.NavigateToAsync("items", new OpenItemCardRequest(item.Id!.Value)).ConfigureAwait(true);

        var doc = shell.Documents.Active;
        Assert.NotNull(doc);
        Assert.IsType<ItemCardViewModel>(doc.Content);

        var card = (ItemCardViewModel)doc.Content;
        Assert.Equal("ITEM-FLT", card.DraftCode);
        Assert.Equal("فلتر هواء محرك", card.DraftNameArabic);
        Assert.Equal("Engine Air Filter", card.DraftNameEnglish);
        Assert.Equal("نشط", card.StatusText);
        Assert.Equal("120.0000 قطعة", card.TotalStockDisplay);

        // Verify Barcodes
        Assert.Single(card.Barcodes);
        Assert.Equal("6291041001", card.Barcodes[0].Barcode);

        // Verify Prices
        Assert.Single(card.Prices);
        Assert.Equal("RETAIL", card.Prices[0].PriceListCode);
        Assert.Equal("15.5000", card.Prices[0].Price);

        // Verify Conversions
        Assert.Single(card.Conversions);
        Assert.Equal("BOX", card.Conversions[0].FromUnitCode);
        Assert.Equal("PCS", card.Conversions[0].ToUnitCode);

        // Verify Movements
        Assert.Single(card.Movements);
        Assert.Equal("REC-001", card.Movements[0].DocumentNumber);
        Assert.Equal(StockMovementKind.PurchaseReceipt, card.Movements[0].MovementKind);
    }

    [Fact]
    public async Task ItemCardViewModelCanCreateNewItemViaDraft()
    {
        var fixture = CreateFixture();
        var accounting = new AccountingFixture();

        var unit = new UnitOfMeasureDto { Id = Guid.NewGuid(), Code = "KG", NameArabic = "كيلوغرام", Symbol = "كغ" };
        var group = new ItemGroupDto { Id = Guid.NewGuid(), Code = "RAW", NameArabic = "مواد أولية" };
        accounting.Units.Add(unit);
        accounting.ItemGroups.Add(group);

        using var shell = fixture.Create(new AdministrationFixture(), accounting: accounting);
        shell.UserName = "admin";
        shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);

        await shell.NavigateToAsync("items", new NewItemCardRequest()).ConfigureAwait(true);

        var doc = shell.Documents.Active;
        Assert.NotNull(doc);
        Assert.IsType<ItemCardViewModel>(doc.Content);

        var card = (ItemCardViewModel)doc.Content;
        Assert.True(card.IsNewMode);

        card.DraftCode = "STEEL-10";
        card.DraftNameArabic = "حديد تسليح 10 ملم";
        card.DraftNameEnglish = "Rebar Steel 10mm";
        card.DraftBaseUnitId = unit.Id;
        card.DraftGroupId = group.Id;
        card.DraftCostMethod = CostMethod.Fifo;
        card.DraftMinStock = "50.0000";
        card.DraftMaxStock = "500.0000";
        card.DraftReorderPoint = "100.0000";

        // Add a barcode
        card.NewBarcodeText = "7891234567";
        card.NewBarcodeUnitId = unit.Id;
        card.NewBarcodeIsDefault = true;
        card.AddBarcodeCommand.Execute(null);
        Assert.Single(card.Barcodes);

        // Add a price
        card.NewPriceListCode = "WHOLESALE";
        card.NewPriceAmount = "680.0000";
        card.NewPriceCurrency = "JOD";
        card.AddPriceCommand.Execute(null);
        Assert.Single(card.Prices);

        Assert.True(card.SaveCardCommand.CanExecute(null));
        await card.SaveCardCommand.ExecuteAsync(null).ConfigureAwait(true);

        // Mode returns to View
        Assert.True(card.IsViewMode);
        Assert.NotNull(card.Item);
        Assert.Equal("STEEL-10", card.Item.Code);
        Assert.Equal(CostMethod.Fifo, card.Item.CostMethod);
        Assert.Single(accounting.Items);
        Assert.Equal("حديد تسليح 10 ملم", accounting.Items[0].NameArabic);
    }

    [Fact]
    public async Task ItemCardViewModelCanEditExistingItemAndSave()
    {
        var fixture = CreateFixture();
        var accounting = new AccountingFixture();

        var unit = new UnitOfMeasureDto { Id = Guid.NewGuid(), Code = "PCS", NameArabic = "قطعة", Symbol = "قطعة" };
        accounting.Units.Add(unit);

        var item = CreateSampleItem(fixture.Company.Id, "PART-99", "قطعة ميكانيكية", baseUnitId: unit.Id);
        accounting.Items.Add(item);

        using var shell = fixture.Create(new AdministrationFixture(), accounting: accounting);
        shell.UserName = "admin";
        shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);

        await shell.NavigateToAsync("items", new OpenItemCardRequest(item.Id!.Value)).ConfigureAwait(true);
        var card = (ItemCardViewModel)shell.Documents.Active.Content!;

        Assert.True(card.IsViewMode);
        Assert.True(card.BeginEditCommand.CanExecute(null));

        card.BeginEditCommand.Execute(null);
        Assert.True(card.IsEditMode);
        Assert.True(card.IsCodeReadOnly);

        // Update fields
        card.DraftNameArabic = "قطعة ميكانيكية معدلة";
        card.DraftMinStock = "25.0000";
        card.DraftMaxStock = "250.0000";

        Assert.True(card.HasUnsavedChanges);
        Assert.True(card.SaveCardCommand.CanExecute(null));

        await card.SaveCardCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.True(card.IsViewMode);
        Assert.Equal("قطعة ميكانيكية معدلة", card.Item!.NameArabic);
        Assert.Equal("قطعة ميكانيكية معدلة", accounting.Items[0].NameArabic);
        Assert.Equal("25.0000", accounting.Items[0].MinStock);
    }

    [Fact]
    public async Task ItemCardViewModelValidatesDraftBeforeSave()
    {
        var fixture = CreateFixture();
        var accounting = new AccountingFixture();

        using var shell = fixture.Create(new AdministrationFixture(), accounting: accounting);
        shell.UserName = "admin";
        shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);

        await shell.NavigateToAsync("items", new NewItemCardRequest()).ConfigureAwait(true);
        var card = (ItemCardViewModel)shell.Documents.Active.Content!;

        // Missing code, arabic name, base unit -> cannot save
        Assert.False(card.CanSaveDraft);

        card.DraftCode = "CODE-1";
        Assert.False(card.CanSaveDraft);

        card.DraftNameArabic = "اسم المادة";
        Assert.False(card.CanSaveDraft); // still missing base unit

        card.DraftBaseUnitId = Guid.NewGuid();
        Assert.True(card.CanSaveDraft); // now valid
    }
}
