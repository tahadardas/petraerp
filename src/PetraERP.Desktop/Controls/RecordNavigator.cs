using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PetraERP.Desktop.Controls;

/// <summary>
/// Al-Ameen style high-speed record navigation control (|&lt; &lt; [Number] &gt; &gt;|) with status marker.
/// Bound strictly to actual server keyset / document navigation queries with unsaved-change protection.
/// </summary>
public sealed class RecordNavigator : Control
{
    public static readonly DependencyProperty FirstCommandProperty = DependencyProperty.Register(
        nameof(FirstCommand), typeof(ICommand), typeof(RecordNavigator));

    public static readonly DependencyProperty PreviousCommandProperty = DependencyProperty.Register(
        nameof(PreviousCommand), typeof(ICommand), typeof(RecordNavigator));

    public static readonly DependencyProperty NextCommandProperty = DependencyProperty.Register(
        nameof(NextCommand), typeof(ICommand), typeof(RecordNavigator));

    public static readonly DependencyProperty LastCommandProperty = DependencyProperty.Register(
        nameof(LastCommand), typeof(ICommand), typeof(RecordNavigator));

    public static readonly DependencyProperty SearchCommandProperty = DependencyProperty.Register(
        nameof(SearchCommand), typeof(ICommand), typeof(RecordNavigator));

    public static readonly DependencyProperty JumpCommandProperty = DependencyProperty.Register(
        nameof(JumpCommand), typeof(ICommand), typeof(RecordNavigator));

    public static readonly DependencyProperty RecordNumberProperty = DependencyProperty.Register(
        nameof(RecordNumber), typeof(object), typeof(RecordNavigator));

    public static readonly DependencyProperty StatusTextProperty = DependencyProperty.Register(
        nameof(StatusText), typeof(string), typeof(RecordNavigator), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty StatusKindProperty = DependencyProperty.Register(
        nameof(StatusKind), typeof(BadgeKind), typeof(RecordNavigator), new PropertyMetadata(BadgeKind.Neutral));

    public static readonly DependencyProperty IsReadOnlyProperty = DependencyProperty.Register(
        nameof(IsReadOnly), typeof(bool), typeof(RecordNavigator), new PropertyMetadata(false));

    static RecordNavigator()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(RecordNavigator), new FrameworkPropertyMetadata(typeof(RecordNavigator)));
    }

    private TextBox? recordInput;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (recordInput is not null)
        {
            recordInput.KeyDown -= OnInputKeyDown;
            recordInput.LostFocus -= OnInputLostFocus;
        }

        recordInput = GetTemplateChild("PART_RecordInput") as TextBox;
        if (recordInput is not null)
        {
            recordInput.KeyDown += OnInputKeyDown;
            recordInput.LostFocus += OnInputLostFocus;
            recordInput.IsReadOnly = JumpCommand is null || IsReadOnly;
        }
    }

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            if (JumpCommand is not null && recordInput is not null)
            {
                var text = recordInput.Text.Trim();
                if (JumpCommand.CanExecute(text))
                {
                    JumpCommand.Execute(text);
                }
            }
        }
        else if (e.Key == Key.Escape)
        {
            if (recordInput is not null)
            {
                recordInput.Text = RecordNumber?.ToString() ?? string.Empty;
                e.Handled = true;
            }
        }
    }

    private void OnInputLostFocus(object sender, RoutedEventArgs e)
    {
        if (recordInput is not null && recordInput.Text != (RecordNumber?.ToString() ?? string.Empty))
        {
            recordInput.Text = RecordNumber?.ToString() ?? string.Empty;
        }
    }

    public ICommand? FirstCommand
    {
        get => (ICommand?)GetValue(FirstCommandProperty);
        set => SetValue(FirstCommandProperty, value);
    }

    public ICommand? PreviousCommand
    {
        get => (ICommand?)GetValue(PreviousCommandProperty);
        set => SetValue(PreviousCommandProperty, value);
    }

    public ICommand? NextCommand
    {
        get => (ICommand?)GetValue(NextCommandProperty);
        set => SetValue(NextCommandProperty, value);
    }

    public ICommand? LastCommand
    {
        get => (ICommand?)GetValue(LastCommandProperty);
        set => SetValue(LastCommandProperty, value);
    }

    public ICommand? SearchCommand
    {
        get => (ICommand?)GetValue(SearchCommandProperty);
        set => SetValue(SearchCommandProperty, value);
    }

    public ICommand? JumpCommand
    {
        get => (ICommand?)GetValue(JumpCommandProperty);
        set => SetValue(JumpCommandProperty, value);
    }

    public object? RecordNumber
    {
        get => GetValue(RecordNumberProperty);
        set => SetValue(RecordNumberProperty, value);
    }

    public string StatusText
    {
        get => (string)GetValue(StatusTextProperty);
        set => SetValue(StatusTextProperty, value);
    }

    public BadgeKind StatusKind
    {
        get => (BadgeKind)GetValue(StatusKindProperty);
        set => SetValue(StatusKindProperty, value);
    }

    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }
}
