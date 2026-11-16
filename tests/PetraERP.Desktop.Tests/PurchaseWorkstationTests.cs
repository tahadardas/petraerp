using PetraERP.ApiClient;
using PetraERP.Desktop.Purchases;

namespace PetraERP.Desktop.Tests;

public sealed class PurchaseWorkstationTests
{
    [Fact]
    public async Task PurchaseIsEnteredInTonsPostedWithOneKeyAndReturnedWithinTheReceivedQuantity()
    {
        var fixture = new ShellFixture { Confirm = true };
        fixture.User.Permissions = ["Purchases.Invoices.View", "Purchases.Invoices.Manage", "Purchases.Invoices.Post", "Purchases.Invoices.Return",
            "Inventory.Items.View", "Ledger.Journals.View", "Parties.Directory.View", "Treasury.Vouchers.View", "Treasury.Vouchers.Manage"];
        var accounting = new AccountingFixture();
        var supplier = Guid.NewGuid();
        accounting.Parties.Add(new PartyDetailDto { Id = supplier, Code = "S1", NameArabic = "مورد", Role = PartyRole.Supplier, IsActive = true });
        accounting.Patterns.Add(new DocumentPatternResponse
        {
            Id = Guid.NewGuid(), Code = "PI", NameArabic = "شراء", OperationKind = BusinessOperationKind.PurchaseInvoice, IsActive = true, IsRunnable = true,
            Version = "AAAAAAAAB9E=", ActiveRevision = new PatternRevisionResponse { Id = Guid.NewGuid(), RevisionNumber = 1 },
        });
        var purchases = new PurchaseFixture();
        using var shell = fixture.Create(new AdministrationFixture(), accounting: accounting, purchases: purchases);
        shell.UserName = "admin"; shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);

        await shell.NavigateToAsync("purchase-invoices", new NewPurchaseRequest(supplier)).ConfigureAwait(true);
        var page = Assert.IsType<PurchasesViewModel>(shell.CurrentView);
        Assert.Equal(supplier, page.DraftSupplierId); Assert.True(page.IsEditing);
        page.DraftWarehouseId ??= Guid.NewGuid();
        Assert.Equal(50, page.Lines.Count);
        var line = page.Lines[0];
        line.ItemId = Guid.NewGuid(); line.UnitId ??= Guid.NewGuid();
        line.Quantity = "2.5"; line.UnitCost = "100000"; line.DiscountAmount = "1000"; line.TaxAmount = "10000";
        Assert.Equal("259000.0000", line.TotalDisplay); Assert.Equal("259000.0000", page.GrandTotalDisplay);
        line.Quantity = "-1";
        Assert.False(page.IsDraftComplete());
        line.Quantity = "2.5";
        Assert.True(page.IsDraftComplete());
        await page.SaveCommand.ExecuteAsync(null).ConfigureAwait(true);
        var saved = Assert.Single(purchases.Saves);
        Assert.Equal(supplier, saved.SupplierId); Assert.Equal("2.5", Assert.Single(saved.Lines!).Quantity);

        purchases.FailPostAfterSend = true;
        await page.PostCommand.ExecuteAsync(null).ConfigureAwait(true);
        purchases.FailPostAfterSend = false;
        await page.PostCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal(2, purchases.Posts.Count);
        Assert.Equal(purchases.Posts[0].IdempotencyKey, purchases.Posts[1].IdempotencyKey);
        Assert.Equal(PurchaseInvoiceStatus.Posted, page.Current!.Status);
        Assert.True(page.IsReturnable); Assert.True(page.OpenJournalCommand.CanExecute(null));
        // A posted credit purchase is settled with a payment voucher; the card must offer that way out.
        Assert.True(page.PaySupplierCommand.CanExecute(null));

