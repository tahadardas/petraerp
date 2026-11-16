using System.IO;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Tests;

public sealed class EntryOptionsTests
{
    [Theory]
    [InlineData(0, EntryOptions.MinRows)]
    [InlineData(5000, EntryOptions.MaxRows)]
    [InlineData(30, 30)]
    public void TheNumberOfReadyLinesStaysWithinTheSupportedRange(int rows, int expected) =>
        Assert.Equal(expected, new EntryOptions { InvoiceRows = rows }.Sanitized().InvoiceRows);

    [Fact]
    public void DefaultsKeepTheDocumentedEntryBehaviour()
    {
        var defaults = new EntryOptions();
        Assert.True(defaults.MaxStockWarning);
        Assert.False(defaults.SalesStartsOnCredit);
        Assert.True(defaults.PurchasesStartOnCredit);
        Assert.Equal(50, defaults.InvoiceRows);
        Assert.True(defaults.AutoGenerateCodeOnAdd);
        Assert.False(defaults.ConfirmOnAdd);
        Assert.True(defaults.PreventModifyingCodeWithMovements);
        Assert.True(defaults.PreventEditingPostedDocuments);
        Assert.True(defaults.WarnOnBelowCost);
        Assert.False(defaults.WarnOnPriceChange);
        Assert.False(defaults.ConfirmOnEdit);
        Assert.True(defaults.ConfirmOnDiscard);
    }

    [Fact]
    public void EntryOptionsAreSavedPerUserAndOldFilesWithoutThemStillLoad()
    {
        var directory = Path.Combine(Path.GetTempPath(), "petra-entry-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new UserPreferenceStore(directory);
            var user = Guid.NewGuid();
            store.Save(user, new LocalUserPreferences
            {
                Entry = new EntryOptions
                {
                    MaxStockWarning = false,
                    SalesStartsOnCredit = true,
                    InvoiceRows = 20,
                    AutoGenerateCodeOnAdd = false,
                    ConfirmOnAdd = true,
                    PreventModifyingCodeWithMovements = false,
                    PreventEditingPostedDocuments = false,
                    WarnOnBelowCost = false,
                    WarnOnPriceChange = true,
                    ConfirmOnEdit = true,
                    ConfirmOnDiscard = true,
                }
            });
            var read = store.Read(user).Entry;
            Assert.False(read.MaxStockWarning);
            Assert.True(read.SalesStartsOnCredit);
            Assert.Equal(20, read.InvoiceRows);
            Assert.False(read.AutoGenerateCodeOnAdd);
            Assert.True(read.ConfirmOnAdd);
            Assert.False(read.PreventModifyingCodeWithMovements);
            Assert.False(read.PreventEditingPostedDocuments);
            Assert.False(read.WarnOnBelowCost);
            Assert.True(read.WarnOnPriceChange);
            Assert.True(read.ConfirmOnEdit);
            Assert.True(read.ConfirmOnDiscard);

            Directory.CreateDirectory(directory);
            var legacy = Guid.NewGuid();
            File.WriteAllText(Path.Combine(directory, legacy.ToString("N") + ".json"), "{ \"Theme\": \"Dark\" }");
            var old = store.Read(legacy);
            Assert.Equal(50, old.Entry.InvoiceRows);
            Assert.True(old.Entry.MaxStockWarning);
            Assert.True(old.Entry.AutoGenerateCodeOnAdd);
            Assert.False(old.Entry.ConfirmOnAdd);
            Assert.True(old.Entry.PreventModifyingCodeWithMovements);
            Assert.True(old.Entry.PreventEditingPostedDocuments);
            Assert.True(old.Entry.WarnOnBelowCost);
            Assert.False(old.Entry.WarnOnPriceChange);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private sealed class MockConfirmation : IUserConfirmation
    {
        public List<string> AskedKeys { get; } = [];
        public bool ResultToReturn { get; set; } = true;

        public Task<bool> ConfirmAsync(string key)
        {
            AskedKeys.Add(key);
            return Task.FromResult(ResultToReturn);
        }
    }

    [Fact]
    public async Task EntrySaveGuardEnforcesAdditionPolicyConfirmations()
    {
        var mock = new MockConfirmation();
        var options = new EntryOptions { ConfirmOnAdd = true };

        // When isNew == true, D_ConfirmAdd is prompted
        var allowed = await EntrySaveGuard.AllowsAsync([], mock, options, isNew: true);
        Assert.True(allowed);
        Assert.Contains("D_ConfirmAdd", mock.AskedKeys);

        // When user rejects confirmation, save is blocked
        mock.AskedKeys.Clear();
        mock.ResultToReturn = false;
        var rejected = await EntrySaveGuard.AllowsAsync([], mock, options, isNew: true);
        Assert.False(rejected);
        Assert.Contains("D_ConfirmAdd", mock.AskedKeys);

        // When isNew == false and ConfirmOnAdd is true, D_ConfirmAdd is not prompted
        mock.AskedKeys.Clear();
        mock.ResultToReturn = true;
        var editAllowed = await EntrySaveGuard.AllowsAsync([], mock, options, isNew: false);
        Assert.True(editAllowed);
        Assert.DoesNotContain("D_ConfirmAdd", mock.AskedKeys);
    }

    [Fact]
    public async Task EntrySaveGuardEnforcesEditingPolicyConfirmations()
    {
        var mock = new MockConfirmation();
        var options = new EntryOptions { ConfirmOnEdit = true };

        // When isNew == false, D_ConfirmEdit is prompted
        var allowed = await EntrySaveGuard.AllowsAsync([], mock, options, isNew: false);
        Assert.True(allowed);
        Assert.Contains("D_ConfirmEdit", mock.AskedKeys);

        // When user rejects edit confirmation, save is blocked
        mock.AskedKeys.Clear();
        mock.ResultToReturn = false;
        var rejected = await EntrySaveGuard.AllowsAsync([], mock, options, isNew: false);
        Assert.False(rejected);
        Assert.Contains("D_ConfirmEdit", mock.AskedKeys);

        // When isNew == true, D_ConfirmEdit is not prompted
        mock.AskedKeys.Clear();
        mock.ResultToReturn = true;
        var newAllowed = await EntrySaveGuard.AllowsAsync([], mock, options, isNew: true);
        Assert.True(newAllowed);
        Assert.DoesNotContain("D_ConfirmEdit", mock.AskedKeys);
    }

    [Fact]
    public async Task EntrySaveGuardEnforcesPriceAdvisoryPolicies()
    {
        var mock = new MockConfirmation();
        var options = new EntryOptions { WarnOnBelowCost = true, WarnOnPriceChange = true };

        // Selling below cost triggers warning
        var allowed = await EntrySaveGuard.AllowsPriceAsync(isBelowCost: true, isChangedFromQuote: false, mock, options);
        Assert.True(allowed);
        Assert.Contains("D_ConfirmBelowCost", mock.AskedKeys);

        // Price change triggers warning
        mock.AskedKeys.Clear();
        var priceAllowed = await EntrySaveGuard.AllowsPriceAsync(isBelowCost: false, isChangedFromQuote: true, mock, options);
        Assert.True(priceAllowed);
        Assert.Contains("D_ConfirmPriceChange", mock.AskedKeys);

        // Rejection blocks
        mock.AskedKeys.Clear();
        mock.ResultToReturn = false;
        var blocked = await EntrySaveGuard.AllowsPriceAsync(isBelowCost: true, isChangedFromQuote: false, mock, options);
        Assert.False(blocked);
    }
}
