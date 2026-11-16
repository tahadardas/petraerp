using PetraERP.ApiClient;
using PetraERP.Desktop.Shell;
using PetraERP.Desktop.Treasury;

namespace PetraERP.Desktop.Tests;

public sealed class TreasuryWorkstationTests
{
    private static readonly string[] Permissions =
        ["Treasury.Vouchers.View", "Treasury.Vouchers.Manage", "Treasury.Vouchers.Allocate", "Treasury.Vouchers.Post", "Treasury.Vouchers.Reverse", "Parties.Directory.View", "Ledger.Journals.View"];

    private static (ShellFixture Fixture, AccountingFixture Accounting, TreasuryFixture Treasury, Guid Customer, Guid ReceiptRevision) Setup()
    {
        var fixture = new ShellFixture { Confirm = true };
        fixture.User.Permissions = Permissions;
        var accounting = new AccountingFixture();
        var customer = Guid.NewGuid();
        accounting.Parties.Add(new PartyDetailDto { Id = customer, Code = "C1", NameArabic = "زبون", Role = PartyRole.Customer, IsActive = true });
        var receiptRevision = Guid.NewGuid();
        foreach (var (kind, revision) in new[] { (BusinessOperationKind.CashReceipt, receiptRevision), (BusinessOperationKind.CashPayment, Guid.NewGuid()) })
            accounting.Patterns.Add(new DocumentPatternResponse
            {
                Id = Guid.NewGuid(), Code = kind.ToString(), NameArabic = kind.ToString(), OperationKind = kind, IsActive = true, IsRunnable = true,
                Version = "AAAAAAAAB9E=", ActiveRevision = new PatternRevisionResponse { Id = revision, RevisionNumber = 1 },
            });
        return (fixture, accounting, new TreasuryFixture(), customer, receiptRevision);
    }

    [Fact]
    public async Task ReceiptAllocatesOutstandingInvoicesKeepsTheKeyOnLostResponseAndReverses()
    {
        var (fixture, accounting, treasury, customer, revision) = Setup();
        var invoice = Guid.NewGuid();
        treasury.Outstanding.Add(new OutstandingInvoiceDto { InvoiceId = invoice, InvoiceNumber = "SI-0001", CurrencyCode = "JOD", Outstanding = "150.0000" });
        using var shell = fixture.Create(new AdministrationFixture(), accounting: accounting, treasury: treasury);
        shell.UserName = "admin"; shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);

        // A receipt started from an invoice preloads that invoice's outstanding balance as the allocation.
        await shell.NavigateToAsync("treasury-vouchers", new NewVoucherRequest(VoucherKind.Receipt, customer, invoice)).ConfigureAwait(true);
        var page = Assert.IsType<VouchersViewModel>(shell.CurrentView);
        Assert.True(page.IsReceipt); Assert.Equal(revision, page.DraftPatternRevisionId);
        var row = Assert.Single(page.AllocationRows);
        Assert.Equal("150.0000", row.Amount); Assert.Equal("150.0000", page.DraftAmount);
        page.DraftAmount = "200";
        Assert.Equal("50.0000", page.AdvanceDisplay);
        row.Amount = "-1";
        Assert.False(page.IsDraftComplete());
        row.Amount = "120";
        Assert.True(page.IsDraftComplete());
        await page.SaveCommand.ExecuteAsync(null).ConfigureAwait(true);
        var saved = Assert.Single(treasury.Saves);
        Assert.Equal(VoucherKind.Receipt, saved.Kind); Assert.Equal("200", saved.Amount);
        Assert.Equal("120", Assert.Single(saved.Allocations!).Amount); Assert.Empty(saved.Lines!);
        Assert.False(page.IsEditing); Assert.True(page.PostCommand.CanExecute(null));

