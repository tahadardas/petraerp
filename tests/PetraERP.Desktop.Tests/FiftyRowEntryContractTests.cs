#pragma warning disable CA1707
using PetraERP.ApiClient;
using PetraERP.Desktop.Controls;
using PetraERP.Desktop.GeneralLedger;
using PetraERP.Desktop.Purchases;
using PetraERP.Desktop.Sales;
using PetraERP.Desktop.Shell;
using PetraERP.Desktop.Treasury;

namespace PetraERP.Desktop.Tests;

public sealed class FiftyRowEntryContractTests
{
    private static ShellFixture CreateFixture(params string[] permissions)
    {
        var fixture = new ShellFixture();
        fixture.User.Permissions = permissions.Length > 0
            ? permissions
            : [
                "Sales.Invoices.View", "Sales.Invoices.Manage", "Sales.Invoices.Approve", "Sales.Invoices.Post", "Sales.Invoices.Reverse",
                "Purchases.Invoices.View", "Purchases.Invoices.Manage", "Purchases.Invoices.Approve", "Purchases.Invoices.Post", "Purchases.Invoices.Return",
                "Treasury.Vouchers.View", "Treasury.Vouchers.Manage", "Treasury.Vouchers.Approve", "Treasury.Vouchers.Post", "Treasury.Vouchers.Reverse", "Treasury.Vouchers.Allocate",
                "Ledger.Journals.View", "Ledger.Journals.Manage", "Ledger.Accounts.View", "Ledger.CostCenters.View",
                "Inventory.Items.View", "Parties.Directory.View"
            ];
        return fixture;
    }

    [Fact]
    public async Task NewSalesInvoice_Has50BlankSlots_SavesExactlyCompletedRows_ZeroPhantomRows()
    {
        var fixture = CreateFixture();
        var accounting = new AccountingFixture();
        var customerId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var patternRevId = Guid.NewGuid();
        var itemId1 = Guid.NewGuid();
        var itemId2 = Guid.NewGuid();
        var itemId3 = Guid.NewGuid();
        var unitId = Guid.NewGuid();

        accounting.Parties.Add(new PartyDetailDto { Id = customerId, Code = "C001", NameArabic = "عميل 1", Role = PartyRole.Customer, IsActive = true });
        accounting.Patterns.Add(new DocumentPatternResponse
        {
            Id = Guid.NewGuid(), Code = "SI", NameArabic = "فاتورة مبيعات", OperationKind = BusinessOperationKind.SalesInvoice, IsActive = true, IsRunnable = true,
            ActiveRevision = new PatternRevisionResponse { Id = patternRevId, RevisionNumber = 1 }
        });
        accounting.Items.Add(new ItemDetailDto { Id = itemId1, Code = "ITEM-1", NameArabic = "مادة 1", IsActive = true });
        accounting.Items.Add(new ItemDetailDto { Id = itemId2, Code = "ITEM-2", NameArabic = "مادة 2", IsActive = true });
        accounting.Items.Add(new ItemDetailDto { Id = itemId3, Code = "ITEM-3", NameArabic = "مادة 3", IsActive = true });

        using var shell = fixture.Create(new AdministrationFixture(), accounting: accounting);
        shell.UserName = "admin"; shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);

        await shell.NavigateToAsync("sales-invoice-card", new NewSalesInvoiceCardRequest()).ConfigureAwait(true);
        var card = Assert.IsType<SalesInvoiceCardViewModel>(shell.Documents.Active!.Content);

        // 1. Assert exactly 50 blank slots initialized
        Assert.Equal(50, card.Lines.Count);
        Assert.All(card.Lines, line =>
        {
            Assert.True(line.IsPristineBlank);
            Assert.False(line.IsComplete);
            Assert.False(line.IsIncomplete);
            Assert.Equal(DocumentRowState.PristineBlank, line.State);
        });

