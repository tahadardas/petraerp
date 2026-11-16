using System.Windows;
using Microsoft.Win32;
using PetraERP.Desktop.Platform;

namespace PetraERP.Desktop.Theming;

/// <summary>
/// Swaps the color dictionary (Themes/Light.xaml or Themes/Dark.xaml) inside the application resources. All
/// views use DynamicResource brushes, so the change is immediate. "System" follows the Windows app theme.
/// </summary>
public sealed class WpfThemeService : IThemeService, IDisposable
{
    private static readonly Uri LightUri = new("pack://application:,,,/PetraERP.Desktop;component/Themes/Light.xaml", UriKind.Absolute);
    private static readonly Uri DarkUri = new("pack://application:,,,/PetraERP.Desktop;component/Themes/Dark.xaml", UriKind.Absolute);

    private readonly System.Windows.Application application;

    public WpfThemeService(System.Windows.Application application)
    {
        ArgumentNullException.ThrowIfNull(application);
        this.application = application;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public event EventHandler? ThemeChanged;

    public ThemePreference Preference { get; private set; }

    public AppTheme EffectiveTheme { get; private set; }

    public DensityMode Density { get; private set; } = DensityMode.Standard;

    public ThemeSkin Skin { get; private set; }

    private readonly List<string> skinKeys = [];

    /// <summary>Re-resolves the base theme (black skins force dark) and lays the skin palette over it.</summary>
    public void ApplySkin(ThemeSkin skin)
    {
        Skin = Enum.IsDefined(skin) ? skin : ThemeSkin.Petra;
        Apply(Preference);
    }

    private void ApplySkinOverrides()
    {
        var resources = application.Resources;
        foreach (var key in skinKeys) resources.Remove(key);
        skinKeys.Clear();
        foreach (var (key, hex) in ThemeSkins.Overrides(Skin))
        {
            resources[key] = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
            skinKeys.Add(key);
        }
    }

    /// <summary>
    /// Overrides the density tokens at the application level (top-level keys win over merged dictionaries). Styles that
    /// read them with DynamicResource (rows, inputs, buttons, cells) update immediately; Standard removes the overrides.
    /// </summary>
    public void ApplyDensity(DensityMode density)
    {
        Density = density;
        var resources = application.Resources;
        foreach (var key in new[] { "Density.Row", "Grid.RowHeight", "Grid.ColumnHeaderHeight", "Control.Height", "CommandBar.Height", "Density.CellPadding" }) resources.Remove(key);
        if (density == DensityMode.Compact)
        {
            resources["Density.Row"] = 24d; resources["Grid.RowHeight"] = 24d; resources["Grid.ColumnHeaderHeight"] = 28d;
            resources["Control.Height"] = 26d; resources["CommandBar.Height"] = 34d; resources["Density.CellPadding"] = new Thickness(4, 0, 4, 0);
        }
        else if (density == DensityMode.Comfortable)
        {
            resources["Density.Row"] = 34d; resources["Grid.RowHeight"] = 34d; resources["Grid.ColumnHeaderHeight"] = 38d;
            resources["Control.Height"] = 34d; resources["CommandBar.Height"] = 42d; resources["Density.CellPadding"] = new Thickness(8, 3, 8, 3);
        }

        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Apply(ThemePreference preference)
    {
        Preference = preference;
        var theme = ThemeResolver.Resolve(preference, SystemThemeReader.AppsUseLightTheme(), Skin);
        EffectiveTheme = theme;
        var dictionaries = application.Resources.MergedDictionaries;
        // Match relative (App.xaml) and absolute (pack) sources alike, otherwise the original Light dictionary would
        // stay merged after the replacement and keep overriding the selected theme.
        var current = dictionaries.FirstOrDefault(static dictionary => IsThemeDictionary(dictionary.Source));
        var replacement = new ResourceDictionary { Source = theme == AppTheme.Dark ? DarkUri : LightUri };
        if (current is null)
        {
            dictionaries.Insert(0, replacement);
        }
        else if (current.Source != replacement.Source)
        {
            dictionaries[dictionaries.IndexOf(current)] = replacement;
        }

        ApplySkinOverrides();

        foreach (Window window in application.Windows)
        {
            WindowAppearance.ApplyTitleBar(window, theme == AppTheme.Dark);
        }

        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    internal static bool IsThemeDictionary(Uri? source)
    {
        var text = source?.OriginalString.Replace('\\', '/');
        return text is not null && (text.EndsWith("Themes/Light.xaml", StringComparison.OrdinalIgnoreCase)
            || text.EndsWith("Themes/Dark.xaml", StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (Preference == ThemePreference.System && e.Category == UserPreferenceCategory.General)
        {
            application.Dispatcher.BeginInvoke(() => Apply(ThemePreference.System));
        }
    }
}

internal static class SystemThemeReader
{
    internal static bool AppsUseLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
    }
}
