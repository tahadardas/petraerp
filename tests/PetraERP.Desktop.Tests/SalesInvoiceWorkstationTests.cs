using PetraERP.ApiClient;
using PetraERP.Desktop.Sales;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Tests;

public sealed class SalesInvoiceWorkstationTests
{
    private static ShellFixture CreateFixture(params string[] permissions)
    {
        var fixture = new ShellFixture();
        fixture.User.Permissions = permissions.Length > 0
            ? permissions
            : ["Sales.Invoices.View", "Sales.Invoices.Manage", "Sales.Invoices.Approve", "Sales.Invoices.Post", "Sales.Invoices.Reverse"];
        return fixture;
    }

    [Fact]
    public async Task PostingRetryAfterLostResponseKeepsTheDocumentRequestKey()
    {
        var fixture = CreateFixture();
        fixture.Confirm = true;
        var accounting = new AccountingFixture { FailSalesPostAfterSend = true };
        using var shell = fixture.Create(new AdministrationFixture(), accounting: accounting);
        shell.UserName = "admin"; shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
        var invoiceId = Guid.NewGuid();
        await shell.NavigateToAsync("sales-invoice-card", new OpenSalesInvoiceCardRequest(invoiceId)).ConfigureAwait(true);
        var card = Assert.IsType<SalesInvoiceCardViewModel>(shell.Documents.Active!.Content);
        Assert.True(card.PostCommand.CanExecute(null));
        await card.PostCommand.ExecuteAsync(null).ConfigureAwait(true);
        await card.PostCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal(2, accounting.SalesPostRequests.Count);
        Assert.Equal(accounting.SalesPostRequests[0].IdempotencyKey, accounting.SalesPostRequests[1].IdempotencyKey);
        card.ActionReason = "سبب جديد";
        await card.PostCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal(3, accounting.SalesPostRequests.Count);
        Assert.NotEqual(accounting.SalesPostRequests[0].IdempotencyKey, accounting.SalesPostRequests[2].IdempotencyKey);
    }

    [Fact]
    public async Task ApprovalAndEditSendTheVersionTheUserLastRead()
    {
        var fixture = CreateFixture();
        fixture.Confirm = true;
        var invoiceId = Guid.NewGuid();
        var accounting = new AccountingFixture
        {
            SalesDetail = new() { Id = invoiceId, Status = SalesInvoiceStatus.Draft, Version = "AAAAAAAAB9E=", SubTotal = "0.0000", DiscountTotal = "0.0000",
                TaxTotal = "0.0000", AdditionsTotal = "0.0000", GrandTotal = "0.0000", ExchangeRate = "1.23456789", Lines = [] },
        };
        accounting.Invoices.Add(new SalesInvoiceSummaryDto { Id = Guid.NewGuid(), Status = SalesInvoiceStatus.Draft, Version = "AAAAAAAAB9I=", GrandTotal = "1.0000" });
        using var shell = fixture.Create(new AdministrationFixture(), accounting: accounting);
        shell.UserName = "admin"; shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);

        await shell.NavigateToAsync("sales-invoice-card", new OpenSalesInvoiceCardRequest(invoiceId)).ConfigureAwait(true);
        var card = Assert.IsType<SalesInvoiceCardViewModel>(shell.Documents.Active!.Content);
        Assert.True(card.ApproveCommand.CanExecute(null));
        await card.ApproveCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal("AAAAAAAAB9E=", Assert.Single(accounting.SalesApproveVersions));

