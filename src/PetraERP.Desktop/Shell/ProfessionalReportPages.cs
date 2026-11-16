using PetraERP.ApiClient;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Reporting;

namespace PetraERP.Desktop.Shell;

/// <summary>One retained parameters model per report; opening a preview never replaces these pages.</summary>
public sealed class ProfessionalReportPages : IDisposable
{
    public ProfessionalReportPages(IProfessionalReportsClient reports, IProfessionalReportLookups lookups,
        IProfessionalReportPresetStore presets, DesktopOperation operation, ILocalizationService localization, ShellNavigator navigator)
    {
        AccountLedger = new(ProfessionalReportKind.AccountLedger, reports, lookups, presets, operation, localization);
        TrialBalance = new(ProfessionalReportKind.TrialBalance, reports, lookups, presets, operation, localization);
        CustomerStatement = new(ProfessionalReportKind.CustomerStatement, reports, lookups, presets, operation, localization);
        SupplierStatement = new(ProfessionalReportKind.SupplierStatement, reports, lookups, presets, operation, localization);
        CombinedStatement = new(ProfessionalReportKind.CombinedPartyStatement, reports, lookups, presets, operation, localization);
        CurrencyBalance = new(ProfessionalReportKind.CurrencyBalance, reports, lookups, presets, operation, localization);
        foreach (var page in new[] { AccountLedger, TrialBalance, CustomerStatement, SupplierStatement, CombinedStatement, CurrencyBalance })
        {
            page.JournalRequested += journal => _ = navigator.NavigateAsync("journals", new OpenJournalRequest(journal));
            page.SourceRequested += row => _ = OpenSourceAsync(page.Context, row, navigator);
            page.PartyCardRequested += party => _ = page.Context?.Can("Parties.Directory.View") == true
                ? navigator.NavigateAsync("party-card", new OpenPartyCardRequest(party)) : Task.CompletedTask;
            page.AccountLedgerRequested += row => _ = row.AccountId is { } account && page.Context?.Can("Ledger.Reports.View") == true
                ? navigator.NavigateAsync("account-ledger", new OpenAccountLedgerRequest(account,
                    page.FromDate is { } from ? DateOnly.FromDateTime(from) : null,
                    page.ToDate is { } to ? DateOnly.FromDateTime(to) : null, false))
                : Task.CompletedTask;
        }
    }

    public ProfessionalReportPageViewModel AccountLedger { get; }
    public ProfessionalReportPageViewModel TrialBalance { get; }
    public ProfessionalReportPageViewModel CustomerStatement { get; }
    public ProfessionalReportPageViewModel SupplierStatement { get; }
    public ProfessionalReportPageViewModel CombinedStatement { get; }
    public ProfessionalReportPageViewModel CurrencyBalance { get; }
    public IReadOnlyList<IShellPage> Pages => [AccountLedger, TrialBalance, CustomerStatement, SupplierStatement, CombinedStatement, CurrencyBalance];

    private static Task OpenSourceAsync(ShellContext? context, ProfessionalReportRowDto row, ShellNavigator navigator)
    {
        if (context is null || row.IsAggregate || !row.CanDrillThrough) return Task.CompletedTask;
        if (row.DocumentId is { } document)
        {
            if (row.Source is ProfessionalReportSource.SalesInvoice or ProfessionalReportSource.SalesReturn && context.Can("Sales.Invoices.View"))
                return navigator.NavigateAsync("sales-invoice-card", new OpenSalesInvoiceCardRequest(document));
            if (row.Source is ProfessionalReportSource.PurchaseInvoice or ProfessionalReportSource.PurchaseReturn && context.Can("Purchases.Invoices.View"))
                return navigator.NavigateAsync("purchase-invoices", new Purchases.OpenPurchaseRequest(document));
            if (row.Source is ProfessionalReportSource.ReceiptVoucher or ProfessionalReportSource.PaymentVoucher && context.Can("Treasury.Vouchers.View"))
                return navigator.NavigateAsync("treasury-vouchers", new Treasury.OpenVoucherRequest(document));
            if (row.Source == ProfessionalReportSource.InventoryDocument && context.Can("Inventory.Movements.View"))
                return navigator.NavigateAsync("inventory-operations", new Inventory.OpenInventoryDocumentRequest(document));
        }
        return row.JournalId is { } journal && context.Can("Ledger.Journals.View")
            ? navigator.NavigateAsync("journals", new OpenJournalRequest(journal)) : Task.CompletedTask;
    }

    public void Dispose()
    {
        foreach (var page in new[] { AccountLedger, TrialBalance, CustomerStatement, SupplierStatement, CombinedStatement, CurrencyBalance }) page.Dispose();
        GC.SuppressFinalize(this);
    }
}
