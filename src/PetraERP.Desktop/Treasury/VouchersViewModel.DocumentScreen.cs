using System.Windows.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Controls;

namespace PetraERP.Desktop.Treasury;

/// <summary>
/// Maps the vouchers screen onto the shared <see cref="IDocumentScreen"/> contract. Receipt and payment are two kinds of
/// "new", so they are primary extras instead of one NewCommand. Posting, reversal and numbering rules are untouched.
/// </summary>
public sealed partial class VouchersViewModel : IDocumentScreen
{
    public DocumentLifecycle Lifecycle => Current switch
    {
        null => IsEditing ? DocumentLifecycle.New : DocumentLifecycle.None,
        { Status: VoucherStatus.Approved } => DocumentLifecycle.Approved,
        { Status: VoucherStatus.Posted } => DocumentLifecycle.Posted,
        { Status: VoucherStatus.Reversed } => DocumentLifecycle.Reversed,
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

    ICommand? IDocumentScreen.NewCommand => null; // receipt / payment: see MoreCommands (primary)
    ICommand? IDocumentScreen.EditCommand => EditCommand;
    ICommand? IDocumentScreen.SaveCommand => SaveCommand;
    ICommand? IDocumentScreen.CancelCommand => CancelCommand;
    ICommand? IDocumentScreen.DeleteCommand => DeleteCommand;
    ICommand? IDocumentScreen.ApproveCommand => ApproveCommand;
    ICommand? IDocumentScreen.PostCommand => PostCommand;
    ICommand? IDocumentScreen.ReverseCommand => ReverseCommand;
    ICommand? IDocumentScreen.PrintCommand => PrintCommand;
    ICommand? IDocumentScreen.FirstCommand => FirstVoucherCommand;
    ICommand? IDocumentScreen.PreviousCommand => PreviousVoucherCommand;
    ICommand? IDocumentScreen.NextCommand => NextVoucherCommand;
    ICommand? IDocumentScreen.LastCommand => LastVoucherCommand;

    public IReadOnlyList<DocumentExtraCommand> MoreCommands =>
    [
        new(Localization["D_NewReceipt"], NewReceiptCommand, null, IsPrimary: true),
        new(Localization["D_NewPayment"], NewPaymentCommand, null, IsPrimary: true),
        new(Localization["D_OpenJournal"], OpenJournalCommand, () => Current?.Status is VoucherStatus.Posted or VoucherStatus.Reversed),
        new(Localization["ARP_OpenReversalJournal"], OpenReversalJournalCommand, () => Current?.Status is VoucherStatus.Reversed),
        new(Localization["D_OpenParty"], OpenPartyCommand, () => Current is not null),
        new(Localization["D_PostAll"], BulkPostCommand, () => CanManagePostingBatches),
    ];
}
