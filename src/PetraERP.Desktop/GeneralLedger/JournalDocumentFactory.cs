using PetraERP.Desktop.Accounting;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;
using PetraERP.Desktop.Sales;

namespace PetraERP.Desktop.GeneralLedger;

/// <summary>
/// Creates one journal view model per journal document (an opened journal or a new draft). Each instance owns its
/// posting-safety state (idempotency key of an ambiguous attempt), so two documents never share it. Dependencies are
/// explicit; there is no service locator.
/// </summary>
public sealed class JournalDocumentFactory(IGeneralLedgerClient client, IUserConfirmation confirmation, DesktopOperation operation,
    ILocalizationService localization, ShellNavigator navigator, IFiscalCalendarClient calendar, SalesDocumentLinksFactory links,
    Relationships.RelatedDocumentsFactory relations)
{
    public GeneralLedgerViewModel Create() => new(client, confirmation, operation, localization)
    {
        IsDocument = true,
        DocumentLinks = links.Create(),
        VoucherLinks = relations.Create(),
        Navigator = navigator,
        Calendar = calendar,
    };
}
