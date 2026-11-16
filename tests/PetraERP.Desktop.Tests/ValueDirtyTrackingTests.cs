#pragma warning disable CA1707
using System.IO;
using PetraERP.ApiClient;
using PetraERP.Desktop.Inventory;
using PetraERP.Desktop.Purchases;
using PetraERP.Desktop.Shell;
using PetraERP.Desktop.Treasury;

namespace PetraERP.Desktop.Tests;

/// <summary>
/// Unsaved changes are detected by value on the purchase, voucher and warehouse-operation screens, as on the sales invoice:
/// opening a document for edit or starting a new one is not an edit, every change is detected, and typing a value back is clean.
/// </summary>
[Collection(WpfApplicationGroup.Name)] // the pilot test renders on the shared WPF Application, so it must not overlap other WPF tests
public sealed class ValueDirtyTrackingTests
{
    private static readonly string[] Permissions =
    [
        "Purchases.Invoices.View", "Purchases.Invoices.Manage", "Treasury.Vouchers.View", "Treasury.Vouchers.Manage",
        "Inventory.Items.View", "Inventory.Movements.View", "Inventory.Warehouses.View", "Inventory.Transfers.Manage", "Inventory.Adjustments.Manage",
        "Inventory.Documents.View", "Parties.Directory.View", "Ledger.CostCenters.View", "Ledger.Accounts.View",
    ];

    private static async Task<DesktopViewModel> LoginAsync(ShellFixture fixture, AccountingFixture accounting, PurchaseFixture? purchases = null,
        InventoryDocumentsFixture? inventory = null)
    {
        fixture.User.Permissions = Permissions;
        var shell = fixture.Create(new AdministrationFixture(), accounting: accounting, purchases: purchases, inventoryDocuments: inventory);
        shell.UserName = "admin"; shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
        return shell;
    }

    private static (AccountingFixture Accounting, Guid Item, Guid Unit, Guid Supplier) Setup()
    {
        var accounting = new AccountingFixture();
        var item = Guid.NewGuid(); var unit = Guid.NewGuid(); var supplier = Guid.NewGuid();
        accounting.Patterns.Add(new DocumentPatternResponse
        {
            Id = Guid.NewGuid(), Code = "PI", NameArabic = "pattern", OperationKind = BusinessOperationKind.PurchaseInvoice, IsActive = true, IsRunnable = true,
            Version = "AAAAAAAAB9E=", ActiveRevision = new PatternRevisionResponse { Id = Guid.NewGuid(), RevisionNumber = 1 },
        });
        accounting.Parties.Add(new PartyDetailDto { Id = supplier, Code = "S1", NameArabic = "supplier", Role = PartyRole.Supplier, IsActive = true });
        accounting.Units.Add(new UnitOfMeasureDto { Id = unit, Code = "PCS", NameArabic = "PCS", Symbol = "PCS", DecimalPlaces = 4, IsActive = true });
        accounting.Items.Add(new ItemDetailDto { Id = item, Code = "I1", NameArabic = "item", BaseUnitId = unit, IsActive = true });
        accounting.Warehouses.Add(new WarehouseDto { Id = Guid.NewGuid(), Code = "W1", NameArabic = "w1", IsActive = true });
        return (accounting, item, unit, supplier);
    }

    // ---- Purchases ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Purchase_NewDraftIsClean_EveryChangeIsDetected_AndTypingTheValueBackIsClean()
    {
        var (accounting, item, _, supplier) = Setup();
        using var shell = await LoginAsync(new ShellFixture(), accounting, new PurchaseFixture()).ConfigureAwait(true);
        await shell.NavigateToAsync("purchase-invoices", new NewPurchaseRequest(supplier)).ConfigureAwait(true);
        var page = Assert.IsType<PurchasesViewModel>(shell.CurrentView);
        Assert.True(page.IsEditing);
        Assert.False(page.HasUnsavedChanges); // the system's own defaults (pattern, warehouse, currency) are not the operator's edit

        void Detects(Action change, Action undo, string field)
        {
            change();
            Assert.True(page.HasUnsavedChanges, $"{field}: the change was not detected");
            undo();
            Assert.False(page.HasUnsavedChanges, $"{field}: restoring the stored value still looks unsaved");
        }

        Detects(() => page.DraftNotes = "memo", () => page.DraftNotes = null, "notes");
        Detects(() => page.DraftSupplierReference = "REF-1", () => page.DraftSupplierReference = null, "supplier reference");
        Detects(() => page.ManualDocumentNumber = "77", () => page.ManualDocumentNumber = string.Empty, "manual number");
        Detects(() => page.DraftSellerText = "5", () => page.DraftSellerText = string.Empty, "seller");
        Detects(() => page.Lines[0].ItemId = item, () => { page.Lines[0].ItemId = null; page.Lines[0].Quantity = "0"; }, "item"); // choosing an item also proposes a quantity

        var line = page.Lines[0];
        line.ItemId = item; line.Quantity = "3";
        Assert.True(page.HasUnsavedChanges);
        line.Quantity = "3.0000"; // the same number, written differently
        Assert.True(page.HasUnsavedChanges);
        line.ItemId = null; line.Quantity = "0"; // the row is blank again
        Assert.False(page.HasUnsavedChanges);
    }