        treasury.FailPostAfterSend = true;
        await page.PostCommand.ExecuteAsync(null).ConfigureAwait(true);
        treasury.FailPostAfterSend = false;
        await page.PostCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal(2, treasury.Posts.Count);
        Assert.Equal(treasury.Posts[0].IdempotencyKey, treasury.Posts[1].IdempotencyKey);
        Assert.Equal(VoucherStatus.Posted, page.Current!.Status);
        Assert.False(page.SaveCommand.CanExecute(null)); Assert.False(page.EditCommand.CanExecute(null)); Assert.True(page.OpenJournalCommand.CanExecute(null));

        Assert.False(page.ReverseCommand.CanExecute(null));
        page.ReverseReason = "إيصال مكرر";
        await page.ReverseCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal("إيصال مكرر", Assert.Single(treasury.Reversals).Reason);
        Assert.Equal(VoucherStatus.Reversed, page.Current!.Status);
    }

    [Fact]
    public async Task PostedCreditInvoiceStartsAReceiptPreAllocatedToItsBalance()
    {
        var (fixture, accounting, treasury, customer, _) = Setup();
        fixture.User.Permissions = [.. Permissions, "Sales.Invoices.View"];
        var invoiceId = Guid.NewGuid();
        accounting.SalesDetail = new() { Id = invoiceId, CustomerId = customer, Status = SalesInvoiceStatus.Posted, PaymentMethod = PaymentMethod.Credit,
            InvoiceNumber = "SI-0007", Version = "AAAAAAAAB9E=", SubTotal = "90.0000", DiscountTotal = "0.0000", TaxTotal = "0.0000",
            AdditionsTotal = "0.0000", GrandTotal = "90.0000", ExchangeRate = "1.00000000", Lines = [] };
        treasury.Outstanding.Add(new OutstandingInvoiceDto { InvoiceId = invoiceId, InvoiceNumber = "SI-0007", Outstanding = "90.0000" });
        using var shell = fixture.Create(new AdministrationFixture(), accounting: accounting, treasury: treasury);
        shell.UserName = "admin"; shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
        await shell.NavigateToAsync("sales-invoice-card", new OpenSalesInvoiceCardRequest(invoiceId)).ConfigureAwait(true);
        var card = Assert.IsType<Sales.SalesInvoiceCardViewModel>(shell.Documents.Active!.Content);
        Assert.True(card.ReceivePaymentCommand.CanExecute(null));
        await card.ReceivePaymentCommand.ExecuteAsync(null).ConfigureAwait(true);
        var page = Assert.IsType<VouchersViewModel>(shell.CurrentView);
        Assert.Equal(customer, page.DraftPartyId);
        Assert.Equal("90.0000", Assert.Single(page.AllocationRows).Amount);
        Assert.Equal("90.0000", page.DraftAmount);

        accounting.SalesDetail.PaymentMethod = PaymentMethod.Cash;
        await card.LoadInvoiceAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.False(card.ReceivePaymentCommand.CanExecute(null)); // a cash invoice was settled at sale
    }

    [Fact]
    public async Task PaymentRequiresSupplierOrAccountLinesAndSendsNoAllocations()
    {
        var (fixture, accounting, treasury, _, _) = Setup();
        using var shell = fixture.Create(new AdministrationFixture(), accounting: accounting, treasury: treasury);
        shell.UserName = "admin"; shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
        await shell.NavigateToAsync("treasury-vouchers", null).ConfigureAwait(true);
        var page = Assert.IsType<VouchersViewModel>(shell.CurrentView);

        page.NewPaymentCommand.Execute(null);
        Assert.True(page.IsPayment);
        Assert.Equal(50, page.LineRows.Count);
        var line = page.LineRows[0];
        page.DraftAmount = "100";
        line.Amount = "100";
        Assert.False(page.IsDraftComplete()); // no target yet
        line.AccountId = Guid.NewGuid();
        Assert.True(page.IsDraftComplete());
        line.PartyId = Guid.NewGuid();
        Assert.Null(line.AccountId); // a line targets a supplier or an account, never both
        await page.SaveCommand.ExecuteAsync(null).ConfigureAwait(true);
        var saved = Assert.Single(treasury.Saves);
        Assert.Equal(VoucherKind.Payment, saved.Kind); Assert.Empty(saved.Allocations!);
        Assert.NotNull(Assert.Single(saved.Lines!).PartyId);

    }

    private static async Task<(VouchersViewModel Page, TreasuryFixture Treasury)> ReceiptEditorAsync()
    {
        var (fixture, accounting, treasury, customer, _) = Setup();
        var invoice = Guid.NewGuid();
        treasury.Outstanding.Add(new OutstandingInvoiceDto { InvoiceId = invoice, InvoiceNumber = "SI-0001", CurrencyCode = "JOD", Outstanding = "150.0000" });
        var shell = fixture.Create(new AdministrationFixture(), accounting: accounting, treasury: treasury);
        shell.UserName = "admin"; shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
        await shell.NavigateToAsync("treasury-vouchers", new NewVoucherRequest(VoucherKind.Receipt, customer, invoice)).ConfigureAwait(true);
        var page = Assert.IsType<VouchersViewModel>(shell.CurrentView);
        page.DraftAmount = "200";
        Assert.Single(page.AllocationRows).Amount = "120";
        Assert.True(page.IsDraftComplete());
        return (page, treasury);
    }

    [Fact]
    public async Task GetFailureAfterSuccessfulSaveRetriesTheSameKeyAndNeverCreatesASecondDocument()
    {
        var (page, treasury) = await ReceiptEditorAsync().ConfigureAwait(true);
        treasury.FailGets = 1; // the create committed, but the follow-up read is lost
        await page.SaveCommand.ExecuteAsync(null).ConfigureAwait(true);
        var created = Assert.Single(treasury.SaveAndProcessRequests);
        Assert.Null(created.VoucherId);
        Assert.Null(page.Current); // the editor could not reload the document

        await page.SaveCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal(2, treasury.SaveAndProcessRequests.Count);
        var retry = treasury.SaveAndProcessRequests[1];
        Assert.Equal(created.IdempotencyKey, retry.IdempotencyKey);   // the pending key survived the failed Get
        Assert.Null(retry.VoucherId);                                  // still the create endpoint: the server resolves the key to the stored result
        Assert.NotNull(page.Current);                                  // and now the document is shown

        // The key is released only after a successful reload: editing the shown document afterwards is a new update with a new key.
        Assert.True(page.EditCommand.CanExecute(null));
        page.EditCommand.Execute(null);
        page.DraftAmount = "210";
        await page.SaveCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.NotEqual(created.IdempotencyKey, treasury.SaveAndProcessRequests[2].IdempotencyKey);
        Assert.Equal(page.Current!.Id, treasury.SaveAndProcessRequests[2].VoucherId);
    }

    [Fact]
    public async Task ChangedPayloadAfterGetFailureUpdatesTheSavedDocumentInsteadOfCreatingAnother()
    {
        var (page, treasury) = await ReceiptEditorAsync().ConfigureAwait(true);
        treasury.FailGets = 1;
        await page.SaveCommand.ExecuteAsync(null).ConfigureAwait(true);
        var created = Assert.Single(treasury.SaveAndProcessRequests);
        var savedId = Assert.Single(treasury.Details.Keys);
        Assert.Null(page.Current);

        page.DraftAmount = "250";   // the user keeps editing before retrying
        await page.SaveCommand.ExecuteAsync(null).ConfigureAwait(true);
        var second = treasury.SaveAndProcessRequests[1];
        Assert.Equal(savedId, second.VoucherId);                       // never a second document
        Assert.NotNull(second.ExpectedVersion);                        // authoritative version read first
        Assert.NotEqual(created.IdempotencyKey, second.IdempotencyKey);
        Assert.Single(treasury.Details.Keys);
    }
}
