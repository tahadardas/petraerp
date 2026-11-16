namespace PetraERP.Desktop.Theming;

public enum ThemePreference
{
    System = 0,
    Light = 1,
    Dark = 2,
}

/// <summary>Row and control height mode. Standard is the established dense accounting layout (28 px rows).</summary>
public enum DensityMode
{
    Compact = 0,
    Standard = 1,
    Comfortable = 2,
}

public enum AppTheme
{
    Light = 0,
    Dark = 1,
}

public static class ThemeResolver
{
    public static AppTheme Resolve(ThemePreference preference, bool systemUsesLightTheme) => preference switch
    {
        ThemePreference.Light => AppTheme.Light,
        ThemePreference.Dark => AppTheme.Dark,
        _ => systemUsesLightTheme ? AppTheme.Light : AppTheme.Dark,
    };

    /// <summary>Black skins always render on the dark base, whatever the Light/Dark/System preference says.</summary>
    public static AppTheme Resolve(ThemePreference preference, bool systemUsesLightTheme, ThemeSkin skin) =>
        ThemeSkins.ForcesDark(skin) ? AppTheme.Dark : Resolve(preference, systemUsesLightTheme);

    public static ThemePreference Next(ThemePreference current) => current switch
    {
        ThemePreference.System => ThemePreference.Light,
        ThemePreference.Light => ThemePreference.Dark,
        _ => ThemePreference.System,
    };
}

public interface IThemeService
{
    ThemePreference Preference { get; }

    AppTheme EffectiveTheme { get; }

    event EventHandler? ThemeChanged;

    void Apply(ThemePreference preference);

    /// <summary>Current density (implementations without density support report Standard).</summary>
    DensityMode Density => DensityMode.Standard;

    /// <summary>Switches row/control heights instantly; the default implementation ignores the request.</summary>
    void ApplyDensity(DensityMode density)
    {
    }

    /// <summary>Current program appearance (implementations without skin support report the default Petra look).</summary>
    ThemeSkin Skin => ThemeSkin.Petra;

    /// <summary>Applies a program appearance on top of the Light/Dark base; the default implementation ignores the request.</summary>
    void ApplySkin(ThemeSkin skin)
    {
    }
}