    [Fact]
    public async Task Purchase_ATypedReversalReasonCountsAsUnsavedInput()
    {
        var (accounting, _, _, supplier) = Setup();
        using var shell = await LoginAsync(new ShellFixture(), accounting, new PurchaseFixture()).ConfigureAwait(true);
        await shell.NavigateToAsync("purchase-invoices", new NewPurchaseRequest(supplier)).ConfigureAwait(true);
        var page = Assert.IsType<PurchasesViewModel>(shell.CurrentView);

        page.ReverseReason = "wrong supplier";
        Assert.True(page.HasUnsavedChanges);
        page.ReverseReason = string.Empty;
        Assert.False(page.HasUnsavedChanges);
        page.ReturnReason = "damaged";
        Assert.True(page.HasUnsavedChanges);
        page.ReturnReason = " ";
        Assert.False(page.HasUnsavedChanges);
    }

    [Fact]
    public async Task Purchase_OpeningASavedDraftForEdit_IsNotAnEdit_AndAChangedQuantityIsDetectedAndUndoable()
    {
        var (accounting, item, unit, supplier) = Setup();
        var warehouse = accounting.Warehouses[0].Id;
        var purchases = new PurchaseFixture();
        var invoiceId = Guid.NewGuid();
        purchases.Details[invoiceId] = new PurchaseDetailDto
        {
            Id = invoiceId, Status = PurchaseInvoiceStatus.Draft, InvoiceNumber = "PI-0001", Version = "AAAAAAAAB9E=", SupplierId = supplier, WarehouseId = warehouse,
            PatternRevisionId = accounting.Patterns[0].ActiveRevision!.Id, PaymentMethod = PaymentMethod.Credit,
            InvoiceDateUtc = new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero),
            Lines = [new() { Id = Guid.NewGuid(), ItemId = item, UnitId = unit, Quantity = "2.0000", UnitCost = "10.0000", DiscountAmount = "0.0000", TaxAmount = "0.0000" }],
        };
        using var shell = await LoginAsync(new ShellFixture(), accounting, purchases).ConfigureAwait(true);
        await shell.NavigateToAsync("purchase-invoices", new OpenPurchaseRequest(invoiceId)).ConfigureAwait(true);
        var page = Assert.IsType<PurchasesViewModel>(shell.CurrentView);
        Assert.False(page.IsEditing);
        Assert.False(page.HasUnsavedChanges); // a document only viewed is never unsaved

        page.EditCommand.Execute(null);

