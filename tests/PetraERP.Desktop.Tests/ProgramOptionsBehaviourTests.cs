using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Tests;

/// <summary>The warning options of the program options window really change what is asked (they never touch a server rule).</summary>
public sealed class ProgramOptionsBehaviourTests
{
    private sealed class Answers(bool answer = true) : IUserConfirmation
    {
        public List<string> Asked { get; } = [];
        public Task<bool> ConfirmAsync(string key) { Asked.Add(key); return Task.FromResult(answer); }
    }

    [Fact]
    public void NewOptionsHaveTheAlAmeenDefaults()
    {
        var defaults = new EntryOptions();
        Assert.True(defaults.PastDateWarning);
        Assert.True(defaults.DuplicateItemWarning);
        Assert.False(defaults.ConfirmOnSave);
        Assert.False(defaults.ConfirmOnExit);
    }

    [Fact]
    public async Task APastDateIsAskedAboutOnlyWhileTheOptionIsOn()
    {
        var localization = new LocalizationService("en");
        var yesterday = DateTime.Today.AddDays(-1);
        var asked = new Answers(); var rejected = new List<string>();
        Assert.True(await DocumentDateGuard.AllowsAsync(yesterday, null, localization, asked, rejected.Add, new EntryOptions { PastDateWarning = true }));
        Assert.Equal(["D_ConfirmPastDate"], asked.Asked);

        var silent = new Answers();
        Assert.True(await DocumentDateGuard.AllowsAsync(yesterday, null, localization, silent, rejected.Add, new EntryOptions { PastDateWarning = false }));
        Assert.Empty(silent.Asked);

        var refused = new Answers(false);
        Assert.False(await DocumentDateGuard.AllowsAsync(yesterday, null, localization, refused, rejected.Add, new EntryOptions { PastDateWarning = true }));
        Assert.Empty(rejected);
    }

    [Fact]
    public async Task ARepeatedItemAndTheGeneralSaveConfirmationAreAskedIndependently()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        Assert.False(EntrySaveGuard.HasDuplicateItem([a, b, null, null]));
        Assert.True(EntrySaveGuard.HasDuplicateItem([a, b, a]));

        var onlyDuplicate = new Answers();
        Assert.True(await EntrySaveGuard.AllowsAsync([a, a], onlyDuplicate, new EntryOptions()));
        Assert.Equal(["D_ConfirmDuplicateItem"], onlyDuplicate.Asked);

        var both = new Answers();
        Assert.True(await EntrySaveGuard.AllowsAsync([a, a], both, new EntryOptions { ConfirmOnSave = true }));
        Assert.Equal(["D_ConfirmDuplicateItem", "D_ConfirmSave"], both.Asked);

        var off = new Answers();
        Assert.True(await EntrySaveGuard.AllowsAsync([a, a], off, new EntryOptions { DuplicateItemWarning = false }));
        Assert.Empty(off.Asked);

        var declined = new Answers(false);
        Assert.False(await EntrySaveGuard.AllowsAsync([a, b], declined, new EntryOptions { ConfirmOnSave = true }));
        Assert.Equal(["D_ConfirmSave"], declined.Asked);
    }

    [Fact]
    public void TheWarningOptionsAreListedInTheWindowAndLocalizedInBothLanguages()
    {
        var keys = new[] { "OPT_E_PastDate", "OPT_E_DuplicateItem", "OPT_E_ConfirmSave", "OPT_E_ConfirmExit", "D_ConfirmDuplicateItem", "D_ConfirmSave", "D_ConfirmExit" };
        foreach (var language in new[] { "en", "ar" })
        {
            var localization = new LocalizationService(language);
            Assert.All(keys, key => Assert.NotEqual(key, localization[key]));
        }

        var listed = ProgramOptionsCatalog.Entries.Where(entry => entry.CategoryId == "warnings").Select(entry => entry.ElementName).ToArray();
        Assert.Contains("PastDateField", listed); Assert.Contains("DuplicateItemField", listed);
        Assert.Contains("ConfirmSaveField", listed); Assert.Contains("ConfirmExitField", listed);
    }
}
