using System.IO;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Reporting;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Tests;

/// <summary>Report options are presentation-only, bounded, persisted per user and published to report windows.</summary>
public sealed class ReportDisplayOptionsTests
{
    [Fact]
    public void SanitizedForcesEveryValueIntoItsAllowedRange()
    {
        var options = new ReportDisplayOptions
        {
            FontSize = 400, Density = (ReportRowDensity)99, GridLines = (ReportGridLines)(-4), ParametersLayout = (ReportParametersLayout)42,
        }.Sanitized();
        Assert.Equal(ReportDisplayOptions.MaximumFontSize, options.FontSize);
        Assert.Equal(ReportRowDensity.Standard, options.Density);
        Assert.Equal(ReportGridLines.Both, options.GridLines);
        Assert.Equal(ReportParametersLayout.Automatic, options.ParametersLayout);
        Assert.Equal(0, new ReportDisplayOptions { FontSize = double.NaN }.Sanitized().FontSize);
        Assert.Equal(ReportDisplayOptions.MinimumFontSize, new ReportDisplayOptions { FontSize = 3 }.Sanitized().FontSize);
    }

    [Fact]
    public void HubPublishesOnlyRealChanges()
    {
        var changes = 0;
        void Handler(object? sender, EventArgs args) => changes++;
        ReportDisplayOptionsHub.Set(new ReportDisplayOptions());
        ReportDisplayOptionsHub.Changed += Handler;
        try
        {
            ReportDisplayOptionsHub.Set(new ReportDisplayOptions());
            Assert.Equal(0, changes);
            ReportDisplayOptionsHub.Set(new ReportDisplayOptions { Density = ReportRowDensity.Compact });
            Assert.Equal(1, changes);
            Assert.Equal(ReportRowDensity.Compact, ReportDisplayOptionsHub.Current.Density);
        }
        finally
        {
            ReportDisplayOptionsHub.Changed -= Handler;
            ReportDisplayOptionsHub.Set(new ReportDisplayOptions());
        }
    }

    [Fact]
    public void ViewModelRoundTripsTheOptionsAndRaisesEdited()
    {
        var model = new ReportOptionsViewModel(new LocalizationService("ar"), new ReportDisplayOptions());
        var edits = 0;
        model.Edited += (_, _) => edits++;
        model.Density = model.Densities.First(option => option.Value == ReportRowDensity.Comfortable);
        model.AlternatingRows = true;
        model.FontSize = model.FontSizes.First(option => Math.Abs(option.Value - 14) < 0.01);
        model.ParametersLayout = model.Layouts.First(option => option.Value == ReportParametersLayout.Tabs);
        Assert.Equal(4, edits);
        var current = model.Current;
        Assert.Equal(ReportRowDensity.Comfortable, current.Density);
        Assert.True(current.AlternatingRows);
        Assert.Equal(14, current.FontSize);
        Assert.Equal(ReportParametersLayout.Tabs, current.ParametersLayout);

        model.SetFrom(new ReportDisplayOptions());
        Assert.Equal(new ReportDisplayOptions(), model.Current);
    }

    [Fact]
    public void UserPreferencesPersistTheReportOptionsPerUserAndPublishThem()
    {
        var directory = Path.Combine(Path.GetTempPath(), "petra-report-options-" + Guid.NewGuid().ToString("N"));
        try
        {
            var user = Guid.NewGuid();
            var store = new UserPreferenceStore(directory);
            var options = new ReportDisplayOptions { Density = ReportRowDensity.Compact, GridLines = ReportGridLines.Horizontal, AlternatingRows = true, FontSize = 13 };
            store.Save(user, new LocalUserPreferences { Report = options });
            Assert.Equal(options, store.Read(user).Report);
            Assert.Equal(new ReportDisplayOptions(), store.Read(Guid.NewGuid()).Report);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
