using System.Globalization;
using System.Windows;
using System.Windows.Data;
using PetraERP.Desktop.Accounting;

namespace PetraERP.Desktop.Controls;

/// <summary>
/// Equality converter supporting single-value comparisons (with ConverterParameter, supporting two-way RadioButton bindings)
/// as well as multi-value comparisons (comparing bound values).
/// </summary>
public sealed class EqualityConverter : IValueConverter, IMultiValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Equals(value?.ToString(), parameter?.ToString());

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? (parameter ?? Binding.DoNothing) : Binding.DoNothing;

    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Length >= 2 && Equals(values[0], values[1]);

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Visible when the value is present / true / nonempty; <c>ConverterParameter=invert</c> flips the rule.</summary>
public sealed class VisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var present = value == DependencyProperty.UnsetValue ? false : value switch
        {
            null => false,
            bool flag => flag,
            string text => text.Length > 0,
            int count => count > 0,
            _ => true,
        };
        var invert = parameter as string == "invert";
        return present ^ invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Formats invariant decimal strings from the server for display (no floating point). Parameter "signed" uses parentheses for negatives.</summary>
public sealed class MoneyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        MoneyText.Format(value as string, parameter as string == "signed");

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Empty text for zero amounts so debit/credit columns stay readable; non-zero values are formatted.</summary>
public sealed class MoneyOrBlankConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        MoneyText.IsZero(value as string) ? string.Empty : MoneyText.Format(value as string);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Formats a DateOnly/DateTime/DateTimeOffset as an invariant ISO date (digits LTR).</summary>
public sealed class DateTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTime time => time.ToString(parameter as string == "time" ? "yyyy-MM-dd HH:mm" : "yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTimeOffset offset => offset.ToLocalTime().ToString(parameter as string == "time" ? "yyyy-MM-dd HH:mm" : "yyyy-MM-dd", CultureInfo.InvariantCulture),
        _ => string.Empty,
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Indents tree rows by level (DIP per level in the parameter, default 16).</summary>
public sealed class IndentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var step = double.TryParse(parameter as string, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : 16;
        return new Thickness(value is int level ? level * step : 0, 0, 0, 0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Marks the list search box that Ctrl+F focuses on the current page.</summary>
public static class SearchTarget
{
    public static readonly DependencyProperty IsTargetProperty = DependencyProperty.RegisterAttached("IsTarget", typeof(bool), typeof(SearchTarget), new PropertyMetadata(false));

    public static bool GetIsTarget(DependencyObject element) { ArgumentNullException.ThrowIfNull(element); return (bool)element.GetValue(IsTargetProperty); }

    public static void SetIsTarget(DependencyObject element, bool value) { ArgumentNullException.ThrowIfNull(element); element.SetValue(IsTargetProperty, value); }
}

/// <summary>True when the command id (first value) is among the favorite commands (second value).</summary>
public sealed class FavoriteStateConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length >= 2 && values[0] is string id && values[1] is IEnumerable<PetraERP.ApiClient.WorkspaceCommandResponse> favorites && favorites.Any(item => item.Id == id);

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Bridges a non-nullable Guid (Guid.Empty = none) to a picker's nullable selection.</summary>
public sealed class GuidOrNullConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is Guid id && id != Guid.Empty ? id : null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is Guid id ? id : Guid.Empty;
}