        // 2. Untouched blank slots produce no dirty state or totals
        Assert.False(card.HasUnsavedChanges);
        Assert.Equal("0.0000", card.SubTotalDisplay);
        Assert.Equal("0.0000", card.GrandTotalDisplay);

        // 3. Complete 3 rows
        card.DraftCustomerId = customerId;
        card.DraftWarehouseId = warehouseId;
        card.DraftPatternRevisionId = patternRevId;

        card.Lines[0].ItemId = itemId1;
        card.Lines[0].UnitId = unitId;
        card.Lines[0].Quantity = "2";
        card.Lines[0].UnitPrice = "100";
        card.Lines[0].Recalculate();

        card.Lines[1].ItemId = itemId2;
        card.Lines[1].UnitId = unitId;
        card.Lines[1].Quantity = "5";
        card.Lines[1].UnitPrice = "50";
        card.Lines[1].Recalculate();

        card.Lines[2].ItemId = itemId3;
        card.Lines[2].UnitId = unitId;
        card.Lines[2].Quantity = "1";
        card.Lines[2].UnitPrice = "200";
        card.Lines[2].Recalculate();

        card.RecalculateTotals();

        Assert.True(card.Lines[0].IsComplete);
        Assert.True(card.Lines[1].IsComplete);
        Assert.True(card.Lines[2].IsComplete);
        Assert.True(card.HasUnsavedChanges);
        Assert.True(card.SaveCardCommand.CanExecute(null));

        // SubTotal = 2*100 + 5*50 + 1*200 = 200 + 250 + 200 = 650
        Assert.Equal("650.0000", card.SubTotalDisplay);
        Assert.Equal("650.0000", card.GrandTotalDisplay);

        // 4. Save draft
        await card.SaveCardCommand.ExecuteAsync(null).ConfigureAwait(true);