        Assert.True(page.IsEditing);
        Assert.False(page.HasUnsavedChanges); // entering edit mode is not an edit
        page.Lines[0].Quantity = "3";
        Assert.True(page.HasUnsavedChanges);
        page.Lines[0].Quantity = "2.00";
        Assert.False(page.HasUnsavedChanges); // the stored quantity written another way
        page.DraftNotes = "x";
        Assert.True(page.HasUnsavedChanges);
    }

    // ---- Vouchers ----------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Voucher_NewDraftIsClean_ChangesAreDetected_AndTheDocumentCommandsStayClosedWhileEditing()
    {
        var (accounting, _, _, _) = Setup();
        using var shell = await LoginAsync(new ShellFixture(), accounting).ConfigureAwait(true);
        await shell.NavigateToAsync("treasury-vouchers", new NewVoucherRequest(VoucherKind.Receipt)).ConfigureAwait(true);
        var page = Assert.IsType<VouchersViewModel>(shell.CurrentView);
        Assert.True(page.IsEditing);
        Assert.False(page.HasUnsavedChanges);
        Assert.False(page.PostCommand.CanExecute(null)); // an open edit still gates posting, changed or not
        Assert.True(page.CancelCommand.CanExecute(null)); // a clean edit can still be left

        void Detects(Action change, Action undo, string field)
        {
            change();
            Assert.True(page.HasUnsavedChanges, $"{field}: the change was not detected");
            undo();
            Assert.False(page.HasUnsavedChanges, $"{field}: restoring the stored value still looks unsaved");
        }

        Detects(() => page.DraftAmount = "125", () => page.DraftAmount = string.Empty, "amount");
        Detects(() => page.DraftNotes = "memo", () => page.DraftNotes = null, "notes");
        Detects(() => page.DraftSourceNumber = "A-9", () => page.DraftSourceNumber = null, "source number");
        Detects(() => page.ManualDocumentNumber = "12", () => page.ManualDocumentNumber = string.Empty, "manual number");
        var originalCurrency = page.DraftCurrency;
        Detects(() => page.DraftCurrency = "USD", () => page.DraftCurrency = originalCurrency, "currency");
        Detects(() => page.ReverseReason = "why", () => page.ReverseReason = string.Empty, "reversal reason");
    }

    // ---- Warehouse operations ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task WarehouseOperation_NewDocumentIsClean_ChangesAreDetected_AndNothingIsOfferedForSavingWhileClean()
    {
        var (accounting, item, unit, _) = Setup();
        using var shell = await LoginAsync(new ShellFixture(), accounting, inventory: new InventoryDocumentsFixture()).ConfigureAwait(true);
        await shell.NavigateToAsync("inventory-operations", null).ConfigureAwait(true);
        var page = Assert.IsType<InventoryOperationsViewModel>(shell.CurrentView);
        await page.NewCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.True(page.IsEditing);
        Assert.False(page.HasUnsavedChanges);
        Assert.False(page.CanSaveBeforeClose);

        void Detects(Action change, Action undo, string field)
        {
            change();
            Assert.True(page.HasUnsavedChanges, $"{field}: the change was not detected");
            undo();
            Assert.False(page.HasUnsavedChanges, $"{field}: restoring the stored value still looks unsaved");
        }

        Detects(() => page.Reason = "stock take", () => page.Reason = string.Empty, "reason");
        var warehouse = accounting.Warehouses[0].Id;
        Detects(() => page.WarehouseId = warehouse, () => page.WarehouseId = null, "warehouse");
        Detects(() => page.Lines[0].ItemId = item, () => page.Lines[0].ItemId = null, "line item");
        Detects(() => page.Lines[0].Quantity = "5", () => page.Lines[0].Quantity = "1.0000", "line quantity");
        Detects(() => page.Lines[0].UnitId = unit, () => page.Lines[0].UnitId = null, "line unit");
    }

    // ---- Window unification pilot: warehouse operations on the shared command contract ----------------------------------------

    [Fact]
    public async Task WarehouseOperation_CommandBarFollowsTheDocumentState_AndRendersThePilotScreen()
    {
        var (accounting, _, _, _) = Setup();
        var shell = await LoginAsync(new ShellFixture(), accounting, inventory: new InventoryDocumentsFixture()).ConfigureAwait(true);
        await shell.NavigateToAsync("inventory-operations", null).ConfigureAwait(true);
        var page = Assert.IsType<InventoryOperationsViewModel>(shell.CurrentView);
        var screen = (PetraERP.Desktop.Controls.IDocumentScreen)page;
        string Visible() => string.Join(",", PetraERP.Desktop.Controls.DocumentCommandLayout.Visible(screen));

        Assert.Equal("New", Visible());
        await page.NewCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal(PetraERP.Desktop.Controls.DocumentLifecycle.New, screen.Lifecycle);
        Assert.Equal("Save,Cancel", Visible());
        Assert.True(screen.SaveCommand!.CanExecute(null) || !page.SaveCommand.CanExecute(null)); // contract names the real command
        Assert.Same(page.SaveCommand, screen.SaveCommand);
        Assert.Same(page.PostCommand, screen.PostCommand);
        Assert.Null(screen.DeleteCommand);
        Assert.Null(screen.PreviousCommand); // list paging is not document navigation

        var output = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".artifacts", "unification-pilot");
        Directory.CreateDirectory(output);
        await PetraERP.Desktop.Tests.WpfApplicationHost.RunAsync(async () =>
        {
            PetraERP.Desktop.Tests.WpfApplicationHost.MergeThemes("Themes/Light.xaml", "Themes/Dense.xaml", "Themes/Components.xaml");
            try
            {
                void Render(string name)
                {
                    // Laid out without a Window so the shared Application never gets a MainWindow from this test.
                    var view = new InventoryOperationsView { DataContext = page, FlowDirection = System.Windows.FlowDirection.RightToLeft };
                    var size = new System.Windows.Size(1366, 768);
                    view.Measure(size); view.Arrange(new System.Windows.Rect(size)); view.UpdateLayout();
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(static () => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                    view.UpdateLayout();
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(1366, 768, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); bitmap.Render(view);
                    using var stream = File.Create(Path.Combine(output, name));
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap)); encoder.Save(stream);
                    view.DataContext = null;
                }
                Render("warehouse-new-editing.png");
            }
            finally { PetraERP.Desktop.Tests.WpfApplicationHost.ResetResources(); }
            // The view bound these collections on the WPF thread, so cancel and release the shell there as well.
            await page.CancelCommand.ExecuteAsync(null).ConfigureAwait(true);
            Assert.Equal("New", Visible());
            shell.Dispose();
        }, TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken).ConfigureAwait(true);
    }
}
