using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace PetraERP.Desktop.Controls;

/// <summary>
/// Shared text for the input controls. The composition root points <see cref="Localize"/> at the localization service;
/// controls never hold business rules, and the server remains the authority for every value.
/// </summary>
public static class PetraInputText
{
    public static Func<string, string> Localize { get; set; } = static key => key;
}

/// <summary>Text input with a placeholder and an optional required marker. Inline errors use the shared validation template.</summary>
public class PetraTextBox : TextBox
{
    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(PetraTextBox));
    public static readonly DependencyProperty IsRequiredProperty = DependencyProperty.Register(nameof(IsRequired), typeof(bool), typeof(PetraTextBox));

    public string? Placeholder { get => (string?)GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }
    public bool IsRequired { get => (bool)GetValue(IsRequiredProperty); set => SetValue(IsRequiredProperty, value); }
}

/// <summary>
/// Numeric entry bound to an invariant decimal string (the API wire format). Digits stay left to right inside RTL layouts
/// and align to the right edge. Arabic-Indic digits and Arabic separators are accepted and normalized on commit. The
/// control never rounds: too many decimals, a negative where none is allowed or unparsable text is shown as an inline
/// error and left for the user to correct, and the bound value keeps the user's text.
/// </summary>
public class PetraNumericBox : PetraTextBox
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(NumberKind), typeof(PetraNumericBox),
        new PropertyMetadata(NumberKind.Money, static (d, _) => ((PetraNumericBox)d).Revalidate()));
    public static readonly DependencyProperty AllowNegativeProperty = DependencyProperty.Register(nameof(AllowNegative), typeof(bool), typeof(PetraNumericBox),
        new PropertyMetadata(false, static (d, _) => ((PetraNumericBox)d).Revalidate()));
    public static readonly DependencyProperty MaximumScaleProperty = DependencyProperty.Register(nameof(MaximumScale), typeof(int?), typeof(PetraNumericBox),
        new PropertyMetadata(null, static (d, _) => ((PetraNumericBox)d).Revalidate()));
    private static readonly DependencyPropertyKey ErrorTextKey = DependencyProperty.RegisterReadOnly(nameof(ErrorText), typeof(string), typeof(PetraNumericBox), new PropertyMetadata(null));
    public static readonly DependencyProperty ErrorTextProperty = ErrorTextKey.DependencyProperty;
    private static readonly DependencyPropertyKey HasInputErrorKey = DependencyProperty.RegisterReadOnly(nameof(HasInputError), typeof(bool), typeof(PetraNumericBox), new PropertyMetadata(false));
    public static readonly DependencyProperty HasInputErrorProperty = HasInputErrorKey.DependencyProperty;

    public PetraNumericBox()
    {
        FlowDirection = FlowDirection.LeftToRight;
        TextAlignment = TextAlignment.Right;
        InputScope = new InputScope { Names = { new InputScopeName(InputScopeNameValue.Number) } };
        DataObject.AddPastingHandler(this, OnPaste);
    }

    public NumberKind Kind { get => (NumberKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public bool AllowNegative { get => (bool)GetValue(AllowNegativeProperty); set => SetValue(AllowNegativeProperty, value); }
    public int? MaximumScale { get => (int?)GetValue(MaximumScaleProperty); set => SetValue(MaximumScaleProperty, value); }
    public string? ErrorText => (string?)GetValue(ErrorTextProperty);
    public bool HasInputError => (bool)GetValue(HasInputErrorProperty);

    /// <summary>Characters that can appear while typing a number (digits in three scripts, separators, sign, parentheses).</summary>
    public static bool IsNumericCharacter(char ch, bool allowNegative) =>
        ch is >= '0' and <= '9' or >= '٠' and <= '٩' or >= '۰' and <= '۹' or '.' or ',' or '٫' or '٬'
        || (allowNegative && ch is '-' or '−' or '(' or ')');

    protected override void OnPreviewTextInput(TextCompositionEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (!IsReadOnly && e.Text.Any(ch => !IsNumericCharacter(ch, AllowNegative))) e.Handled = true;
        base.OnPreviewTextInput(e);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        // Space would otherwise insert a grouping blank that OnPreviewTextInput does not see.
        if (e.Key == Key.Space) e.Handled = true;
        base.OnPreviewKeyDown(e);
    }

    protected override void OnTextChanged(TextChangedEventArgs e)
    {
        base.OnTextChanged(e);
        Revalidate();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        // Normalize only valid input (native digits to ASCII, grouping removed). Invalid text stays as typed.
        if (!IsReadOnly && Validate() is null && PetraNumberFormat.TryParseInput(Text, out var value))
        {
            var wire = PetraNumberFormat.ToWire(value);
            if (!string.Equals(wire, Text, StringComparison.Ordinal)) SetCurrentValue(TextProperty, wire);
        }

        base.OnLostKeyboardFocus(e);
    }

    private void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetData(DataFormats.UnicodeText) is not string pasted) return;
        var normalized = PetraNumberFormat.NormalizeInput(pasted);
        if (normalized.Any(ch => !IsNumericCharacter(ch, AllowNegative))) { e.CancelCommand(); return; }
        var data = new DataObject(); data.SetData(DataFormats.UnicodeText, normalized);
        e.DataObject = data;
    }

    private string? Validate() => PetraNumberFormat.Validate(Text, Kind, AllowNegative, MaximumScale);

    private void Revalidate()
    {
        var key = Validate();
        SetValue(ErrorTextKey, key is null ? null : PetraInputText.Localize(key));
        SetValue(HasInputErrorKey, key is not null);
    }
}

