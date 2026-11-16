using System.Windows;
using System.Windows.Controls;

namespace PetraERP.Desktop.Controls;

/// <summary>Attaches a system icon glyph (Segoe Fluent Icons code point) to command buttons and navigation items.</summary>
public static class Icon
{
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.RegisterAttached("Glyph", typeof(string), typeof(Icon), new PropertyMetadata(null));

    public static string? GetGlyph(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (string?)element.GetValue(GlyphProperty);
    }

    public static void SetGlyph(DependencyObject element, string? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(GlyphProperty, value);
    }
}

public enum BadgeKind
{
    Neutral,
    Info,
    Informational = Info,
    Success,
    Warning,
    Error,
    Accent,
}

/// <summary>
/// Compact status chip. The text is always shown (meaning never depends on color) and an icon distinguishes kinds
/// for color-blind users; the chip is exposed to automation with its text.
/// </summary>
public sealed class StatusBadge : Control
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(BadgeKind), typeof(StatusBadge), new PropertyMetadata(BadgeKind.Neutral));
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(StatusBadge), new PropertyMetadata(string.Empty));

    static StatusBadge() => DefaultStyleKeyProperty.OverrideMetadata(typeof(StatusBadge), new FrameworkPropertyMetadata(typeof(StatusBadge)));

    public BadgeKind Kind { get => (BadgeKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
}

/// <summary>Explains an empty list or an unselected detail pane instead of showing a blank area.</summary>
public sealed class EmptyState : Control
{
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(EmptyState), new PropertyMetadata(""));
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(EmptyState));
    public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(nameof(Message), typeof(string), typeof(EmptyState));

    static EmptyState() => DefaultStyleKeyProperty.OverrideMetadata(typeof(EmptyState), new FrameworkPropertyMetadata(typeof(EmptyState)));

    public string Glyph { get => (string)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public string? Title { get => (string?)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Message { get => (string?)GetValue(MessageProperty); set => SetValue(MessageProperty, value); }
}

/// <summary>Page title row with optional subtitle, status content and a command bar slot.</summary>
public sealed class PageHeader : Control
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(PageHeader));
    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(PageHeader));
    public static readonly DependencyProperty StatusProperty = DependencyProperty.Register(nameof(Status), typeof(object), typeof(PageHeader));
    public static readonly DependencyProperty CommandsProperty = DependencyProperty.Register(nameof(Commands), typeof(object), typeof(PageHeader));

    static PageHeader() => DefaultStyleKeyProperty.OverrideMetadata(typeof(PageHeader), new FrameworkPropertyMetadata(typeof(PageHeader)));

    public string? Title { get => (string?)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Subtitle { get => (string?)GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
    public object? Status { get => GetValue(StatusProperty); set => SetValue(StatusProperty, value); }
    public object? Commands { get => GetValue(CommandsProperty); set => SetValue(CommandsProperty, value); }
}
