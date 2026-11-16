using PetraERP.ApiClient;
using PetraERP.Desktop.Purchases;
using PetraERP.Desktop.Sales;

namespace PetraERP.Desktop.Tests;

/// <summary>The purchase invoice's discounts-and-additions table: totals, rows sent to the server, and incomplete rows refused.</summary>
public sealed class PurchaseAdjustmentsWorkstationTests
{
    [Fact]
    public async Task AdjustmentRowsFeedTheGrandTotalAndAreSentWithTheirAccounts()
    {
        var fixture = new ShellFixture { Confirm = true };
        fixture.User.Permissions = ["Purchases.Invoices.View", "Purchases.Invoices.Manage", "Purchases.Invoices.Post", "Purchases.Invoices.Return",
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
        Assert.Equal(supplier, page.DraftSupplierId); Assert.True(page.IsEditing);
        page.DraftWarehouseId ??= Guid.NewGuid();
        var line = page.Lines[0];
        line.ItemId = Guid.NewGuid(); line.UnitId ??= Guid.NewGuid();
        line.Quantity = "2.5"; line.UnitCost = "100000"; line.DiscountAmount = "1000"; line.TaxAmount = "10000";
        Assert.Equal("259000.0000", page.GrandTotalDisplay);

        var earned = Guid.NewGuid(); var freight = Guid.NewGuid(); var counter = Guid.NewGuid();
        var discount = page.AddAdjustment(); discount.Kind = SalesInvoiceAdjustmentKind.Discount; discount.AccountId = earned; discount.Amount = "4000";
        var addition = page.AddAdjustment(); addition.Kind = SalesInvoiceAdjustmentKind.Addition; addition.AccountId = freight; addition.Percentage = "2";
        Assert.Equal("5000", addition.Amount);                 // 2% of the 250000 subtotal
        Assert.Equal("260000.0000", page.GrandTotalDisplay);   // 259000 - 4000 + 5000

        var internalRow = page.AddAdjustment(); internalRow.Kind = SalesInvoiceAdjustmentKind.Addition; internalRow.AccountId = freight; internalRow.CounterAccountId = counter; internalRow.Amount = "700";
        Assert.Equal("260000.0000", page.GrandTotalDisplay);   // a counter-account row never changes the total

        var started = page.AddAdjustment(); started.Amount = "10";
        Assert.False(page.IsDraftComplete() && page.GetValidationErrors().Count == 0);   // an unfinished row is reported, not dropped
        page.RemoveAdjustment(started);

        await page.SaveCommand.ExecuteAsync(null).ConfigureAwait(true);
        var saved = Assert.Single(purchases.Saves);
        Assert.Equal(3, saved.Adjustments!.Count);
        Assert.Contains(saved.Adjustments, a => a.Kind == SalesInvoiceAdjustmentKind.Discount && a.AccountId == earned && a.Amount == "4000.0000");
        Assert.Contains(saved.Adjustments, a => a.AccountId == freight && a.CounterAccountId == counter && a.Amount == "700.0000");
    }
}
