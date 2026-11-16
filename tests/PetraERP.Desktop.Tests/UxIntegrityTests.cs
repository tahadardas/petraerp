#pragma warning disable CA1707
using PetraERP.ApiClient;
using PetraERP.Desktop.Inventory;
using PetraERP.Desktop.Purchases;
using PetraERP.Desktop.Sales;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Tests;

/// <summary>
/// The data-safety contract of the document screens: unsaved changes are detected by value, a unit always belongs to its item,
/// no default is taken just because it is first in a list, and a context link opens the record it names.
/// </summary>
public sealed class UxIntegrityTests
{
    private static readonly string[] Permissions =
    [
        "Sales.Invoices.View", "Sales.Invoices.Manage", "Purchases.Invoices.View", "Purchases.Invoices.Manage",
        "Inventory.Items.View", "Inventory.Movements.View", "Inventory.Warehouses.View", "Parties.Directory.View", "Parties.Directory.Manage",
        "Ledger.CostCenters.View", "Ledger.Accounts.View",
    ];

    private static UnitOfMeasureDto Unit(Guid id, string code, UnitDimension dimension = UnitDimension.Unspecified) =>
        new() { Id = id, Code = code, NameArabic = code, Symbol = code, DecimalPlaces = 4, IsActive = true, Dimension = dimension };

    private static UnitConversionDto Conversion(Guid? item, Guid from, Guid to, string factor) =>
        new() { Id = Guid.NewGuid(), ItemId = item, FromUnitId = from, ToUnitId = to, Factor = factor };

    private static async Task<DesktopViewModel> LoginAsync(ShellFixture fixture, AccountingFixture accounting, PurchaseFixture? purchases = null,
        InventoryDocumentsFixture? inventory = null)
    {
        fixture.User.Permissions = Permissions;
        var shell = fixture.Create(new AdministrationFixture(), accounting: accounting, purchases: purchases, inventoryDocuments: inventory);
        shell.UserName = "admin"; shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
        return shell;
    }

    private static void AddPattern(AccountingFixture accounting, BusinessOperationKind kind, Guid revision, PatternConfigurationDto? configuration = null) =>
        accounting.Patterns.Add(new DocumentPatternResponse
        {
            Id = Guid.NewGuid(), Code = kind.ToString(), NameArabic = "نمط", OperationKind = kind, IsActive = true, IsRunnable = true,
            Version = "AAAAAAAAB9E=", ActiveRevision = new PatternRevisionResponse { Id = revision, RevisionNumber = 1, Configuration = configuration },
        });

    // ---- Unit rule (pure) -------------------------------------------------------------------------------------------------

    [Fact]
    public void ItemUnits_AreTheBaseUnitPlusUnitsDirectlyConvertibleToIt()
    {
        var item = Guid.NewGuid(); var otherItem = Guid.NewGuid();
        var kg = Guid.NewGuid(); var ton = Guid.NewGuid(); var bag = Guid.NewGuid(); var litre = Guid.NewGuid(); var box = Guid.NewGuid(); var gram = Guid.NewGuid();
        UnitOfMeasureDto[] units =
        [
            Unit(kg, "KG", UnitDimension.Mass), Unit(ton, "TON", UnitDimension.Mass), Unit(bag, "BAG"), Unit(litre, "L", UnitDimension.Volume),
            Unit(box, "BOX"), Unit(gram, "G", UnitDimension.Mass),
        ];
        UnitConversionDto[] conversions =
        [
            Conversion(null, ton, kg, "1000"),      // company-wide, same dimension, stored toward the base unit
            Conversion(item, kg, bag, "0.02"),      // item-specific, stored away from the base unit
            Conversion(null, litre, kg, "1"),       // company-wide across dimensions: refused, no density is assumed
            Conversion(otherItem, box, kg, "12"),   // another item's conversion
            Conversion(null, gram, kg, "0"),        // not a positive factor
            Conversion(null, gram, ton, "0.000001"),// does not reach the base unit directly (no chains)
        ];

        var choices = ItemUnitCatalog.Compute(item, kg, conversions, units);

        Assert.True(choices.Resolved);
        Assert.Equal(kg, choices.BaseUnitId);
        Assert.Equal([kg, ton, bag], choices.UnitIds);
        Assert.False(choices.Allows(litre)); Assert.False(choices.Allows(box)); Assert.False(choices.Allows(gram));
    }