        // Assert exactly 3 lines serialized to backend API, 0 phantom rows
        var request = Assert.Single(accounting.SalesCreateRequests);
        Assert.Equal(3, request.Lines!.Count);
        Assert.Equal(itemId1, request.Lines[0].ItemId);
        Assert.Equal(itemId2, request.Lines[1].ItemId);
        Assert.Equal(itemId3, request.Lines[2].ItemId);
    }

    [Fact]
    public async Task SalesInvoice_PartiallyEditedRow_BlocksSave_AndFlagsRowError()
    {
        var fixture = CreateFixture();
        var accounting = new AccountingFixture();
        var customerId = Guid.NewGuid();
        var patternRevId = Guid.NewGuid();

        accounting.Parties.Add(new PartyDetailDto { Id = customerId, Code = "C001", NameArabic = "عميل", Role = PartyRole.Customer, IsActive = true });
        accounting.Patterns.Add(new DocumentPatternResponse
        {
            Id = Guid.NewGuid(), Code = "SI", NameArabic = "فاتورة", OperationKind = BusinessOperationKind.SalesInvoice, IsActive = true, IsRunnable = true,
            ActiveRevision = new PatternRevisionResponse { Id = patternRevId, RevisionNumber = 1 }
        });

        using var shell = fixture.Create(new AdministrationFixture(), accounting: accounting);
        shell.UserName = "admin"; shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);

        await shell.NavigateToAsync("sales-invoice-card", new NewSalesInvoiceCardRequest()).ConfigureAwait(true);
        var card = Assert.IsType<SalesInvoiceCardViewModel>(shell.Documents.Active!.Content);

        card.DraftCustomerId = customerId;
        card.DraftWarehouseId = Guid.NewGuid();
        card.DraftPatternRevisionId = patternRevId;

        // Line 0 complete
        card.Lines[0].ItemId = Guid.NewGuid();
        card.Lines[0].UnitId = Guid.NewGuid();
        card.Lines[0].Quantity = "1";
        card.Lines[0].UnitPrice = "100";
        card.Lines[0].Recalculate();

        // Line 1 partially filled (Item selected, but quantity empty/0)
        card.Lines[1].ItemId = Guid.NewGuid();
        card.Lines[1].UnitId = Guid.NewGuid();
        card.Lines[1].Quantity = "";
        card.Lines[1].UnitPrice = "50";
        card.Lines[1].Recalculate();

        Assert.True(card.Lines[1].IsIncomplete);
        Assert.Equal(DocumentRowState.Incomplete, card.Lines[1].State);
        Assert.NotEmpty(card.Lines[1].Error);

        card.RecalculateTotals();
        Assert.False(card.CanSaveDraft); // Must not allow save with incomplete row
    }

    [Fact]
    public void SalesInvoice_ZeroPriceExplicitlyEntered_IsPreservedAndComplete()
    {
        var line = SalesInvoiceLineItem.CreateBlankSlot();
        Assert.True(line.IsPristineBlank);

        line.ItemId = Guid.NewGuid();
        line.UnitId = Guid.NewGuid();
        line.Quantity = "2.0000";
        line.UnitPrice = "0"; // Free promotional sample
        line.Recalculate();

        Assert.False(line.IsPristineBlank);
        Assert.True(line.IsComplete);
        Assert.Equal(DocumentRowState.Complete, line.State);
        Assert.Equal("0.0000", line.LineTotal);
    }

    [Fact]
    public async Task SalesInvoice_AutoGrow_AllowsMoreThanFiftyLines()
    {
        var fixture = CreateFixture();
        var accounting = new AccountingFixture();
        using var shell = fixture.Create(new AdministrationFixture(), accounting: accounting);
        shell.UserName = "admin"; shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);

        await shell.NavigateToAsync("sales-invoice-card", new NewSalesInvoiceCardRequest()).ConfigureAwait(true);
        var card = Assert.IsType<SalesInvoiceCardViewModel>(shell.Documents.Active!.Content);

        Assert.Equal(50, card.Lines.Count);
        card.AutoGrowCommand.Execute(null);
        Assert.Equal(60, card.Lines.Count);
    }

    [Fact]
    public async Task Purchases_Has50BlankSlots_SavesExactlyCompletedRows()
    {
        var fixture = CreateFixture();
        var purchases = new PurchaseFixture();
        var supplierId = Guid.NewGuid();
        var patternRevId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var unitId = Guid.NewGuid();

        using var shell = fixture.Create(new AdministrationFixture(), purchases: purchases);
        shell.UserName = "admin"; shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);

        await shell.NavigateToAsync("purchase-invoices", new NewPurchaseRequest(supplierId)).ConfigureAwait(true);
        var vm = shell.Pages.Purchases!;

        Assert.Equal(50, vm.Lines.Count);
        Assert.All(vm.Lines, l => Assert.True(l.IsPristineBlank));

        vm.DraftWarehouseId = warehouseId;
        vm.DraftPatternRevisionId = patternRevId;

        // Complete 2 lines
        vm.Lines[0].ItemId = itemId;
        vm.Lines[0].UnitId = unitId;
        vm.Lines[0].Quantity = "10";
        vm.Lines[0].UnitCost = "100";

        vm.Lines[1].ItemId = itemId;
        vm.Lines[1].UnitId = unitId;
        vm.Lines[1].Quantity = "5";
        vm.Lines[1].UnitCost = "200";

        Assert.Equal("2000.0000", vm.GrandTotalDisplay);
        Assert.True(vm.SaveCommand.CanExecute(null));

        await vm.SaveCommand.ExecuteAsync(null).ConfigureAwait(true);
        var saved = Assert.Single(purchases.Saves);
        Assert.Equal(2, saved.Lines!.Count);
    }

    [Fact]
    public async Task GeneralLedger_Has50BlankSlots_BalancesOnCompleteRows_SavesExactlyTwoLines()
    {
        var fixture = CreateFixture();
        var ledger = new FakeLedgerClient();
        using var shell = fixture.Create(new AdministrationFixture(), ledger: ledger);
        shell.UserName = "admin"; shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);

        await shell.NavigateToAsync("journals", NewJournalRequest.Instance).ConfigureAwait(true);
        var vm = Assert.IsType<GeneralLedgerViewModel>(shell.Documents.Active!.Content);

        Assert.Equal(50, vm.Lines.Count);
        Assert.All(vm.Lines, l => Assert.True(l.IsPristineBlank));

        vm.JournalDescription = "قيد تسوية تجريبي";
        vm.Lines[0].AccountId = Guid.NewGuid();
        vm.Lines[0].Debit = 500m;

        vm.Lines[1].AccountId = Guid.NewGuid();
        vm.Lines[1].Credit = 500m;

        Assert.True(vm.IsBalanced);
        Assert.Equal(500m, vm.TotalDebit);
        Assert.Equal(500m, vm.TotalCredit);
        Assert.Equal(0m, vm.Difference);
        Assert.True(vm.SaveDraftCommand.CanExecute(null));

        await vm.SaveDraftCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.NotNull(ledger.LastCreateRequest);
        Assert.Equal(2, ledger.LastCreateRequest.Lines.Count);
    }

    [Fact]
    public async Task TreasuryDistribution_Has50BlankSlots_SavesOnlyCompletedLines()
    {
        var fixture = CreateFixture();
        var accounting = new AccountingFixture();
        accounting.Patterns.Add(new DocumentPatternResponse
        {
            Id = Guid.NewGuid(), Code = "VP", NameArabic = "سند صرف", OperationKind = BusinessOperationKind.CashPayment, IsActive = true, IsRunnable = true,
            ActiveRevision = new PatternRevisionResponse { Id = Guid.NewGuid(), RevisionNumber = 1 }
        });
        var treasury = new TreasuryFixture();
        using var shell = fixture.Create(new AdministrationFixture(), accounting: accounting, treasury: treasury);
        shell.UserName = "admin"; shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);

        await shell.NavigateToAsync("treasury-vouchers", null).ConfigureAwait(true);
        var vm = shell.Pages.Vouchers!;

        vm.NewPaymentCommand.Execute(null);
        Assert.True(vm.IsPayment);
        Assert.Null(vm.DraftPartyId); // Distribution payment
        Assert.Equal(50, vm.LineRows.Count);
        Assert.All(vm.LineRows, l => Assert.True(l.IsPristineBlank));

        vm.DraftAmount = "300";
        vm.LineRows[0].AccountId = Guid.NewGuid();
        vm.LineRows[0].Amount = "300";

        Assert.True(vm.SaveCommand.CanExecute(null));
        await vm.SaveCommand.ExecuteAsync(null).ConfigureAwait(true);

        var saved = Assert.Single(treasury.Saves);
        Assert.Single(saved.Lines!);
    }

    [Fact]
    public async Task PostedSalesInvoice_LoadsPersistedLinesOnly_NoEditableBlankSlotsAdded()
    {
        var fixture = CreateFixture();
        var invoiceId = Guid.NewGuid();
        var accounting = new AccountingFixture
        {
            SalesDetail = new()
            {
                Id = invoiceId,
                Status = SalesInvoiceStatus.Posted,
                InvoiceNumber = "SI-0099",
                Version = "AAAAAAAAB9E=",
                SubTotal = "100.0000",
                GrandTotal = "100.0000",
                Lines = [
                    new() { Id = Guid.NewGuid(), ItemId = Guid.NewGuid(), Quantity = "1.0000", UnitPrice = "100.0000", LineTotal = "100.0000" }
                ]
            }
        };

        using var shell = fixture.Create(new AdministrationFixture(), accounting: accounting);
        shell.UserName = "admin"; shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);

        await shell.NavigateToAsync("sales-invoice-card", new OpenSalesInvoiceCardRequest(invoiceId)).ConfigureAwait(true);
        var card = Assert.IsType<SalesInvoiceCardViewModel>(shell.Documents.Active!.Content);

        // Must load only the 1 persisted line, not 50
        Assert.Single(card.Lines);
        Assert.True(card.Lines[0].IsPersisted);
        Assert.False(card.IsEditing);
        Assert.False(card.AddLineCommand.CanExecute(null));
    }

    [Fact]
    public async Task SalesInvoice_HeaderDefaults_LeaveEveryBlankSlotUnsaved_AndOneFilledRowSavesAlone()
    {
        var fixture = CreateFixture();
        var accounting = new AccountingFixture();
        var customerId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var costCenterId = Guid.NewGuid();
        var patternRevId = Guid.NewGuid();

        accounting.Parties.Add(new PartyDetailDto { Id = customerId, Code = "C001", NameArabic = "عميل", Role = PartyRole.Customer, IsActive = true });
        accounting.Patterns.Add(new DocumentPatternResponse
        {
            Id = Guid.NewGuid(), Code = "SI", NameArabic = "فاتورة مبيعات", OperationKind = BusinessOperationKind.SalesInvoice, IsActive = true, IsRunnable = true,
            ActiveRevision = new PatternRevisionResponse { Id = patternRevId, RevisionNumber = 1 }
        });

        using var shell = fixture.Create(new AdministrationFixture(), accounting: accounting);
        shell.UserName = "admin"; shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);

        await shell.NavigateToAsync("sales-invoice-card", new NewSalesInvoiceCardRequest()).ConfigureAwait(true);
        var card = Assert.IsType<SalesInvoiceCardViewModel>(shell.Documents.Active!.Content);

        // Header values chosen after the 50-slot buffer exists must not turn the untouched slots into data.
        card.DraftCustomerId = customerId;
        card.DraftPatternRevisionId = patternRevId;
        card.DraftWarehouseId = warehouseId;
        card.DraftCostCenterId = costCenterId;

        Assert.All(card.Lines, line => Assert.True(line.IsPristineBlank));
        Assert.All(card.Lines, line =>
        {
            Assert.Equal(warehouseId, line.WarehouseId);
            Assert.Equal(costCenterId, line.CostCenterId);
        });
        Assert.DoesNotContain(card.GetValidationErrors(), error => error.Contains("— سطر", StringComparison.Ordinal));

        card.Lines[0].ItemId = Guid.NewGuid();
        card.Lines[0].UnitId = Guid.NewGuid();
        card.Lines[0].Quantity = "3";
        card.Lines[0].UnitPrice = "25";
        card.Lines[0].Recalculate();

        Assert.True(card.SaveCardCommand.CanExecute(null));
        await card.SaveCardCommand.ExecuteAsync(null).ConfigureAwait(true);

        var request = Assert.Single(accounting.SalesCreateRequests);
        var line = Assert.Single(request.Lines!);
        Assert.Equal(warehouseId, line.WarehouseId);
        Assert.Equal(costCenterId, line.CostCenterId);
    }

    [Fact]
    public async Task SalesInvoice_HalfFilledRow_ReportsItsOwnNumberAndReason_NeverARawKey()
    {
        var fixture = CreateFixture();
        var accounting = new AccountingFixture();
        var patternRevId = Guid.NewGuid();

        accounting.Parties.Add(new PartyDetailDto { Id = Guid.NewGuid(), Code = "C001", NameArabic = "عميل", Role = PartyRole.Customer, IsActive = true });
        accounting.Patterns.Add(new DocumentPatternResponse
        {
            Id = Guid.NewGuid(), Code = "SI", NameArabic = "فاتورة مبيعات", OperationKind = BusinessOperationKind.SalesInvoice, IsActive = true, IsRunnable = true,
            ActiveRevision = new PatternRevisionResponse { Id = patternRevId, RevisionNumber = 1 }
        });

        using var shell = fixture.Create(new AdministrationFixture(), accounting: accounting);
        shell.UserName = "admin"; shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);

        await shell.NavigateToAsync("sales-invoice-card", new NewSalesInvoiceCardRequest()).ConfigureAwait(true);
        var card = Assert.IsType<SalesInvoiceCardViewModel>(shell.Documents.Active!.Content);

        card.DraftCustomerId = accounting.Parties[0].Id;
        card.DraftWarehouseId = Guid.NewGuid();
        card.DraftPatternRevisionId = patternRevId;

        card.Lines[0].ItemId = Guid.NewGuid();
        card.Lines[0].UnitId = Guid.NewGuid();
        card.Lines[0].Quantity = "1";
        card.Lines[0].UnitPrice = "10";
        card.Lines[0].Recalculate();

        // Row 2 has an item but no unit: the operator must be told which row and what is missing.
        card.Lines[1].ItemId = Guid.NewGuid();
        card.Lines[1].Recalculate();

        var localization = new Localization.LocalizationService("ar");
        var errors = card.GetValidationErrors();
        var rowError = Assert.Single(errors, error => error.Contains("— سطر", StringComparison.Ordinal));
        Assert.Equal(string.Format(System.Globalization.CultureInfo.CurrentCulture, localization["D_LineRowError"], localization["D_InvoiceLines"], 2, localization["D_LineUnitRequired"]), rowError);
        Assert.DoesNotContain("D_", rowError, StringComparison.Ordinal);
        Assert.False(card.CanSaveDraft);
    }

    [Fact]
    public async Task SalesInvoice_NewDraft_ShowsTheNextNumberAsAnEstimate_WithoutReservingIt()
    {
        var fixture = CreateFixture();
        var accounting = new AccountingFixture();
        var patternId = Guid.NewGuid();
        var patternRevId = Guid.NewGuid();

        accounting.Patterns.Add(new DocumentPatternResponse
        {
            Id = patternId, Code = "SI", NameArabic = "فاتورة مبيعات", OperationKind = BusinessOperationKind.SalesInvoice, IsActive = true, IsRunnable = true,
            ActiveRevision = new PatternRevisionResponse { Id = patternRevId, RevisionNumber = 1 }
        });

        using var shell = fixture.Create(new AdministrationFixture(), accounting: accounting);
        shell.UserName = "admin"; shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);

        await shell.NavigateToAsync("sales-invoice-card", new NewSalesInvoiceCardRequest()).ConfigureAwait(true);
        var card = Assert.IsType<SalesInvoiceCardViewModel>(shell.Documents.Active!.Content);
        card.DraftPatternRevisionId = patternRevId;

        Assert.Equal("SEQ-00001", card.NumberPreview);
        Assert.True(card.HasNumberPreview);
        Assert.True(accounting.NumberPreviewCalls > 0);
        Assert.Equal(0, accounting.NumberGenerationCalls);

        // Abandoning the draft must leave the sequence where it was.
        await card.CancelCardCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.False(card.HasNumberPreview);
        Assert.Equal(0, accounting.NumberGenerationCalls);
    }

    [Fact]
    public async Task SalesInvoice_SavedDocument_KeepsItsOwnNumber_AndHidesTheEstimate()
    {
        var fixture = CreateFixture();
        var invoiceId = Guid.NewGuid();
        var patternRevId = Guid.NewGuid();
        var accounting = new AccountingFixture
        {
            SalesDetail = new()
            {
                Id = invoiceId, Status = SalesInvoiceStatus.Draft, InvoiceNumber = "SI-0099", Version = "AAAAAAAAB9E=",
                PatternRevisionId = patternRevId, SubTotal = "100.0000", GrandTotal = "100.0000",
                Lines = [new() { Id = Guid.NewGuid(), ItemId = Guid.NewGuid(), Quantity = "1.0000", UnitPrice = "100.0000", LineTotal = "100.0000" }]
            }
        };
        accounting.Patterns.Add(new DocumentPatternResponse
        {
            Id = Guid.NewGuid(), Code = "SI", NameArabic = "فاتورة مبيعات", OperationKind = BusinessOperationKind.SalesInvoice, IsActive = true, IsRunnable = true,
            ActiveRevision = new PatternRevisionResponse { Id = patternRevId, RevisionNumber = 1 }
        });

        using var shell = fixture.Create(new AdministrationFixture(), accounting: accounting);
        shell.UserName = "admin"; shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);

        await shell.NavigateToAsync("sales-invoice-card", new OpenSalesInvoiceCardRequest(invoiceId)).ConfigureAwait(true);
        var card = Assert.IsType<SalesInvoiceCardViewModel>(shell.Documents.Active!.Content);

        Assert.Equal("SI-0099", card.Invoice!.InvoiceNumber);
        Assert.False(card.HasNumberPreview);

        card.BeginEditCommand.Execute(null);
        Assert.False(card.HasNumberPreview);
        Assert.Equal(0, accounting.NumberGenerationCalls);
    }

    [Fact]
    public async Task Purchase_NewDraft_ShowstheNextNumberAsAnEstimate_WithoutReservingIt()
    {
        var fixture = CreateFixture();
        var accounting = new AccountingFixture();
        var patternRevId = Guid.NewGuid();

        accounting.Patterns.Add(new DocumentPatternResponse
        {
            Id = Guid.NewGuid(), Code = "PI", NameArabic = "فاتورة مشتريات", OperationKind = BusinessOperationKind.PurchaseInvoice, IsActive = true, IsRunnable = true,
            ActiveRevision = new PatternRevisionResponse { Id = patternRevId, RevisionNumber = 1 }
        });

        using var shell = fixture.Create(new AdministrationFixture(), accounting: accounting, purchases: new PurchaseFixture());
        shell.UserName = "admin"; shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);

        await shell.NavigateToAsync("purchase-invoices", new NewPurchaseRequest(Guid.NewGuid())).ConfigureAwait(true);
        var vm = shell.Pages.Purchases!;

        Assert.Equal("SEQ-00001", vm.NumberPreview);
        Assert.True(vm.HasNumberPreview);
        Assert.True(accounting.NumberPreviewCalls > 0);
        Assert.Equal(0, accounting.NumberGenerationCalls);

        // A different document date re-reads the estimate instead of consuming one.
        vm.DraftDate = DateTime.Today.AddDays(1);
        Assert.Equal(0, accounting.NumberGenerationCalls);
    }

    [Fact]
    public async Task Voucher_NewDraft_ShowsTheNextNumberAsAnEstimate_WithoutReservingIt()
    {
        var fixture = CreateFixture();
        var accounting = new AccountingFixture();
        var patternRevId = Guid.NewGuid();

        accounting.Patterns.Add(new DocumentPatternResponse
        {
            Id = Guid.NewGuid(), Code = "VR", NameArabic = "سند قبض", OperationKind = BusinessOperationKind.CashReceipt, IsActive = true, IsRunnable = true,
            ActiveRevision = new PatternRevisionResponse { Id = patternRevId, RevisionNumber = 1 }
        });

        using var shell = fixture.Create(new AdministrationFixture(), accounting: accounting, treasury: new TreasuryFixture());
        shell.UserName = "admin"; shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);

        await shell.NavigateToAsync("treasury-vouchers", null).ConfigureAwait(true);
        var vm = shell.Pages.Vouchers!;
        vm.NewReceiptCommand.Execute(null);

        Assert.True(vm.IsEditing);
        Assert.Equal("SEQ-00001", vm.NumberPreview);
        Assert.True(vm.HasNumberPreview);
        Assert.True(accounting.NumberPreviewCalls > 0);
        Assert.Equal(0, accounting.NumberGenerationCalls);
    }
}
