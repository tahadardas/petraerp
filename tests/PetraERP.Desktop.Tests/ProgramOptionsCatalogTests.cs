using System.IO;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Tests;

/// <summary>The program options window lists only real options, finds them with Arabic-tolerant search and matches its XAML panels.</summary>
public sealed class ProgramOptionsCatalogTests
{
    private static string WindowXaml()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PetraERP.slnx"))) directory = directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory!.FullName, "src", "PetraERP.Desktop", "Shell", "ProgramOptionsWindow.xaml"));
    }

    [Fact]
    public void CategoriesAndEntriesAreConsistent()
    {
        var ids = ProgramOptionsCatalog.Categories.Select(category => category.Id).ToArray();
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ProgramOptionsCatalog.Entries, entry => Assert.Contains(entry.CategoryId, ids));
        var names = ProgramOptionsCatalog.Entries.Select(entry => entry.ElementName).ToArray();
        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ProgramOptionsCatalog.Categories.Where(category => category.Scope == OptionScope.Link), category => Assert.NotNull(category.Route));
    }

    [Fact]
    public void EveryCategoryPanelAndFieldExistsInTheWindowXaml()
    {
        var xaml = WindowXaml();
        foreach (var category in ProgramOptionsCatalog.Categories) Assert.Contains($"x:Name=\"Section_{category.Id}\"", xaml, StringComparison.Ordinal);
        foreach (var entry in ProgramOptionsCatalog.Entries) Assert.Contains($"x:Name=\"{entry.ElementName}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryCaptionIsTranslatedInBothLanguages()
    {
        var keys = ProgramOptionsCatalog.Categories.Select(category => category.TitleKey).Concat(ProgramOptionsCatalog.Entries.Select(entry => entry.TitleKey)).ToArray();
        foreach (var language in new[] { "ar", "en" })
        {
            var localization = new LocalizationService(language);
            Assert.All(keys, key => Assert.NotEqual(key, localization[key]));
        }
    }

    [Fact]
    public void SearchIgnoresArabicVariantsAndFindsOptionsWithTheirPath()
    {
        var localization = new LocalizationService("ar");
        var results = ProgramOptionsCatalog.Search("الارقام", key => localization[key]);
        Assert.Contains(results, result => result.Category.Id == "numbers" && result.Entry is null);
        var numbering = ProgramOptionsCatalog.Search("ترقيم", key => localization[key]);
        Assert.Contains(numbering, result => result.Entry?.ElementName == "ManualNumberingField" && result.Path.Contains('◂', StringComparison.Ordinal));
        Assert.Equal(ProgramOptionsCatalog.Search("الأرقام", key => localization[key]).Count, results.Count);
    }

    [Fact]
    public void EmptyQueriesFindNothingAndUnavailableCategoriesAreHidden()
    {
        var localization = new LocalizationService("ar");
        Assert.Empty(ProgramOptionsCatalog.Search("  ", key => localization[key]));
        Assert.Empty(ProgramOptionsCatalog.Search(null, key => localization[key]));
        var withoutCompany = ProgramOptionsCatalog.Search("ترقيم", key => localization[key], category => category.Scope != OptionScope.Company);
        Assert.Empty(withoutCompany);
    }

    [Theory]
    [InlineData("أإآا", "اااا")]
    [InlineData("ةى", "هي")]
    [InlineData("الحِسابات", "الحسابات")]
    public void NormalizeUnifiesLetterVariantsAndDropsMarks(string input, string expected) =>
        Assert.Equal(expected, ProgramOptionsCatalog.Normalize(input));
}