    // ---- Sales invoice: unsaved changes by value ---------------------------------------------------------------------------

    private static (AccountingFixture Accounting, Guid InvoiceId, Guid Warehouse, Guid OtherWarehouse, Guid CostCenter) SavedDraft()
    {
        var invoiceId = Guid.NewGuid(); var revision = Guid.NewGuid(); var warehouse = Guid.NewGuid(); var otherWarehouse = Guid.NewGuid();
        var item = Guid.NewGuid(); var unit = Guid.NewGuid(); var costCenter = Guid.NewGuid();
        var accounting = new AccountingFixture
        {
            SalesDetail = new()
            {
                Id = invoiceId, Status = SalesInvoiceStatus.Draft, InvoiceNumber = "SI-0001", Version = "AAAAAAAAB9E=", PatternRevisionId = revision,
                WarehouseId = warehouse, PaymentMethod = PaymentMethod.Cash, CustomerIsCashCustomer = true,
                InvoiceDateUtc = new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero), Notes = "بيان", SubTotal = "200.0000", GrandTotal = "200.0000",
                Lines = [new() { Id = Guid.NewGuid(), ItemId = item, UnitId = unit, Quantity = "2.0000", UnitPrice = "100.0000", LineTotal = "200.0000",
                    WarehouseId = warehouse, DisplayOrder = 1 }],
            },
        };
        AddPattern(accounting, BusinessOperationKind.SalesInvoice, revision);
        accounting.Units.Add(Unit(unit, "PCS"));
        accounting.Items.Add(new ItemDetailDto { Id = item, Code = "I1", NameArabic = "مادة", BaseUnitId = unit, IsActive = true });
        accounting.Warehouses.Add(new WarehouseDto { Id = warehouse, Code = "W1", NameArabic = "مستودع 1", IsActive = true });
        accounting.Warehouses.Add(new WarehouseDto { Id = otherWarehouse, Code = "W2", NameArabic = "مستودع 2", IsActive = true });
        accounting.CostCenters.Add(new CostCenterResponse { Id = costCenter, Code = "CC", NameArabic = "مركز", IsActive = true });
        return (accounting, invoiceId, warehouse, otherWarehouse, costCenter);
    }

    [Fact]
    public async Task SalesInvoice_OpeningASavedDraftForEdit_IsNotAnEdit_AndEveryFieldChangeIsDetectedAndUndoable()
    {
        var (accounting, invoiceId, warehouse, otherWarehouse, costCenter) = SavedDraft();
        using var shell = await LoginAsync(new ShellFixture(), accounting).ConfigureAwait(true);
        await shell.NavigateToAsync("sales-invoice-card", new OpenSalesInvoiceCardRequest(invoiceId)).ConfigureAwait(true);
        var card = Assert.IsType<SalesInvoiceCardViewModel>(shell.Documents.Active!.Content);
        card.BeginEditCommand.Execute(null);
        Assert.True(card.IsEditMode);
        Assert.False(card.HasUnsavedChanges); // loading the stored values is not a change
        var line = card.Lines[0];

        void Detects(Action change, Action undo, string field)
        {
            change();
            Assert.True(card.HasUnsavedChanges, $"{field}: the change was not detected");
            undo();
            Assert.False(card.HasUnsavedChanges, $"{field}: restoring the stored value still looks unsaved");
        }

        Detects(() => line.Quantity = "3", () => line.Quantity = "2", "quantity");
        Detects(() => line.UnitPrice = "150", () => line.UnitPrice = "100.00", "price");
        Detects(() => line.DiscountPercentage = "10", () => line.DiscountPercentage = "0", "line discount");
        Detects(() => line.Notes = "ملاحظة", () => line.Notes = null, "line notes");
        Detects(() => card.DraftInvoiceDate = new DateTime(2026, 10, 6), () => card.DraftInvoiceDate = new DateTime(2026, 10, 5), "date");
        Detects(() => card.DraftWarehouseId = otherWarehouse, () => { card.DraftWarehouseId = warehouse; line.WarehouseId = warehouse; }, "warehouse");
        Detects(() => card.DraftCostCenterId = costCenter, () => { card.DraftCostCenterId = null; line.CostCenterId = null; }, "cost centre");
        Detects(() => card.DraftNotes = "بيان معدل", () => card.DraftNotes = "بيان", "notes");
        Detects(() => card.DraftPaymentMethod = PaymentMethod.Credit, () => card.DraftPaymentMethod = PaymentMethod.Cash, "payment method");

        // A discount row and a second line are changes as well, and removing them is clean again.
        var adjustment = card.AddAdjustment();
        adjustment.Notes = "حسم";
        Assert.True(card.HasUnsavedChanges);
        card.RemoveAdjustment(adjustment);
        Assert.False(card.HasUnsavedChanges);
        Detects(() => card.Lines[1].Notes = "سطر جديد", () => card.Lines[1].Notes = null, "added line");
    }

    [Fact]
    public async Task SalesInvoice_NewDraft_TakesNoWarehouseWhenSeveralExist_AndStaysCleanUntilTheOperatorTypes()
    {
        var (accounting, _, _, _, _) = SavedDraft();
        using var shell = await LoginAsync(new ShellFixture(), accounting).ConfigureAwait(true);
        await shell.NavigateToAsync("sales-invoice-card", new NewSalesInvoiceCardRequest()).ConfigureAwait(true);
        var card = Assert.IsType<SalesInvoiceCardViewModel>(shell.Documents.Active!.Content);

        Assert.True(card.IsNewMode);
        Assert.Equal(2, card.WarehouseLookups.Count);
        Assert.Null(card.DraftWarehouseId); // two warehouses and no pattern default: the operator chooses
        Assert.False(card.HasUnsavedChanges);

        card.DraftWarehouseId = card.WarehouseLookups[1].Id;
        Assert.True(card.HasUnsavedChanges);
    }

    [Fact]
    public async Task SalesInvoice_NewDraft_FillsTheOnlyWarehouse()
    {
        var (accounting, _, warehouse, _, _) = SavedDraft();
        accounting.Warehouses.RemoveAt(1);
        using var shell = await LoginAsync(new ShellFixture(), accounting).ConfigureAwait(true);
        await shell.NavigateToAsync("sales-invoice-card", new NewSalesInvoiceCardRequest()).ConfigureAwait(true);
        var card = Assert.IsType<SalesInvoiceCardViewModel>(shell.Documents.Active!.Content);

        Assert.Equal(warehouse, card.DraftWarehouseId);
        Assert.False(card.HasUnsavedChanges); // a default the system filled is not an edit
    }

    // ---- Sales invoice: the unit follows the item ---------------------------------------------------------------------------

    [Fact]
    public async Task SalesLine_TakesItsItemsBaseUnit_FollowsAReplacedItem_AndNeverBorrowsAnotherUnit()
    {
        var (accounting, _, _, _, _) = SavedDraft();
        var kg = Guid.NewGuid(); var metre = Guid.NewGuid(); var cement = Guid.NewGuid(); var cable = Guid.NewGuid();
        accounting.Units.Insert(0, Unit(Guid.NewGuid(), "FIRST")); // the first company unit must never be picked as a fallback
        accounting.Units.Add(Unit(kg, "KG")); accounting.Units.Add(Unit(metre, "M"));
        accounting.Items.Add(new ItemDetailDto { Id = cement, Code = "CEM", NameArabic = "إسمنت", BaseUnitId = kg, IsActive = true });
        accounting.Items.Add(new ItemDetailDto { Id = cable, Code = "CAB", NameArabic = "كبل", BaseUnitId = metre, IsActive = true });
        using var shell = await LoginAsync(new ShellFixture(), accounting).ConfigureAwait(true);
        await shell.NavigateToAsync("sales-invoice-card", new NewSalesInvoiceCardRequest()).ConfigureAwait(true);
        var card = Assert.IsType<SalesInvoiceCardViewModel>(shell.Documents.Active!.Content);
        var line = card.Lines[0];

        line.ItemId = cement;
        Assert.Equal(kg, line.UnitId);

        line.ItemId = cable; // the kilogram of the previous item must not stay on a cable
        Assert.Equal(metre, line.UnitId);

        line.ItemId = Guid.NewGuid(); // unknown to the lookups and to the server
        Assert.Null(line.UnitId);
        Assert.True(line.IsIncomplete);

        line.ItemId = null; // the unit that was filled for the item leaves with it; only the missing item is reported
        Assert.Null(line.UnitId);
        Assert.Equal("D_LineItemRequired", line.RowErrorCode);
    }

    [Fact]
    public async Task SalesLine_ReportsMissingUnitSetup_WhenTheItemHasNoBaseUnit()
    {
        var (accounting, _, _, _, _) = SavedDraft();
        var orphan = Guid.NewGuid();
        accounting.Items.Add(new ItemDetailDto { Id = orphan, Code = "ORPH", NameArabic = "بلا وحدة", BaseUnitId = null, IsActive = true });
        using var shell = await LoginAsync(new ShellFixture(), accounting).ConfigureAwait(true);
        await shell.NavigateToAsync("sales-invoice-card", new NewSalesInvoiceCardRequest()).ConfigureAwait(true);
        var card = Assert.IsType<SalesInvoiceCardViewModel>(shell.Documents.Active!.Content);

        card.Lines[0].ItemId = orphan;

        Assert.Null(card.Lines[0].UnitId);
        Assert.Equal("D_LineUnitSetupMissing", card.Lines[0].RowErrorCode);
    }

    // ---- Purchase invoice: unit choices, defaults, warehouse link --------------------------------------------------------------

    private static (AccountingFixture Accounting, Guid Item, Guid Kg, Guid Ton, Guid Litre, Guid Warehouse) PurchaseSetup(bool twoWarehouses = false)
    {
        var accounting = new AccountingFixture();
        var item = Guid.NewGuid(); var kg = Guid.NewGuid(); var ton = Guid.NewGuid(); var litre = Guid.NewGuid(); var warehouse = Guid.NewGuid();
        AddPattern(accounting, BusinessOperationKind.PurchaseInvoice, Guid.NewGuid());
        accounting.Parties.Add(new PartyDetailDto { Id = Guid.NewGuid(), Code = "S1", NameArabic = "مورد", Role = PartyRole.Supplier, IsActive = true });
        accounting.Units.Add(Unit(litre, "L", UnitDimension.Volume)); accounting.Units.Add(Unit(kg, "KG", UnitDimension.Mass)); accounting.Units.Add(Unit(ton, "TON", UnitDimension.Mass));
        accounting.UnitConversions.Add(Conversion(null, ton, kg, "1000"));
        accounting.Items.Add(new ItemDetailDto { Id = item, Code = "CEM", NameArabic = "إسمنت", BaseUnitId = kg, IsActive = true });
        accounting.Warehouses.Add(new WarehouseDto { Id = warehouse, Code = "W1", NameArabic = "مستودع 1", IsActive = true });
        if (twoWarehouses) accounting.Warehouses.Add(new WarehouseDto { Id = Guid.NewGuid(), Code = "W2", NameArabic = "مستودع 2", IsActive = true });
        return (accounting, item, kg, ton, litre, warehouse);
    }

    [Fact]
    public async Task PurchaseLine_GetsTheBaseUnit_OffersOnlyTheItemsUnits_AndRefusesAUnitWithoutConversion()
    {
        var (accounting, item, kg, ton, litre, warehouse) = PurchaseSetup();
        using var shell = await LoginAsync(new ShellFixture(), accounting, new PurchaseFixture()).ConfigureAwait(true);
        await shell.NavigateToAsync("purchase-invoices", new NewPurchaseRequest(accounting.Parties[0].Id)).ConfigureAwait(true);
        var page = Assert.IsType<PurchasesViewModel>(shell.CurrentView);
        Assert.Equal(warehouse, page.DraftWarehouseId); // the only warehouse
        var line = page.Lines[0];

        line.ItemId = item;

        Assert.Equal(kg, line.UnitId); // filled, not asked for
        Assert.Equal([kg, ton], line.UnitOptions.Select(o => o.Id)); // litre has no conversion to kilogram
        line.UnitCost = "10";
        Assert.True(line.IsComplete);

        line.UnitId = ton;
        Assert.True(line.IsComplete);

        line.UnitId = litre; // a programmatic or pasted value the picker would not offer
        Assert.True(line.HasDisallowedUnit);
        Assert.Equal("D_LineUnitNotAllowed", line.RowErrorCode);
        Assert.False(page.IsDraftComplete());
        Assert.Contains(page.GetValidationErrors(), error => error.Contains(page.Localization["D_LineUnitNotAllowed"], StringComparison.Ordinal));

        line.ItemId = null; // the unit that was filled for the item leaves with it; only the missing item is reported
        Assert.Null(line.UnitId);
        Assert.Equal("D_LineItemRequired", line.RowErrorCode);
    }

    [Fact]
    public async Task Purchase_NewDraft_TakesNoWarehouseWhenSeveralExist()
    {
        var (accounting, _, _, _, _, _) = PurchaseSetup(twoWarehouses: true);
        using var shell = await LoginAsync(new ShellFixture(), accounting, new PurchaseFixture()).ConfigureAwait(true);
        await shell.NavigateToAsync("purchase-invoices", new NewPurchaseRequest(accounting.Parties[0].Id)).ConfigureAwait(true);
        var page = Assert.IsType<PurchasesViewModel>(shell.CurrentView);

        Assert.Equal(2, page.Warehouses.Count);
        Assert.Null(page.DraftWarehouseId);
        Assert.False(page.OpenWarehouseCommand.CanExecute(null)); // nothing to open yet
    }

    [Fact]
    public async Task Purchase_OpenWarehouse_OpensThatWarehouse_AndTheDraftIsStillThereOnReturn()
    {
        var (accounting, item, kg, _, _, warehouse) = PurchaseSetup();
        var inventory = new InventoryDocumentsFixture();
        using var shell = await LoginAsync(new ShellFixture(), accounting, new PurchaseFixture(), inventory).ConfigureAwait(true);
        await shell.NavigateToAsync("purchase-invoices", new NewPurchaseRequest(accounting.Parties[0].Id)).ConfigureAwait(true);
        var page = Assert.IsType<PurchasesViewModel>(shell.CurrentView);
        page.Lines[0].ItemId = item; page.Lines[0].Quantity = "7"; page.Lines[0].UnitCost = "3";
        Assert.True(page.OpenWarehouseCommand.CanExecute(null));

        await page.OpenWarehouseCommand.ExecuteAsync(null).ConfigureAwait(true);

        var operations = Assert.IsType<InventoryOperationsViewModel>(shell.CurrentView);
        Assert.Equal(warehouse, operations.FilterWarehouseId);
        Assert.Equal(1, operations.WorkspaceTab); // movements of that warehouse, not the general document list
        Assert.Equal(warehouse, inventory.MovementWarehouseQueries[^1]);
        Assert.Equal(warehouse, inventory.BalanceWarehouseQueries[^1]);

        await shell.NavigateToAsync("purchase-invoices", null).ConfigureAwait(true);
        var back = Assert.IsType<PurchasesViewModel>(shell.CurrentView);
        Assert.Same(page, back);
        Assert.True(back.IsEditing);
        Assert.Equal(item, back.Lines[0].ItemId); Assert.Equal(kg, back.Lines[0].UnitId); Assert.Equal("7", back.Lines[0].Quantity);
    }

    // ---- Sales invoice: the unit column -------------------------------------------------------------------------------------

    [Fact]
    public async Task SalesLine_UnitColumnOffersOnlyTheItemsUnits_AndAUnitWithoutConversionBlocksTheLine()
    {
        var (accounting, _, _, _, _) = SavedDraft();
        var kg = Guid.NewGuid(); var ton = Guid.NewGuid(); var litre = Guid.NewGuid(); var cement = Guid.NewGuid();
        accounting.Units.Add(Unit(litre, "L", UnitDimension.Volume)); accounting.Units.Add(Unit(kg, "KG", UnitDimension.Mass)); accounting.Units.Add(Unit(ton, "TON", UnitDimension.Mass));
        accounting.UnitConversions.Add(Conversion(null, ton, kg, "1000"));
        accounting.Items.Add(new ItemDetailDto { Id = cement, Code = "CEM", NameArabic = "إسمنت", BaseUnitId = kg, IsActive = true });
        using var shell = await LoginAsync(new ShellFixture(), accounting).ConfigureAwait(true);
        await shell.NavigateToAsync("sales-invoice-card", new NewSalesInvoiceCardRequest()).ConfigureAwait(true);
        var card = Assert.IsType<SalesInvoiceCardViewModel>(shell.Documents.Active!.Content);
        var line = card.Lines[0];

        line.ItemId = cement;

        Assert.Equal(kg, line.UnitId);
        Assert.Equal([kg, ton], line.UnitOptions.Select(o => o.Id));
        line.UnitPrice = "5";
        Assert.True(line.IsComplete);

        line.UnitId = ton; // sold by the ton: allowed, there is a conversion to the base unit
        Assert.True(line.IsComplete);
        Assert.Equal("TON", line.UnitCode);

        line.UnitId = litre;
        Assert.True(line.HasDisallowedUnit);
        Assert.Equal("D_LineUnitNotAllowed", line.RowErrorCode);
        Assert.False(line.IsComplete);
    }

    // ---- Sales invoice: the invoice's own account ---------------------------------------------------------------------------

    private static (AccountingFixture Accounting, Guid PatternCash, Guid OtherCash, Guid Warehouse, Guid Item) CashAccountSetup(bool overridable)
    {
        var accounting = new AccountingFixture();
        var patternCash = Guid.NewGuid(); var otherCash = Guid.NewGuid(); var warehouse = Guid.NewGuid(); var item = Guid.NewGuid(); var unit = Guid.NewGuid();
        accounting.Accounts.Add(new AccountResponse { Id = patternCash, Code = "1310", NameArabic = "الصندوق الرئيسي", PostingAllowed = true, IsActive = true });
        accounting.Accounts.Add(new AccountResponse { Id = otherCash, Code = "1311", NameArabic = "صندوق الفرع", PostingAllowed = true, IsActive = true });
        AddPattern(accounting, BusinessOperationKind.SalesInvoice, Guid.NewGuid(), new PatternConfigurationDto
        {
            AccountMappings = [new PatternAccountMappingDto { Role = "Cash", AccountId = patternCash, IsRequired = !overridable, AllowOverride = overridable }],
        });
        accounting.Units.Add(Unit(unit, "PCS"));
        accounting.Items.Add(new ItemDetailDto { Id = item, Code = "I1", NameArabic = "مادة", BaseUnitId = unit, IsActive = true });
        accounting.Warehouses.Add(new WarehouseDto { Id = warehouse, Code = "W1", NameArabic = "مستودع", IsActive = true });
        return (accounting, patternCash, otherCash, warehouse, item);
    }

    [Fact]
    public async Task SalesInvoice_ShowsThePatternCashAccount_AndSendsAnotherAccountOnlyWhenThePatternPermitsIt()
    {
        var (accounting, patternCash, otherCash, _, item) = CashAccountSetup(overridable: true);
        using var shell = await LoginAsync(new ShellFixture { Confirm = true }, accounting).ConfigureAwait(true);
        await shell.NavigateToAsync("sales-invoice-card", new NewSalesInvoiceCardRequest()).ConfigureAwait(true);
        var card = Assert.IsType<SalesInvoiceCardViewModel>(shell.Documents.Active!.Content);
        card.DraftPaymentMethod = PaymentMethod.Cash;

        Assert.Equal(patternCash, card.DraftCustomerAccountId); // the pattern's account, not a guess by code or name
        Assert.True(card.CanChangeCustomerAccount);
        card.Lines[0].ItemId = item; card.Lines[0].UnitPrice = "10";

        await card.SaveCardCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Null(accounting.SalesCreateRequests[^1].CustomerAccountId); // the default account is not sent as an override

        await shell.NavigateToAsync("sales-invoice-card", new NewSalesInvoiceCardRequest()).ConfigureAwait(true);
        card = Assert.IsType<SalesInvoiceCardViewModel>(shell.Documents.Active!.Content);
        card.DraftPaymentMethod = PaymentMethod.Cash;
        Assert.False(card.HasUnsavedChanges || card.DraftCustomerAccountId != patternCash);
        card.DraftCustomerAccountId = otherCash;
        Assert.True(card.HasUnsavedChanges);
        card.Lines[0].ItemId = item; card.Lines[0].UnitPrice = "10";

        await card.SaveCardCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal(otherCash, accounting.SalesCreateRequests[^1].CustomerAccountId);
    }

    [Fact]
    public async Task SalesInvoice_AccountIsReadOnly_WhenThePatternEnforcesItsCashAccount()
    {
        var (accounting, patternCash, _, _, _) = CashAccountSetup(overridable: false);
        using var shell = await LoginAsync(new ShellFixture(), accounting).ConfigureAwait(true);
        await shell.NavigateToAsync("sales-invoice-card", new NewSalesInvoiceCardRequest()).ConfigureAwait(true);
        var card = Assert.IsType<SalesInvoiceCardViewModel>(shell.Documents.Active!.Content);
        card.DraftPaymentMethod = PaymentMethod.Cash;

        Assert.Equal(patternCash, card.DraftCustomerAccountId);
        Assert.False(card.CanChangeCustomerAccount);
        Assert.True(card.IsCustomerAccountReadOnly);
    }

    // ---- Party card: the party's own account --------------------------------------------------------------------------------

    [Fact]
    public async Task NewCustomer_IsNotTiedToAnotherCustomersAccount_AndGetsItsOwnAccountOnSave()
    {
        var accounting = new AccountingFixture();
        var heading = Guid.NewGuid(); var otherCustomerAccount = Guid.NewGuid();
        accounting.Accounts.Add(new AccountResponse { Id = heading, Code = "121", NameArabic = "الزبائن", PostingAllowed = false, IsActive = true });
        accounting.Accounts.Add(new AccountResponse { Id = otherCustomerAccount, Code = "121001", NameArabic = "زبون قديم", ParentId = heading, PostingAllowed = true, IsActive = true });
        accounting.AccountPlan = new()
        {
            Receivable = new() { ParentAccountId = heading, ParentCode = "121", ParentNameArabic = "الزبائن", NextAccountCode = "121002", FromCompanyPolicy = true },
            Payable = new() { ParentCode = "221", ParentNameArabic = "الموردون", NextAccountCode = "221001", ParentWillBeCreated = true },
        };
        using var shell = await LoginAsync(new ShellFixture(), accounting).ConfigureAwait(true);
        await shell.NavigateToAsync("party-card", new NewPartyCardRequest()).ConfigureAwait(true);
        var card = Assert.IsType<PetraERP.Desktop.Parties.PartyCardViewModel>(shell.CurrentView);
        card.DraftCode = "C-NEW"; card.DraftNameArabic = "زبون جديد"; card.DraftRole = PartyRole.Customer;

        Assert.Null(card.DraftReceivableAccountId); // never the first account under 121: that one belongs to another customer
        Assert.Contains("121002", card.ReceivableAccountPlanText, StringComparison.Ordinal);
        Assert.Contains("خيارات الشركة", card.ReceivableAccountPlanText, StringComparison.Ordinal);

        await card.SaveCardCommand.ExecuteAsync(null).ConfigureAwait(true);

        var saved = accounting.Parties.Single(p => p.Code == "C-NEW");
        Assert.NotNull(saved.ReceivableAccountId);
        Assert.NotEqual(otherCustomerAccount, saved.ReceivableAccountId);
        Assert.Equal("121002", accounting.Accounts.Single(a => a.Id == saved.ReceivableAccountId).Code);
        Assert.Contains(card.AccountLookups, a => a.Id == saved.ReceivableAccountId); // the card can show the new account
    }
}