/// <summary>Explicit alias for standard accounting numeric entry.</summary>
public class PetraNumericTextBox : PetraNumericBox
{
}

/// <summary>Amount entry: up to 4 decimals; negatives only when <see cref="PetraNumericBox.AllowNegative"/> is set.</summary>
public sealed class PetraMoneyBox : PetraNumericBox
{
}

/// <summary>Quantity entry: up to 4 decimals, never negative by default (returns use their own documents).</summary>
public sealed class PetraQuantityBox : PetraNumericBox
{
    public PetraQuantityBox() => SetCurrentValue(KindProperty, NumberKind.Quantity);
}

/// <summary>Exchange rate and factor entry: up to 8 decimals, never negative, never silently rounded.</summary>
public sealed class PetraRateBox : PetraNumericBox
{
    public PetraRateBox()
    {
        SetCurrentValue(KindProperty, NumberKind.Rate);
        SetCurrentValue(MaximumScaleProperty, 8);
    }
}

/// <summary>
/// Date entry whose text is always laid out left to right. Up/Down change the day and PageUp/PageDown the month while the
/// text box has focus; Alt+Down opens the calendar (framework default). The selected date is still parsed by the framework
/// with the UI culture, and ISO text (yyyy-MM-dd) is accepted in every language.
/// </summary>
public sealed class PetraDatePicker : DatePicker
{
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild("PART_TextBox") is DatePickerTextBox text) text.FlowDirection = FlowDirection.LeftToRight;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (Keyboard.Modifiers == ModifierKeys.None && !IsDropDownOpen && Step(e.Key) is { } step)
        {
            var current = SelectedDate ?? DateTime.Today;
            var next = step.Months != 0 ? current.AddMonths(step.Months) : current.AddDays(step.Days);
            if (IsSelectable(next)) SetCurrentValue(SelectedDateProperty, next);
            e.Handled = true;
        }

        base.OnPreviewKeyDown(e);
    }

    private static (int Days, int Months)? Step(Key key) => key switch
    {
        Key.Up => (1, 0),
        Key.Down => (-1, 0),
        Key.PageUp => (0, 1),
        Key.PageDown => (0, -1),
        _ => null,
    };

    private bool IsSelectable(DateTime date) =>
        (DisplayDateStart is null || date >= DisplayDateStart) && (DisplayDateEnd is null || date <= DisplayDateEnd)
        && !BlackoutDates.Any(range => date >= range.Start && date <= range.End);
}

/// <summary>Search input: a search glyph, a clear button, Escape clears, and Ctrl+F focuses it on its page.</summary>
public sealed class PetraSearchBox : PetraTextBox
{
    public static readonly RoutedUICommand ClearCommand = new("Clear", nameof(ClearCommand), typeof(PetraSearchBox));

    public PetraSearchBox()
    {
        SearchTarget.SetIsTarget(this, true);
        CommandBindings.Add(new CommandBinding(ClearCommand, (_, e) => { Clear(); Focus(); e.Handled = true; }, (_, e) => e.CanExecute = !string.IsNullOrEmpty(Text)));
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None && !string.IsNullOrEmpty(Text)) { Clear(); e.Handled = true; }
        base.OnPreviewKeyDown(e);
    }
}
