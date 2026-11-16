using System.Windows.Input;

namespace PetraERP.Desktop.Productivity;

public enum WorkspaceShortcut { New, SaveDraft, Search, Palette, Print, SafePrimary, CloseOverlay }
public static class WorkspaceShortcuts
{
    public static WorkspaceShortcut? Resolve(Key key, ModifierKeys modifiers, bool editingCell) => (key, modifiers) switch
    {
        (Key.K, ModifierKeys.Control) => WorkspaceShortcut.Palette,
        (Key.F, ModifierKeys.Control) => WorkspaceShortcut.Search,
        (Key.N, ModifierKeys.Control) => WorkspaceShortcut.New,
        (Key.S, ModifierKeys.Control) => WorkspaceShortcut.SaveDraft,
        (Key.P, ModifierKeys.Control) => WorkspaceShortcut.Print,
        (Key.Enter, ModifierKeys.Control) when !editingCell => WorkspaceShortcut.SafePrimary,
        (Key.Escape, ModifierKeys.None) => WorkspaceShortcut.CloseOverlay,
        _ => null,
    };
}
