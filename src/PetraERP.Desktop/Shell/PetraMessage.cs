using System.Windows;
using System.Windows.Controls;

namespace PetraERP.Desktop.Shell;

/// <summary>
/// Themed replacement for <see cref="MessageBox"/> in windows that have no shell DialogHost (the template designer).
/// Same signature and result as MessageBox.Show, so a call site changes only its class name. Cancel/No is the default
/// for warning questions, matching the DialogHost rule for destructive actions.
/// </summary>
public static class PetraMessage
{
    public static MessageBoxResult Show(Window? owner, string message, string? title = null,
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None)
    {
        owner ??= PetraWindows.ActiveOwner();
        var arabic = (owner?.FlowDirection ?? FlowDirection.RightToLeft) == FlowDirection.RightToLeft;
        var result = buttons is MessageBoxButton.OK ? MessageBoxResult.OK : MessageBoxResult.Cancel;
        var window = new Window { Title = title ?? owner?.Title ?? string.Empty, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize };

        var (glyph, brushKey) = image switch
        {
            MessageBoxImage.Error => ("", "Error"),
            MessageBoxImage.Warning => ("", "Warning"),
            MessageBoxImage.Question => ("", "Accent"),
            MessageBoxImage.Information => ("", "Info"),
            _ => (null, null),
        };

        var body = new DockPanel { LastChildFill = true };
        if (glyph is not null)
        {
            var icon = new TextBlock { Text = glyph, FontSize = 18, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Top };
            if (window.TryFindResource("Text.Icon") is Style iconStyle) icon.Style = iconStyle;
            icon.SetResourceReference(TextBlock.ForegroundProperty, brushKey!);
            DockPanel.SetDock(icon, Dock.Left);
            body.Children.Add(icon);
        }
        body.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Top });

        var footer = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var cautious = image is MessageBoxImage.Warning or MessageBoxImage.Error;
        void Add(MessageBoxResult value, string arabicLabel, string englishLabel, bool primary, bool isDefault, bool isCancel)
        {
            var button = new Button { Content = arabic ? arabicLabel : englishLabel, MinWidth = 90, Margin = new Thickness(8, 2, 0, 2), IsDefault = isDefault, IsCancel = isCancel };
            button.SetResourceReference(FrameworkElement.StyleProperty, primary ? "Button.Primary" : "Button.Command");
            button.Click += (_, _) => { result = value; window.DialogResult = true; };
            footer.Children.Add(button);
        }
        switch (buttons)
        {
            case MessageBoxButton.OK:
                Add(MessageBoxResult.OK, "موافق", "OK", true, true, true); break;
            case MessageBoxButton.OKCancel:
                Add(MessageBoxResult.OK, "موافق", "OK", !cautious, !cautious, false);
                Add(MessageBoxResult.Cancel, "إلغاء", "Cancel", false, cautious, true); break;
            case MessageBoxButton.YesNo:
                Add(MessageBoxResult.Yes, "نعم", "Yes", !cautious, !cautious, false);
                Add(MessageBoxResult.No, "لا", "No", false, cautious, true);
                result = MessageBoxResult.No; break;
            case MessageBoxButton.YesNoCancel:
                Add(MessageBoxResult.Yes, "نعم", "Yes", true, !cautious, false);
                Add(MessageBoxResult.No, "لا", "No", false, false, false);
                Add(MessageBoxResult.Cancel, "إلغاء", "Cancel", false, cautious, true); break;
        }

        var root = new StackPanel();
        root.SetResourceReference(FrameworkElement.MarginProperty, "Pad.Dialog");
        root.Children.Add(body);
        root.Children.Add(footer);
        window.Content = root;
        PetraWindows.PrepareDialog(window, owner, DialogSize.Small);
        window.MinWidth = 0;
        _ = window.ShowDialog();
        return result;
    }
}
