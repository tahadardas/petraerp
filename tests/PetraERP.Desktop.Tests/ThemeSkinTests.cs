using PetraERP.Desktop.Theming;

namespace PetraERP.Desktop.Tests;

public sealed class ThemeSkinTests
{
    [Fact]
    public void EverySkinHasValidPaletteColours()
    {
        foreach (var skin in Enum.GetValues<ThemeSkin>().Where(s => s != ThemeSkin.Petra))
        {
            var overrides = ThemeSkins.Overrides(skin);
            Assert.NotEmpty(overrides);
            Assert.All(overrides.Values, hex => Assert.Matches("^#[0-9A-F]{6}$", hex));
            Assert.Equal(overrides["Accent"], overrides["Focus"]);
        }
        Assert.Empty(ThemeSkins.Overrides(ThemeSkin.Petra));
    }

    [Fact]
    public void OnlyBlackSkinsForceTheDarkBase()
    {
        var black = new[] { ThemeSkin.Office2007Black, ThemeSkin.Office2010Black };
        foreach (var skin in Enum.GetValues<ThemeSkin>())
        {
            Assert.Equal(black.Contains(skin), ThemeSkins.ForcesDark(skin));
            Assert.Equal(black.Contains(skin) ? AppTheme.Dark : AppTheme.Light, ThemeResolver.Resolve(ThemePreference.Light, true, skin));
        }
        // A light skin never overrides an explicit dark preference.
        Assert.Equal(AppTheme.Dark, ThemeResolver.Resolve(ThemePreference.Dark, true, ThemeSkin.Office2013));
    }

    [Fact]
    public void BlackSkinsDoNotOverrideBackgrounds()
    {
        Assert.DoesNotContain("App.Background", ThemeSkins.Overrides(ThemeSkin.Office2007Black).Keys);
        Assert.Contains("App.Background", ThemeSkins.Overrides(ThemeSkin.Office2007Blue).Keys);
    }
}
