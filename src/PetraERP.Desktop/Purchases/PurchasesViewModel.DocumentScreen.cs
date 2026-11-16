using System.Windows.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Controls;

namespace PetraERP.Desktop.Purchases;

/// <summary>Maps the purchase-invoice screen onto the shared <see cref="IDocumentScreen"/> contract (names only; rules untouched).</summary>
public sealed partial class PurchasesViewModel : IDocumentScreen
{
    public DocumentLifecycle Lifecycle => Current switch
    {
        null => IsEditing ? DocumentLifecycle.New : DocumentLifecycle.None,
        { Status: PurchaseInvoiceStatus.Approved } => DocumentLifecycle.Approved,
        { Status: PurchaseInvoiceStatus.Posted } => DocumentLifecycle.Posted,
        { Status: PurchaseInvoiceStatus.Reversed } => DocumentLifecycle.Reversed,
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

    ICommand? IDocumentScreen.NewCommand => NewCommand;
    ICommand? IDocumentScreen.EditCommand => EditCommand;
    ICommand? IDocumentScreen.SaveCommand => SaveCommand;
    ICommand? IDocumentScreen.CancelCommand => CancelCommand;
    ICommand? IDocumentScreen.DeleteCommand => DeleteCommand;
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
        new(Localization["D_PostReturn"], PostReturnCommand, () => Lifecycle == DocumentLifecycle.Posted),
        new(Localization["D_PaySupplier"], PaySupplierCommand, () => Current is not null),
        new(Localization["D_OpenJournal"], OpenJournalCommand, () => Lifecycle is DocumentLifecycle.Posted or DocumentLifecycle.Reversed),
        new(Localization["D_OpenWarehouse"], OpenWarehouseCommand, () => Current is not null),
        new(Localization["D_OpenOriginalInvoice"], OpenOriginalCommand, () => Current is not null),
        new(Localization["D_OpenLinkedReversal"], OpenLinkedReversalCommand, () => Lifecycle == DocumentLifecycle.Reversed),
        new(Localization["D_OpenSupplier"], OpenSupplierCommand, () => Current is not null),
        new("كشف حساب المورد", OpenSupplierStatementCommand, () => Current is not null),
        new(Localization["D_ManagePatterns"], ManagePatternsCommand, () => Lifecycle == DocumentLifecycle.None),
        new("تصميم قالب الفاتورة...", DesignTemplateCommand, () => true),
        new(Localization["D_PostAll"], BulkPostCommand, () => CanManagePostingBatches),
    ];
}
