using System.Windows.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Controls;

namespace PetraERP.Desktop.Sales;

/// <summary>Maps the sales-invoice card onto the shared <see cref="IDocumentScreen"/> contract (names only; rules untouched).</summary>
public sealed partial class SalesInvoiceCardViewModel : IDocumentScreen
{
    public DocumentLifecycle Lifecycle => Invoice switch
    {
        null => IsEditing ? DocumentLifecycle.New : DocumentLifecycle.None,
        { Status: SalesInvoiceStatus.Approved } => DocumentLifecycle.Approved,
        { Status: SalesInvoiceStatus.Posted } => DocumentLifecycle.Posted,
        // A cancelled invoice is final and offers nothing to do, exactly like a reversed one.
        { Status: SalesInvoiceStatus.Reversed or SalesInvoiceStatus.Cancelled } => DocumentLifecycle.Reversed,
        _ => DocumentLifecycle.Draft,
    };

    public bool HasDocumentStatus => Lifecycle != DocumentLifecycle.None;

    public BadgeKind DocumentStatusKind => Lifecycle switch
    {
        DocumentLifecycle.Posted => BadgeKind.Success,
        DocumentLifecycle.Reversed => BadgeKind.Warning,
        DocumentLifecycle.Approved => BadgeKind.Accent,
        DocumentLifecycle.Draft or DocumentLifecycle.New => BadgeKind.Info,
        _ => BadgeKind.Neutral,
    };

    ICommand? IDocumentScreen.NewCommand => BeginNewCommand;
    ICommand? IDocumentScreen.EditCommand => BeginEditCommand;
    ICommand? IDocumentScreen.SaveCommand => SaveCardCommand;
    ICommand? IDocumentScreen.CancelCommand => CancelCardCommand;
    ICommand? IDocumentScreen.DeleteCommand => DeleteDraftCommand;
    ICommand? IDocumentScreen.ApproveCommand => ApproveCommand;
    ICommand? IDocumentScreen.PostCommand => PostCommand;
    ICommand? IDocumentScreen.ReverseCommand => ReverseCommand;
    ICommand? IDocumentScreen.PrintCommand => PrintCommand;
    ICommand? IDocumentScreen.FirstCommand => FirstCommand;
    ICommand? IDocumentScreen.PreviousCommand => PreviousCommand;
    ICommand? IDocumentScreen.NextCommand => NextCommand;
    ICommand? IDocumentScreen.LastCommand => LastCommand;

    public IReadOnlyList<DocumentExtraCommand> MoreCommands =>
    [
        new(Localization["D_SaveAsDraft"], SaveAsDraftCommand, () => IsEditing, AllowWhileEditing: true),
        new("ترحيل المرتجع", PostReturnCommand, () => Lifecycle == DocumentLifecycle.Posted),
        new("الفاتورة الأصلية (للمرتجع / العكس)", OpenOriginalInvoiceCommand, () => Invoice is not null),
        new("بطاقة العميل", OpenCustomerCommand, () => Invoice is not null),
        new("سند قبض للفاتورة", ReceivePaymentCommand, () => Invoice is not null),
        new("القيد المحاسبي الناتج", OpenJournalCommand, () => Lifecycle is DocumentLifecycle.Posted or DocumentLifecycle.Reversed),
        new("كشف حساب العميل", OpenCustomerStatementCommand, () => Invoice is not null),
        new("فاتورة العكس المرتبطة", OpenReversalInvoiceCommand, () => Lifecycle == DocumentLifecycle.Reversed),
        new(Localization["D_ManagePatterns"], ManagePatternsCommand, () => Lifecycle == DocumentLifecycle.None),
        new("تصميم قالب الفاتورة...", DesignTemplateCommand, () => true),
    ];
}
