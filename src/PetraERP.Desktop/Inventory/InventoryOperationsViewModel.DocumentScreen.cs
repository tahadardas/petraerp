using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Controls;

namespace PetraERP.Desktop.Inventory;

/// <summary>
/// Maps the warehouse-operations screen onto the shared <see cref="IDocumentScreen"/> contract. Only names the
/// existing commands: posting, reversal, inventory and numbering rules are untouched.
/// </summary>
public sealed partial class InventoryOperationsViewModel : IDocumentScreen
{
    public DocumentLifecycle Lifecycle => Current switch
    {
        null => IsEditing ? DocumentLifecycle.New : DocumentLifecycle.None,
        { State: InventoryDocumentState.Posted } => DocumentLifecycle.Posted,
        { State: InventoryDocumentState.Reversed } => DocumentLifecycle.Reversed,
        _ => DocumentLifecycle.Draft,
    };

    public bool HasDocumentStatus => Lifecycle != DocumentLifecycle.None;

    /// <summary>Text of the single status badge in the title bar ("Draft", "Posted", ...). Empty when no document is shown.</summary>
    public string DocumentStatusText => Lifecycle == DocumentLifecycle.None ? string.Empty
        : Lifecycle == DocumentLifecycle.New ? Localization["D_New"]
        : Localization["INV_State_" + Current!.State];

    public BadgeKind DocumentStatusKind => Lifecycle switch
    {
        DocumentLifecycle.Posted => BadgeKind.Success,
        DocumentLifecycle.Reversed => BadgeKind.Warning,
        DocumentLifecycle.Draft or DocumentLifecycle.New => BadgeKind.Info,
        _ => BadgeKind.Neutral,
    };

    private RelayCommand? backToListCommand;

    /// <summary>The documents list is only shown while no document is open or being edited, so the editor gets the whole tab.</summary>
    public bool ShowDocumentList => Current is null && !IsEditing;

    /// <summary>The cost center is mandatory only for an internal issue; for other kinds it stays collapsed.</summary>
    public bool IsCostCenterRequired => Kind == InventoryDocumentKind.InternalIssue;

    /// <summary>Closes a viewed (not edited) document and returns to the list.</summary>
    public RelayCommand BackToListCommand => backToListCommand ??= new RelayCommand(() => { Show(null); NotifyCommands(); }, () => Current is not null && !IsEditing);

    ICommand? IDocumentScreen.NewCommand => NewCommand;
    ICommand? IDocumentScreen.EditCommand => EditCommand;
    ICommand? IDocumentScreen.SaveCommand => SaveCommand;
    ICommand? IDocumentScreen.CancelCommand => CancelCommand;
    ICommand? IDocumentScreen.DeleteCommand => null;      // warehouse documents are never deleted; drafts are cancelled
    ICommand? IDocumentScreen.ApproveCommand => null;     // approval and posting are one action here (PostCommand)
    ICommand? IDocumentScreen.PostCommand => PostCommand;
    ICommand? IDocumentScreen.ReverseCommand => ReverseCommand;
    ICommand? IDocumentScreen.PrintCommand => null;       // no print template for warehouse documents yet
    // PreviousCommand/NextCommand on this screen page the documents list, they do not move between documents.
    ICommand? IDocumentScreen.FirstCommand => null;
    ICommand? IDocumentScreen.PreviousCommand => null;
    ICommand? IDocumentScreen.NextCommand => null;
    ICommand? IDocumentScreen.LastCommand => null;

    public IReadOnlyList<DocumentExtraCommand> MoreCommands =>
    [
        new(Localization["D_OpenJournal"], OpenJournalCommand, () => Current is not null),
        new(Localization["D_PostAll"], BulkPostCommand, () => CanManagePostingBatches),
    ];
}
