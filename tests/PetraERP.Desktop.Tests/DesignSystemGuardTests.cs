using System.IO;
using System.Text.RegularExpressions;

namespace PetraERP.Desktop.Tests;

/// <summary>
/// Design system guard (Petra ERP design system, "Audit and refactor plan"): screens take every colour, the smallest font size
/// and the corner radii from Themes/*. A literal colour in a view does not follow the dark theme or the skins, and sizes 9/10
/// are below the readable minimum (Font.Caption = 11). Printing is exempt: paper is really white.
/// </summary>
public sealed partial class DesignSystemGuardTests
{
    [Fact]
    public void ViewsUseThemeTokensInsteadOfLiteralColoursAndTinyFonts()
    {
        var desktop = Path.Combine(FindRoot(), "src", "PetraERP.Desktop");
        var problems = new List<string>();
        foreach (var file in Directory.EnumerateFiles(desktop, "*.xaml", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(desktop, file).Replace('\\', '/');
            if (relative.StartsWith("obj/", StringComparison.Ordinal) || relative.StartsWith("bin/", StringComparison.Ordinal)
                || relative.StartsWith("Themes/", StringComparison.Ordinal) || relative.StartsWith("Printing/", StringComparison.Ordinal)) continue;
            var text = Comment().Replace(File.ReadAllText(file), string.Empty);
            foreach (Match match in LiteralColour().Matches(text)) problems.Add(relative + ": colour " + match.Value);
            foreach (Match match in TinyFont().Matches(text)) problems.Add(relative + ": " + match.Value);
        }
        Assert.True(problems.Count == 0, "Use DynamicResource tokens / Font.Caption:" + Environment.NewLine + string.Join(Environment.NewLine, problems));
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PetraERP.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("PetraERP.slnx not found above the test output.");
    }

    [GeneratedRegex("<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex Comment();

    [GeneratedRegex("(=\"#[0-9A-Fa-f]{3,8}\")|((Background|Foreground|BorderBrush|Fill|Stroke|Value)=\"(White|Black|Gray|Grey|LightGray|DarkGray|Silver|Red|Green|Blue|Navy|WhiteSmoke|Gainsboro|DimGray|DarkBlue|Yellow|Orange)\")")]
    private static partial Regex LiteralColour();

    [GeneratedRegex("FontSize=\"(9|10)\"")]
    private static partial Regex TinyFont();
}
