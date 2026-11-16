namespace PetraERP.Desktop.Theming;

/// <summary>
/// Program appearance ("مظهر البرنامج"), named after the skin list of Al-Ameen's Window menu. A skin is only a colour palette laid over
/// the Light or Dark base: it never changes layout, numbers or accounting behaviour. The palettes are Petra's own approximations of each
/// family (blue / silver / black / aqua ...), not copies of any third-party skin.
/// </summary>
public enum ThemeSkin
{
    Petra = 0,
    Office2003 = 1,
    Office2007Blue = 2,
    Office2007Silver = 3,
    Office2007Black = 4,
    Office2007Aqua = 5,
    Office2010Blue = 6,
    Office2010Silver = 7,
    Office2010Black = 8,
    Office2013 = 9,
    Office2013White = 10,
    Windows7Scenic = 11,
    VisualStudio2010 = 12,
}

/// <param name="ForceDark">Black skins always use the Dark base; the others follow the Light/Dark/System preference.</param>
/// <param name="Background">Window background for light skins; null keeps the base theme's.</param>
public sealed record SkinPalette(bool ForceDark, string TopBar, string TopBarHover, string TopBarText, string TopBarInput, string Accent, string AccentHover,
    string AccentSubtle, string? Background, string GridHeader, string Selection);

public static class ThemeSkins
{
    private static readonly Dictionary<ThemeSkin, SkinPalette> Palettes = new()
    {
        [ThemeSkin.Office2003] = new(false, "#3E6AAA", "#2F5A9A", "#FFFFFF", "#5A84BE", "#316AC5", "#2757A8", "#C1D2EE", "#EAF1FB", "#C9DBF4", "#C1D2EE"),
        [ThemeSkin.Office2007Blue] = new(false, "#2F5F9E", "#244E86", "#FFFFFF", "#4B79B4", "#2A5DA8", "#214C8A", "#DCE9F8", "#DFE9F5", "#C5D9F1", "#FFE6A0"),
        [ThemeSkin.Office2007Silver] = new(false, "#5F6B7D", "#4D586A", "#FFFFFF", "#7C8798", "#566377", "#444F60", "#E4E7EC", "#E8EAEE", "#D3D7DE", "#FFE6A0"),
        [ThemeSkin.Office2007Black] = new(true, "#161616", "#2A2A2A", "#FFFFFF", "#333333", "#F0A030", "#D88A1E", "#3A2E18", null, "#2B2B2B", "#4A3A1C"),
        [ThemeSkin.Office2007Aqua] = new(false, "#1E7F85", "#17676C", "#FFFFFF", "#3D9AA0", "#2B9AA0", "#1E7F85", "#D6F0F1", "#E3F4F5", "#BFE6E8", "#FFE6A0"),
        [ThemeSkin.Office2010Blue] = new(false, "#2C5A91", "#22497A", "#FFFFFF", "#4A76AB", "#2E6CB0", "#235793", "#DDEAF7", "#E8EFF8", "#CADBF0", "#FDE79A"),
        [ThemeSkin.Office2010Silver] = new(false, "#697587", "#566173", "#FFFFFF", "#8791A1", "#5D6B80", "#4A576A", "#E6E9EE", "#EDEFF2", "#D8DCE3", "#FDE79A"),
        [ThemeSkin.Office2010Black] = new(true, "#1B1B1B", "#303030", "#FFFFFF", "#3A3A3A", "#4F8FD6", "#3B78BA", "#1F2F44", null, "#2E2E2E", "#27405F"),
        [ThemeSkin.Office2013] = new(false, "#2B579A", "#214786", "#FFFFFF", "#4A72B2", "#2B579A", "#214786", "#DCE5F3", "#EDEDED", "#DADADA", "#CDE6F7"),
        [ThemeSkin.Office2013White] = new(false, "#FFFFFF", "#E6E6E6", "#2B2B2B", "#EFEFEF", "#2B579A", "#214786", "#DCE5F3", "#FFFFFF", "#F0F0F0", "#CDE6F7"),
        [ThemeSkin.Windows7Scenic] = new(false, "#4F7FB8", "#3E6A9D", "#FFFFFF", "#6E98C8", "#3C7FB1", "#2F6893", "#DCEBF6", "#F0F4FA", "#D5E3F2", "#CFE6F9"),
        [ThemeSkin.VisualStudio2010] = new(false, "#293955", "#1F2D44", "#FFFFFF", "#46587A", "#3C5A8E", "#2E4776", "#D6DFEE", "#CFD9E8", "#BCC9DE", "#FFE8A6"),
    };

    /// <summary>Palette of a skin; null for the default Petra look (no overrides).</summary>
    public static SkinPalette? Get(ThemeSkin skin) => Palettes.GetValueOrDefault(skin);

    public static bool ForcesDark(ThemeSkin skin) => Get(skin)?.ForceDark == true;

    /// <summary>Resource key → colour (#RRGGBB) overrides for a skin; empty for the default look.</summary>
    public static IReadOnlyDictionary<string, string> Overrides(ThemeSkin skin)
    {
        if (Get(skin) is not { } p) return new Dictionary<string, string>();
        var map = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["TopBar"] = p.TopBar, ["Brand.Navy"] = p.TopBar, ["Brand.NavyHover"] = p.TopBarHover, ["TopBar.Text"] = p.TopBarText, ["Text.OnBrand"] = p.TopBarText, ["TopBar.Input"] = p.TopBarInput,
            ["Accent"] = p.Accent, ["Accent.Hover"] = p.AccentHover, ["Accent.Subtle"] = p.AccentSubtle,
            ["Brand.Teal"] = p.Accent, ["Brand.TealHover"] = p.AccentHover, ["Brand.TealSubtle"] = p.AccentSubtle, ["Teal"] = p.Accent, ["Teal.Subtle"] = p.AccentSubtle,
            ["Focus"] = p.Accent, ["Grid.ActiveCell"] = p.Accent, ["Grid.Header"] = p.GridHeader, ["Grid.Selection"] = p.Selection, ["Selection"] = p.Selection,
        };
        if (p.Background is { } bg) { map["App.Background"] = bg; map["Surface"] = bg; map["Canvas"] = bg; }
        return map;
    }
}