        var list = shell.Pages.SalesInvoices;
        await shell.NavigateToAsync("sales-invoices", null).ConfigureAwait(true);
        list.SelectedInvoice = Assert.Single(list.Invoices);
        Assert.True(list.ApproveInvoiceCommand.CanExecute(null));
        await list.ApproveInvoiceCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal("AAAAAAAAB9I=", accounting.SalesApproveVersions[1]);
    }

    [Fact]
    public async Task DraftCanBeDeletedWithItsVersionButPostedInvoiceCannot()
    {
        var fixture = CreateFixture();
        fixture.Confirm = true;
        var invoiceId = Guid.NewGuid();
        var accounting = new AccountingFixture
        {
            SalesDetail = new() { Id = invoiceId, Status = SalesInvoiceStatus.Draft, Version = "AAAAAAAAB9E=", SubTotal = "0.0000", DiscountTotal = "0.0000",
                TaxTotal = "0.0000", AdditionsTotal = "0.0000", GrandTotal = "0.0000", ExchangeRate = "1.00000000", Lines = [] },
        };
        using var shell = fixture.Create(new AdministrationFixture(), accounting: accounting);
        shell.UserName = "admin"; shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
        await shell.NavigateToAsync("sales-invoice-card", new OpenSalesInvoiceCardRequest(invoiceId)).ConfigureAwait(true);
        var card = Assert.IsType<SalesInvoiceCardViewModel>(shell.Documents.Active!.Content);

        Assert.True(card.CanDeleteDraft);
        Assert.True(card.DeleteDraftCommand.CanExecute(null));
        await card.DeleteDraftCommand.ExecuteAsync(null).ConfigureAwait(true);
        var deleted = Assert.Single(accounting.SalesDeleteRequests);
        Assert.Equal(invoiceId, deleted.InvoiceId);
        Assert.Equal("AAAAAAAAB9E=", deleted.Version);
        Assert.False(card.CanDeleteDraft);
    }

    [Fact]
    public async Task PostedInvoiceCannotBeDeletedOnlyReversed()
    {
        var fixture = CreateFixture();
        fixture.Confirm = true;
        var invoiceId = Guid.NewGuid();
        var accounting = new AccountingFixture
        {
            SalesDetail = new() { Id = invoiceId, Status = SalesInvoiceStatus.Posted, InvoiceNumber = "SI-0001", Version = "AAAAAAAAB9E=", SubTotal = "0.0000",
                DiscountTotal = "0.0000", TaxTotal = "0.0000", AdditionsTotal = "0.0000", GrandTotal = "0.0000", ExchangeRate = "1.00000000", Lines = [] },
        };
        using var shell = fixture.Create(new AdministrationFixture(), accounting: accounting);
        shell.UserName = "admin"; shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
        await shell.NavigateToAsync("sales-invoice-card", new OpenSalesInvoiceCardRequest(invoiceId)).ConfigureAwait(true);
        var card = Assert.IsType<SalesInvoiceCardViewModel>(shell.Documents.Active!.Content);
        Assert.False(card.CanDeleteDraft);
        Assert.False(card.DeleteDraftCommand.CanExecute(null));
        Assert.True(card.ReverseCommand.CanExecute(null));
    }

    [Fact]
    public async Task ReturnPanelLimitsQuantityKeepsTheKeyOnLostResponseAndBlocksFullReversal()
    {
        var fixture = CreateFixture();
        fixture.Confirm = true;
        var invoiceId = Guid.NewGuid(); var lineId = Guid.NewGuid();
        var accounting = new AccountingFixture
        {
            FailSalesReturnAfterSend = true,
            SalesDetail = new() { Id = invoiceId, Status = SalesInvoiceStatus.Posted, InvoiceNumber = "SI-0001", Version = "AAAAAAAAB9E=", SubTotal = "200.0000",
                DiscountTotal = "0.0000", TaxTotal = "0.0000", AdditionsTotal = "0.0000", GrandTotal = "200.0000", ExchangeRate = "1.00000000",
                Lines = [new() { Id = lineId, ItemCode = "I1", ItemNameArabic = "مادة", Quantity = "2.0000", UnitPrice = "100.0000", ReturnedQuantity = "0.5000",
                    DiscountPercentage = "0.0000", DiscountAmount = "0.0000", TaxPercentage = "0.0000", TaxAmount = "0.0000", PumpCost = "0.0000", TransportCost = "0.0000", LineTotal = "200.0000" }] },
        };
        using var shell = fixture.Create(new AdministrationFixture(), accounting: accounting);
        shell.UserName = "admin"; shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
        await shell.NavigateToAsync("sales-invoice-card", new OpenSalesInvoiceCardRequest(invoiceId)).ConfigureAwait(true);
        var card = Assert.IsType<SalesInvoiceCardViewModel>(shell.Documents.Active!.Content);

        Assert.True(card.IsReturnable);
        Assert.False(card.CanReverse);
        var line = Assert.Single(card.ReturnLines);
        Assert.Equal(1.5m, line.Returnable);
        line.ReturnQuantity = "1.6";
        card.ReturnReason = "تالف";
        Assert.False(card.PostReturnCommand.CanExecute(null));
        line.ReturnQuantity = "1.5";
        Assert.True(card.PostReturnCommand.CanExecute(null));

        await card.PostReturnCommand.ExecuteAsync(null).ConfigureAwait(true);
        accounting.FailSalesReturnAfterSend = false;
        await card.PostReturnCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal(2, accounting.SalesReturnRequests.Count);
        Assert.Equal(accounting.SalesReturnRequests[0].IdempotencyKey, accounting.SalesReturnRequests[1].IdempotencyKey);
        var sent = Assert.Single(accounting.SalesReturnRequests[1].Lines);
        Assert.Equal(lineId, sent.SourceLineId); Assert.Equal("1.5000", sent.Quantity);
        var opened = Assert.IsType<SalesInvoiceCardViewModel>(shell.Documents.Active!.Content);
        Assert.Equal(accounting.SalesReturnResultId, opened.InvoiceId);
    }

    [Fact]
    public void SalesInvoiceLineCalculatesTotalsAccuratelyWithConcreteAdditions()
    {
        var line = new SalesInvoiceLineItem
        {
            Quantity = "10.0000",
            UnitPrice = "50.0000",
            DiscountPercentage = "10.0000",
            TaxPercentage = "16.0000",
            PumpCost = "40.0000",
            TransportCost = "25.0000",
            ConcreteGrade = "C30",
        };

        // SubTotal = 10 * 50 = 500
        // Discount = 10% of 500 = 50
        // Net = 450
        // Tax = 16% of 450 = 72
        // Additions = 40 + 25 = 65
        // LineTotal = 450 + 72 + 65 = 587.0000
        line.Recalculate();

        Assert.Equal("50.0000", line.DiscountAmount);
        Assert.Equal("72.0000", line.TaxAmount);
        Assert.Equal("587.0000", line.LineTotal);
        Assert.Equal("C30", line.ConcreteGrade);
    }

    [Fact]
    public async Task SalesInvoicesViewModelLoadsAndDisplaysInvoices()
    {
        var fixture = CreateFixture();
        var accounting = new AccountingFixture();

        var inv1 = new SalesInvoiceSummaryDto
        {
            Id = Guid.NewGuid(),
            InvoiceNumber = "INV-001",
            CustomerName = "شركة المقاولات الحديثة",
            WarehouseCode = "WH-MAIN",
            InvoiceDateUtc = DateTimeOffset.UtcNow,
            PaymentMethod = PaymentMethod.Credit,
            Status = SalesInvoiceStatus.Posted,
            GrandTotal = "1500.0000",
        };

        var inv2 = new SalesInvoiceSummaryDto
        {
            Id = Guid.NewGuid(),
            InvoiceNumber = null,
            CustomerName = "مؤسسة الأمل",
            WarehouseCode = "WH-MAIN",
            InvoiceDateUtc = DateTimeOffset.UtcNow,
            PaymentMethod = PaymentMethod.Cash,
            Status = SalesInvoiceStatus.Draft,
            GrandTotal = "350.0000",
        };

        accounting.Invoices.AddRange([inv1, inv2]);

        using var shell = fixture.Create(new AdministrationFixture(), accounting: accounting);
        shell.UserName = "admin";
        shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);

        var viewModel = shell.Pages.SalesInvoices;
        await shell.NavigateToAsync("sales-invoices", null).ConfigureAwait(true);

        Assert.Equal(2, viewModel.Invoices.Count);
        Assert.Equal(2, viewModel.VisibleInvoices.Count);
        Assert.True(viewModel.CanManage);
        Assert.True(viewModel.CanApprove);
        Assert.True(viewModel.CanPost);
        Assert.True(viewModel.CanReverse);
    }

    [Fact]
    public async Task EveryRunnablePatternGetsAListButtonThatOpensAnInvoiceFixedOnThatPattern()
    {
        var fixture = CreateFixture();
        var data = new AccountingFixture();
        foreach (var (code, name) in new[] { ("SI", "مبيعات نقدية"), ("SC", "مبيعات آجلة") })
            data.Patterns.Add(new() { Id = Guid.NewGuid(), Code = code, NameArabic = name, ShortCode = code, OperationKind = BusinessOperationKind.SalesInvoice,
                IsActive = true, IsRunnable = true, ActiveRevision = new() { Id = Guid.NewGuid(), RevisionNumber = 1 } });
        using var shell = fixture.Create(new AdministrationFixture(), accounting: data);
        shell.UserName = "admin";
        shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);

        await shell.NavigateToAsync("sales-invoices", null).ConfigureAwait(true);
        var list = Assert.IsType<SalesInvoicesViewModel>(shell.Documents.Active!.Content);
        Assert.True(list.HasPatternShortcuts);
        Assert.Equal(2, list.PatternShortcuts.Count);
        var pattern = list.PatternShortcuts[1];

        await list.NewByPatternCommand.ExecuteAsync(pattern).ConfigureAwait(true);

        var card = Assert.IsType<SalesInvoiceCardViewModel>(shell.Documents.Active!.Content);
        Assert.True(card.IsNewMode);
        Assert.Equal(pattern.RevisionId, card.DraftPatternRevisionId);
        Assert.True(card.IsPatternLocked);
        Assert.False(card.IsPatternPickerEnabled);
    }

    [Fact]
    public async Task SalesInvoiceCardViewModelInitializesInNewModeAndComputesTotals()
    {
        var fixture = CreateFixture();
        var accounting = new AccountingFixture();

        using var shell = fixture.Create(new AdministrationFixture(), accounting: accounting);
        shell.UserName = "admin";
        shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);

        await shell.NavigateToAsync("sales-invoices", new NewSalesInvoiceCardRequest()).ConfigureAwait(true);

        var doc = shell.Documents.Active;
        Assert.NotNull(doc);
        Assert.IsType<SalesInvoiceCardViewModel>(doc.Content);

        var card = (SalesInvoiceCardViewModel)doc.Content;

        Assert.True(card.IsNewMode);
        Assert.Equal(50, card.Lines.Count);

        var line = card.Lines[0];
        line.Quantity = "5.0000";
        line.UnitPrice = "60.0000";
        line.DiscountPercentage = "0.0000";
        line.TaxPercentage = "16.0000";
        line.PumpCost = "50.0000";
        line.TransportCost = "20.0000";
        line.Recalculate();

        // 5 * 60 = 300, tax 48, additions 70 => lineTotal = 418.0000
        Assert.Equal("418.0000", line.LineTotal);
        Assert.Equal("418.0000", card.GrandTotalDisplay);
        Assert.Equal("300.0000", card.SubTotalDisplay);
        Assert.Equal("48.0000", card.TaxTotalDisplay);
        Assert.Equal("70.0000", card.AdditionsTotalDisplay);
        Assert.Equal("5.0000", card.TotalQuantityDisplay);
    }
}