        var returnLine = Assert.Single(page.ReturnLines);
        returnLine.ReturnQuantity = "2.6";
        page.ReturnReason = "متضرر";
        Assert.False(page.PostReturnCommand.CanExecute(null));
        returnLine.ReturnQuantity = "1";
        Assert.True(page.PostReturnCommand.CanExecute(null));
        await page.PostReturnCommand.ExecuteAsync(null).ConfigureAwait(true);
        var sent = Assert.Single(purchases.Returns);
        Assert.Equal("1.0000", Assert.Single(sent.Lines!).Quantity);
        Assert.True(page.IsReturnDocument); Assert.True(page.OpenOriginalCommand.CanExecute(null));
    }

    /// <summary>
    /// The purchase card must carry the same information the sales card does: the full totals breakdown, the supplier's due date, the line
    /// cost centre, and the manual number the operator typed — the one-click save posts with that number, so losing it silently is a bug.
    /// </summary>
    [Fact]
    public async Task PurchaseCardSendsItsManualNumberAndShowsTotalsBreakdownDueDateAndLineCostCenter()
    {
        var fixture = new ShellFixture { Confirm = true };
        fixture.User.Permissions = ["Purchases.Invoices.View", "Purchases.Invoices.Manage", "Purchases.Invoices.Post",
            "Inventory.Items.View", "Ledger.Accounts.View", "Ledger.CostCenters.View", "Parties.Directory.View", "Patterns.Documents.View"];
        var accounting = new AccountingFixture();
        var supplier = Guid.NewGuid();
        accounting.Parties.Add(new PartyDetailDto { Id = supplier, Code = "S1", NameArabic = "مورد", Role = PartyRole.Supplier, IsActive = true, PaymentTermDays = 30 });
        accounting.Patterns.Add(new DocumentPatternResponse
        {
            Id = Guid.NewGuid(), Code = "PI", NameArabic = "شراء", OperationKind = BusinessOperationKind.PurchaseInvoice, IsActive = true, IsRunnable = true,
            Version = "AAAAAAAAB9E=", ActiveRevision = new PatternRevisionResponse { Id = Guid.NewGuid(), RevisionNumber = 1 },
        });
        var purchases = new PurchaseFixture();
        using var shell = fixture.Create(new AdministrationFixture(), accounting: accounting, purchases: purchases);
        shell.UserName = "admin"; shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);

        await shell.NavigateToAsync("purchase-invoices", new NewPurchaseRequest(supplier)).ConfigureAwait(true);
        var page = Assert.IsType<PurchasesViewModel>(shell.CurrentView);
        page.DraftWarehouseId ??= Guid.NewGuid();
        var line = page.Lines[0];
        line.ItemId = Guid.NewGuid(); line.UnitId ??= Guid.NewGuid();
        line.Quantity = "2.5"; line.UnitCost = "100000"; line.DiscountAmount = "1000"; line.TaxAmount = "10000";
        var costCenter = Guid.NewGuid();
        line.CostCenterId = costCenter;

        Assert.Equal("250000.0000", page.SubTotalDisplay);
        Assert.Equal("1000.0000", page.DiscountTotalDisplay);
        Assert.Equal("10000.0000", page.TaxTotalDisplay);
        Assert.Equal("259000.0000", page.GrandTotalDisplay);
        Assert.True(page.ShowDueDate);
        Assert.Equal(page.DraftDate!.Value.Date.AddDays(30).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), page.DueDateDisplay);

        page.ManualDocumentNumber = "PI-0007";
        await page.SaveCommand.ExecuteAsync(null).ConfigureAwait(true);

        var sent = Assert.Single(purchases.ProcessRequests);
        Assert.Equal("PI-0007", sent.ManualNumber);
        Assert.Equal(costCenter, Assert.Single(sent.Document!.Lines!).CostCenterId);
    }

    [Fact]
    public async Task PurchaseCardRequiresAReasonToReverseThenLinksTheReversalAndBlocksASecondOne()
    {
        var fixture = new ShellFixture { Confirm = true };
        fixture.User.Permissions = ["Purchases.Invoices.View", "Purchases.Invoices.Manage", "Purchases.Invoices.Post",
            "Inventory.Items.View", "Ledger.Journals.View", "Parties.Directory.View"];
        var accounting = new AccountingFixture();
        var supplier = Guid.NewGuid();
        accounting.Parties.Add(new PartyDetailDto { Id = supplier, Code = "S1", NameArabic = "مورد", Role = PartyRole.Supplier, IsActive = true });
        accounting.Patterns.Add(new DocumentPatternResponse
        {
            Id = Guid.NewGuid(), Code = "PI", NameArabic = "شراء", OperationKind = BusinessOperationKind.PurchaseInvoice, IsActive = true, IsRunnable = true,
            Version = "AAAAAAAAB9E=", ActiveRevision = new PatternRevisionResponse { Id = Guid.NewGuid(), RevisionNumber = 1 },
        });
        var purchases = new PurchaseFixture();
        using var shell = fixture.Create(new AdministrationFixture(), accounting: accounting, purchases: purchases);
        shell.UserName = "admin"; shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
        await shell.NavigateToAsync("purchase-invoices", new NewPurchaseRequest(supplier)).ConfigureAwait(true);
        var page = Assert.IsType<PurchasesViewModel>(shell.CurrentView);
        page.DraftWarehouseId ??= Guid.NewGuid();
        var line = page.Lines[0];
        line.ItemId = Guid.NewGuid(); line.UnitId ??= Guid.NewGuid(); line.Quantity = "1"; line.UnitCost = "500";
        await page.SaveCommand.ExecuteAsync(null).ConfigureAwait(true);
        await page.PostCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal(PurchaseInvoiceStatus.Posted, page.Current!.Status);
        Assert.True(page.ReverseCommand.CanExecute(null));

        // The reason is part of the accounting record, so the card asks for it before it will reverse anything.
        page.ReverseReason = " ";
        await page.ReverseCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Empty(purchases.Reversals);
        Assert.Equal(page.Localization["D_ReverseReasonRequired"], page.ValidationMessage);

        page.ReverseReason = "أُلغي طلب الإسمنت";
        await page.ReverseCommand.ExecuteAsync(null).ConfigureAwait(true);
        var sent = Assert.Single(purchases.Reversals);
        Assert.Equal("أُلغي طلب الإسمنت", sent.Reason);
        Assert.StartsWith("purchase-reverse:", sent.IdempotencyKey, StringComparison.Ordinal);
        Assert.Equal(page.Current!.Version, sent.ExpectedVersion);

        // The original now reads reversed, offers its linked reversal, and cannot be reversed a second time.
        Assert.Equal(PurchaseInvoiceStatus.Reversed, page.Current.Status);
        Assert.False(page.ReverseCommand.CanExecute(null));
        Assert.NotNull(page.Current.ReversedByInvoiceId);
        Assert.True(page.OpenLinkedReversalCommand.CanExecute(null));
    }
}
