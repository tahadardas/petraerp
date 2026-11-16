using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PetraERP.Desktop.Shell;

/// <summary>
/// Centralized keyboard shortcut router for the modern accounting desktop interface.
/// Unifies shortcuts across DesktopWindow, DocumentWindow, and child editors.
/// </summary>
public static class DesktopShortcutRouter
{
    public static bool HandleKey(Window window, DesktopViewModel shell, WorkspaceDocument? document, Key key, ModifierKeys modifiers)
    {
        if (!shell.IsAuthenticated || shell.Dialogs?.Current is not null) return false;

        // Ctrl+K: Palette
        if (modifiers == ModifierKeys.Control && key == Key.K)
        {
            if (shell.Palette.IsOpen) shell.Palette.IsOpen = false;
            else
            {
                if (window is DocumentWindow dw) { dw.MainShell?.Activate(); }
                shell.Palette.Open();
            }
            return true;
        }

        if (shell.Palette.IsOpen) return false;

        // Escape: close notifications
        if (key == Key.Escape && modifiers == ModifierKeys.None)
        {
            if (shell.Notifications.IsPanelOpen)
            {
                shell.Notifications.IsPanelOpen = false;
                return true;
            }
        }

        // Ctrl+W or Ctrl+F4: Close document
        if (modifiers == ModifierKeys.Control && key is Key.W or Key.F4)
        {
            var targetDoc = document ?? shell.Documents.Active;
            if (targetDoc.IsClosable)
            {
                _ = shell.CloseDocumentCommand.ExecuteAsync(targetDoc);
                return true;
            }
        }

        // Workspace navigation tabs (Ctrl+Tab, Ctrl+Shift+Tab)
        if (key == Key.Tab && modifiers == ModifierKeys.Control)
        {
            shell.NextDocumentCommand.Execute(null);
            return true;
        }
        if (key == Key.Tab && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            shell.PreviousDocumentCommand.Execute(null);
            return true;
        }

        // Alt+1...3: Select pane in active host
        if (modifiers == ModifierKeys.Alt && key >= Key.D1 && key <= Key.D3)
        {
            var host = (window as DocumentWindow)?.ContentHost ?? (window as DesktopWindow)?.PageHost;
            if (host is not null && DesktopWindow.FindPaneHost(host)?.SelectPane(key - Key.D1, true) == true)
                return true;
        }

        // Ctrl+F: Search / filter
        if (modifiers == ModifierKeys.Control && key == Key.F)
        {
            var host = (window as DocumentWindow)?.ContentHost ?? (window as DesktopWindow)?.PageHost;
            if (host is not null && DesktopWindow.FindSearchTarget(host) is { } target)
            {
                DesktopWindow.FindPaneHost(host)?.Reveal(target);
                target.BringIntoView();
                target.Focus();
                (target as TextBox)?.SelectAll();
                return true;
            }
            shell.Palette.Open();
            return true;
        }

        return false;
    }
}
