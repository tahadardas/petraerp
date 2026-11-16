using System.ComponentModel;
using System.Windows.Input;
using PetraERP.Desktop.Localization;

namespace PetraERP.Desktop.Controls;

/// <summary>Lifecycle state of the document shown on a daily-document screen. Drives which commands are applicable.</summary>
public enum DocumentLifecycle
{
    /// <summary>No document loaded and not editing (list / empty).</summary>
    None,
    /// <summary>A document that has not been saved yet.</summary>
    New,
    Draft,
    Approved,
    Posted,
    Reversed,
}

/// <summary>
/// An extra, screen-specific command. By default it lives under the "More" menu; <paramref name="IsPrimary"/> puts it
/// next to the standard buttons (for screens with several kinds of "new", e.g. receipt and payment voucher). Extras are
/// hidden while editing unless <paramref name="AllowWhileEditing"/> (e.g. "save as draft").
/// </summary>
public sealed record DocumentExtraCommand(string Caption, ICommand Command, Func<bool>? IsApplicable = null, bool IsPrimary = false, bool AllowWhileEditing = false);

/// <summary>
/// One command contract for every daily document screen (sales, purchases, vouchers, warehouse operations).
/// A command that a screen does not support is <c>null</c>; a command that does not apply to the current
/// lifecycle state is hidden by <see cref="DocumentCommandBar"/> (never shown disabled). Posting, inventory and
/// numbering rules stay inside each screen's own commands - this contract only names them.
/// </summary>
public interface IDocumentScreen : INotifyPropertyChanged
{
    ILocalizationService Localization { get; }

    DocumentLifecycle Lifecycle { get; }
    bool IsEditing { get; }

    ICommand? NewCommand { get; }
    ICommand? EditCommand { get; }
    ICommand? SaveCommand { get; }
    ICommand? CancelCommand { get; }
    ICommand? DeleteCommand { get; }
    ICommand? ApproveCommand { get; }
    ICommand? PostCommand { get; }
    ICommand? ReverseCommand { get; }
    ICommand? PrintCommand { get; }

    /// <summary>Navigation between documents (not list paging). Null when the screen has no document navigation.</summary>
    ICommand? FirstCommand { get; }
    ICommand? PreviousCommand { get; }
    ICommand? NextCommand { get; }
    ICommand? LastCommand { get; }

    IReadOnlyList<DocumentExtraCommand> MoreCommands { get; }
}

public enum DocumentCommandId { New, Edit, Save, Cancel, Approve, Post, Print, Reverse, Delete }

/// <summary>Pure layout rules (unit-testable without WPF): which commands are visible, and which of them go under "More".</summary>
public static class DocumentCommandLayout
{
    public static bool IsApplicable(DocumentCommandId id, DocumentLifecycle state, bool isEditing) => id switch
    {
        DocumentCommandId.New => !isEditing,
        DocumentCommandId.Edit => !isEditing && state == DocumentLifecycle.Draft,
        DocumentCommandId.Save => isEditing,
        DocumentCommandId.Cancel => isEditing,
        DocumentCommandId.Approve => !isEditing && state == DocumentLifecycle.Draft,
        DocumentCommandId.Post => !isEditing && state is DocumentLifecycle.Draft or DocumentLifecycle.Approved,
        DocumentCommandId.Print => !isEditing && state is not (DocumentLifecycle.None or DocumentLifecycle.New),
        DocumentCommandId.Reverse => !isEditing && state is DocumentLifecycle.Posted,
        DocumentCommandId.Delete => !isEditing && state == DocumentLifecycle.Draft,
        _ => false,
    };

    /// <summary>Reverse and Delete are rare, so they are only reachable from the "More" menu.</summary>
    public static bool IsPrimary(DocumentCommandId id) => id is not (DocumentCommandId.Reverse or DocumentCommandId.Delete);

    public static ICommand? CommandOf(IDocumentScreen screen, DocumentCommandId id) => id switch
    {
        DocumentCommandId.New => screen.NewCommand,
        DocumentCommandId.Edit => screen.EditCommand,
        DocumentCommandId.Save => screen.SaveCommand,
        DocumentCommandId.Cancel => screen.CancelCommand,
        DocumentCommandId.Approve => screen.ApproveCommand,
        DocumentCommandId.Post => screen.PostCommand,
        DocumentCommandId.Print => screen.PrintCommand,
        DocumentCommandId.Reverse => screen.ReverseCommand,
        DocumentCommandId.Delete => screen.DeleteCommand,
        _ => null,
    };

    /// <summary>Visible commands in display order for the screen's current state.</summary>
    public static IReadOnlyList<DocumentCommandId> Visible(IDocumentScreen screen) =>
        Enum.GetValues<DocumentCommandId>()
            .Where(id => CommandOf(screen, id) is not null && IsApplicable(id, screen.Lifecycle, screen.IsEditing))
            .ToArray();

    public static string CaptionKey(DocumentCommandId id) => id switch
    {
        DocumentCommandId.New => "D_New",
        DocumentCommandId.Edit => "D_Edit",
        DocumentCommandId.Save => "D_Save",
        DocumentCommandId.Cancel => "D_Cancel",
        DocumentCommandId.Approve => "D_Approve",
        DocumentCommandId.Post => "D_Post",
        DocumentCommandId.Print => "D_Print",
        DocumentCommandId.Reverse => "D_Reverse",
        DocumentCommandId.Delete => "D_Delete",
        _ => string.Empty,
    };
}
