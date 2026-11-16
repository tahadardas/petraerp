using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using PetraERP.ApiClient;
using PetraERP.Desktop.Controls;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;
using PetraERP.Desktop.Theming;

namespace PetraERP.Desktop.Tests;

/// <summary>Petra Design System behavior: numbers, loading states, notifications, preferences, palette rows and grid clipboard.</summary>
[Collection(NumberFormatterGroup.Name)]
public sealed class DesignSystemTests
{
    [Fact]
    public void NumbersFormatFromDecimalStringsWithoutRoundingOrFloatingPoint()
    {
        {
            Assert.Equal("1,234,567.89", PetraNumberFormat.Format("1234567.89"));
            Assert.Equal("(1,234.50)", PetraNumberFormat.Format("-1234.5", NumberKind.Money, true));
            Assert.Equal("0.1234", PetraNumberFormat.Format("0.1234"));
            Assert.Equal("12.5", PetraNumberFormat.Format("12.5000", NumberKind.Quantity));
            Assert.Equal("3.67250001", PetraNumberFormat.Format("3.67250001", NumberKind.Rate));
            Assert.Equal("not a number", PetraNumberFormat.Format("not a number"));
            Assert.Equal("79228162514264337593543950335.00", PetraNumberFormat.Format(decimal.MaxValue).Replace(",", string.Empty, StringComparison.Ordinal));

            Assert.True(PetraNumberFormat.TryParseInput("١٬٢٣٤٫٥٠", out var arabic)); Assert.Equal(1234.50m, arabic); Assert.Equal(2, PetraNumberFormat.Scale(arabic));
            Assert.True(PetraNumberFormat.TryParseInput("۱۲۳", out var persian)); Assert.Equal(123m, persian);
            Assert.True(PetraNumberFormat.TryParseInput("(1,000.25)", out var negative)); Assert.Equal(-1000.25m, negative);
            Assert.Equal("1234.50", PetraNumberFormat.ToWire(arabic));
            Assert.Equal("UI_TooManyDecimals", PetraNumberFormat.Validate("1.23456", NumberKind.Money, false));
            Assert.Equal("UI_NegativeNotAllowed", PetraNumberFormat.Validate("-1", NumberKind.Quantity, false));
            Assert.Equal("UI_InvalidNumber", PetraNumberFormat.Validate("1.2.3", NumberKind.Money, true));
            Assert.Null(PetraNumberFormat.Validate("", NumberKind.Money, false));
            Assert.True(PetraNumberFormat.IsNegative("(5.00)")); Assert.True(PetraNumberFormat.IsNegative("-0.01")); Assert.False(PetraNumberFormat.IsNegative("0.00"));
            Assert.Equal("-1,234.50", PetraERP.Desktop.Accounting.MoneyText.Format("-1234.50"));

            // Language format (no shared state is changed): the culture's separators, ASCII digits.
            var syria = CultureInfo.GetCultureInfo("ar-SY");
            var cultured = PetraNumberFormat.FormatWith(1234.5m, NumberKind.Money, false, NumberDisplayMode.Culture, syria);
            Assert.All(cultured.Where(char.IsDigit), digit => Assert.InRange(digit, '0', '9'));
            Assert.Contains(syria.NumberFormat.NumberDecimalSeparator, cultured, StringComparison.Ordinal);
            Assert.Equal("1,234.50", PetraNumberFormat.FormatWith(1234.5m, NumberKind.Money, false, NumberDisplayMode.Invariant, syria));
        }
    }

    [Fact]
    public void NumberConverterSupportsKindsBlankAndSignedOptions()
    {
        var converter = new NumberConverter();
        Assert.Equal(string.Empty, converter.Convert("0.00", typeof(string), "blank", CultureInfo.InvariantCulture));
        Assert.Equal("(7.00)", converter.Convert(-7m, typeof(string), "signed", CultureInfo.InvariantCulture));
        Assert.Equal("2.5", converter.Convert("2.500", typeof(string), "quantity", CultureInfo.InvariantCulture));
        Assert.Equal(true, new NegativeNumberConverter().Convert("-3", typeof(bool), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void LoadingStateShowsSkeletonsOnlyOnFirstLoadAndBlocksDuplicateSubmits()
    {
        var state = new PetraLoadingState();
        Assert.True(state.Begin(OperationKind.Read)); Assert.Equal(LoadState.Loading, state.State); Assert.True(state.ShowSkeleton);
        state.Complete(OperationKind.Read); Assert.True(state.HasLoadedOnce); Assert.Equal(LoadState.Succeeded, state.State);
        Assert.True(state.Begin(OperationKind.Read)); Assert.Equal(LoadState.Refreshing, state.State); Assert.False(state.ShowSkeleton);
        state.Complete(OperationKind.Read);
        Assert.True(state.Begin(OperationKind.Post)); Assert.True(state.IsSubmitting);
        Assert.False(state.Begin(OperationKind.Post)); Assert.False(state.Begin(OperationKind.Save)); Assert.Equal(LoadState.Posting, state.State);
        state.Fail(new TaskCanceledException("timeout", new TimeoutException()), OperationKind.Post);
        Assert.Equal(LoadState.OutcomeUnknown, state.State); Assert.True(state.IsProblem); Assert.False(state.IsSubmitting);
        state.Reset(); Assert.False(state.HasLoadedOnce); Assert.Equal(LoadState.Idle, state.State);
    }

    [Fact]
    public void TimeoutsAndLostResponsesAreNeverConfirmedFailuresForMutations()
    {
        Assert.Equal(LoadState.OutcomeUnknown, PetraLoadingState.Classify(new HttpRequestException("reset"), OperationKind.Save));
        Assert.Equal(LoadState.OutcomeUnknown, PetraLoadingState.Classify(new TimeoutException(), OperationKind.Post));
        Assert.Equal(LoadState.OutcomeUnknown, PetraLoadingState.Classify(new OperationCanceledException(), OperationKind.Post));
        Assert.Equal(LoadState.OutcomeUnknown, PetraLoadingState.Classify(new PetraApiException("x", 503, null, null!, null), OperationKind.Post));
        Assert.Equal(LoadState.Failed, PetraLoadingState.Classify(new PetraApiException("x", 409, "{\"code\":\"concurrency.conflict\"}", null!, null), OperationKind.Post));
        Assert.Equal(LoadState.Failed, PetraLoadingState.Classify(new TimeoutException(), OperationKind.Read));
        Assert.Equal(LoadState.Cancelled, PetraLoadingState.Classify(new OperationCanceledException(), OperationKind.Read));
    }

    [Fact]
    public async Task SharedOperationReportsUnknownPostingOutcomeAsWarningAndRejectsSecondSubmit()
    {
        var localization = new LocalizationService("en");
        var notifications = new NotificationCenter(localization, TimeSpan.Zero);
        var operation = new DesktopOperation(localization, notifications);
        var page = new PetraLoadingState();
        var release = new TaskCompletionSource();
        var first = operation.RunAsync(async () => { await release.Task.ConfigureAwait(true); throw new HttpRequestException("lost"); }, OperationKind.Post, page);
        Assert.True(page.IsSubmitting); Assert.Equal(LoadState.Posting, operation.Loading.State);
        Assert.False(await operation.RunAsync(() => Task.CompletedTask, OperationKind.Post, page).ConfigureAwait(true));
        release.SetResult();
        Assert.False(await first.ConfigureAwait(true));
        Assert.Equal(LoadState.OutcomeUnknown, page.State);
        Assert.Equal(NotificationKind.Warning, notifications.Items[0].Kind);
        Assert.Equal(localization["UI_OutcomeUnknownMessage"], notifications.Items[0].Message);
    }

    [Fact]
    public async Task NotificationsDeduplicateKeepHistoryCountUnreadAndRunActionsOnlyOnClick()
    {
        var localization = new LocalizationService("en");
        var center = new NotificationCenter(localization, TimeSpan.Zero);
        center.Warning("D_ServerUnavailable"); center.Warning("D_ServerUnavailable");
        var top = Assert.Single(center.Items); Assert.Equal(2, top.Count); Assert.True(top.IsRepeated);
        Assert.Single(center.History); Assert.Equal(2, center.UnreadCount);
        var ran = 0;
        var withAction = center.Show(NotificationKind.Info, "D_Toast_Info", "D_Reconnect", null, "D_Reconnect", () => { ran++; return Task.CompletedTask; });
        Assert.Equal(0, ran); Assert.True(withAction.HasAction);
        await center.InvokeActionCommand.ExecuteAsync(withAction).ConfigureAwait(true);
        Assert.Equal(1, ran); Assert.DoesNotContain(withAction, center.Items);
        center.TogglePanelCommand.Execute(null); Assert.True(center.IsPanelOpen); Assert.Equal(0, center.UnreadCount);
        center.ClearHistoryCommand.Execute(null); Assert.Empty(center.History);
    }

    [Fact]
    public void PreferencesPersistPerUserApplyDensityAndKeepFiltersCriteriaOnly()
    {
        var directory = Path.Combine(Path.GetTempPath(), "petra-prefs-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new UserPreferenceStore(directory);
            var themes = new RecordingThemes();
            var localization = new LocalizationService("ar");
            var service = new UserPreferencesService(store, themes, localization);
            var user = Guid.NewGuid(); var company = Guid.NewGuid();
            service.Load(user);
            service.SetTheme(ThemePreference.Dark); service.SetDensity(DensityMode.Comfortable);
            Assert.Equal(DensityMode.Comfortable, themes.Density);
            service.Save(new SavedFilter(Guid.NewGuid(), "TB", "trial-balance", company, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), IncludeZeroRows: true));

            var reloaded = new UserPreferencesService(store, new RecordingThemes(), localization);
            reloaded.Load(user);
            Assert.Equal(ThemePreference.Dark, reloaded.Theme); Assert.Equal(DensityMode.Comfortable, reloaded.Density);
            store.Save(Guid.Empty, new LocalUserPreferences { Numbers = NumberDisplayMode.Culture });
            Assert.Equal(NumberDisplayMode.Culture, store.Read(Guid.Empty).Numbers); // stored only; the shared formatter is not changed in tests
            var filter = Assert.Single(reloaded.Filters);
            var request = Assert.IsType<OpenTrialBalanceRequest>(filter.ToRequest());
            Assert.Equal(new DateOnly(2026, 3, 31), request.To); Assert.True(request.IncludeZeroRows);
            Assert.DoesNotContain("amount", File.ReadAllText(Path.Combine(directory, user.ToString("N") + ".json")), StringComparison.OrdinalIgnoreCase);

            // A new user starts from this computer's last appearance but never inherits another user's filters.
            var other = new UserPreferencesService(store, new RecordingThemes(), localization);
            other.Load(Guid.NewGuid());
            Assert.Equal(ThemePreference.Dark, other.Theme); Assert.Empty(other.Filters);

            reloaded.ResetAppearance();
            Assert.Equal(ThemePreference.System, reloaded.Theme); Assert.Equal(DensityMode.Standard, reloaded.Density); Assert.Empty(reloaded.Filters);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void SavedFiltersMapToTheExistingTypedDrillDownRequests()
    {
        var company = Guid.NewGuid(); var account = Guid.NewGuid();
        Assert.IsType<OpenAccountLedgerRequest>(new SavedFilter(Guid.NewGuid(), "L", "account-ledger", company, AccountId: account).ToRequest());
        Assert.Null(new SavedFilter(Guid.NewGuid(), "L", "account-ledger", company).ToRequest());
        Assert.Equal(true, Assert.IsType<OpenGeneralJournalRequest>(new SavedFilter(Guid.NewGuid(), "G", "general-journal", company, Consolidated: true).ToRequest()).Consolidated);
        Assert.Null(new SavedFilter(Guid.NewGuid(), "X", "users", company).ToRequest());
    }

    [Fact]
    public async Task PaletteRunsShellRowsAndStillRejectsUnpermittedRouteRows()
    {
        var fixture = new WorkspaceFixture(); var localization = new LocalizationService("en");
        using var workspace = new PetraERP.Desktop.Productivity.WorkspaceViewModel(fixture, localization, new TestThemes());
        await workspace.SetContextAsync(fixture.A, fixture.Branches[0]).ConfigureAwait(true);
        var navigation = new NavigationService(localization); navigation.Configure(["Ledger.Accounts.View"]);
        var navigator = new ShellNavigator(); string? route = null;
        navigator.Requested += (r, _) => { route = r; return Task.CompletedTask; };
        using var palette = new CommandPaletteViewModel(navigation, workspace, navigator, localization);
        var ran = 0;
        palette.ShellEntries = () =>
        [
            new PaletteEntry(PaletteEntryKind.Command, "cmd.test", "Use dark theme", "Command", "", "home", null, Run: () => { ran++; return Task.CompletedTask; }),
            new PaletteEntry(PaletteEntryKind.Tab, "tab:1", "Draft journal", "Open tab · unsaved changes", "", "journals", null, Run: () => Task.CompletedTask, IsDirty: true),
            new PaletteEntry(PaletteEntryKind.Filter, "filter:1", "Journals filter", "Saved filter", "", "journals", new OpenGeneralJournalRequest()),
        ];
        palette.Open();
        Assert.Contains(palette.Entries, entry => entry.Kind == PaletteEntryKind.Tab && entry.IsDirty);
        Assert.DoesNotContain(palette.Entries, entry => entry.Kind == PaletteEntryKind.Filter); // route not permitted
        await palette.InvokeAsync(palette.Entries.Single(entry => entry.Id == "cmd.test")).ConfigureAwait(true);
        Assert.Equal(1, ran); Assert.False(palette.IsOpen); Assert.Null(route);
    }

    [Fact]
    public async Task GridClipboardPastesValidatedTextAndUndoesAsOneStep()
    {
        await RunStaAsync(() =>
        {
            var rows = new ObservableCollection<PasteRow> { new() { Code = "A", Amount = "1.00" }, new() { Code = "B", Amount = "2.00" } };
            var grid = new DataGrid { ItemsSource = rows, AutoGenerateColumns = false, CanUserAddRows = false, SelectionUnit = DataGridSelectionUnit.CellOrRowHeader };
            grid.Columns.Add(new DataGridTextColumn { Binding = new Binding(nameof(PasteRow.Code)) });
            var amount = new DataGridTextColumn { Binding = new Binding(nameof(PasteRow.Amount)) };
            GridClipboard.SetNumeric(amount, true);
            grid.Columns.Add(amount);
            {
                grid.CurrentCell = new DataGridCellInfo(rows[0], grid.Columns[0]);
                var (applied, rejected) = GridClipboard.Paste(grid, "X\t١٬٢٣٤٫٥\r\nY\tbad\r\nZ\t9\r\n");
                Assert.Equal(3, applied); Assert.Equal(1, rejected); // third line has no row; "bad" amount rejected by the row's validation
                Assert.Equal("1234.5", rows[0].Amount); Assert.Equal("X", rows[0].Code); Assert.Equal("Y", rows[1].Code); Assert.Equal("2.00", rows[1].Amount);
                grid.SelectedCells.Clear(); grid.CurrentCell = new DataGridCellInfo(rows[0], amount); // unrealized grid: copy falls back to the current cell
                Assert.Equal("1234.5\r\n", GridClipboard.BuildText(grid));
                Assert.True(GridClipboard.Undo(grid));
                Assert.Equal("A", rows[0].Code); Assert.Equal("1.00", rows[0].Amount); Assert.Equal("B", rows[1].Code);
                grid.IsReadOnly = true;
                Assert.Equal((0, 0), GridClipboard.Paste(grid, "Q"));
            }
        }).ConfigureAwait(true);
    }

    [Fact]
    public async Task NumericBoxFlagsInvalidInputWithoutRewritingIt()
    {
        await RunStaAsync(() =>
        {
            var box = new PetraMoneyBox();
            box.Text = "12.345"; Assert.False(box.HasInputError);
            box.Text = "12.34567"; Assert.True(box.HasInputError); Assert.Equal("12.34567", box.Text);
            box.Text = "-5"; Assert.True(box.HasInputError);
            box.AllowNegative = true; Assert.False(box.HasInputError);
            Assert.Equal(FlowDirection.LeftToRight, box.FlowDirection);
            Assert.False(PetraNumericBox.IsNumericCharacter('a', true)); Assert.True(PetraNumericBox.IsNumericCharacter('٣', false));
            Assert.False(PetraNumericBox.IsNumericCharacter('-', false));
        }).ConfigureAwait(true);
    }

    private static Task RunStaAsync(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { action(); completion.SetResult(); }
            catch (Exception exception) { completion.SetException(exception); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
    }

    private sealed class RecordingThemes : IThemeService
    {
        public ThemePreference Preference { get; private set; }
        public AppTheme EffectiveTheme => ThemeResolver.Resolve(Preference, true);
        public DensityMode Density { get; private set; } = DensityMode.Standard;
        public event EventHandler? ThemeChanged;
        public void Apply(ThemePreference preference) { Preference = preference; ThemeChanged?.Invoke(this, EventArgs.Empty); }
        public void ApplyDensity(DensityMode density) => Density = density;
    }

    /// <summary>Row with property-level validation, like a draft line: an amount must be a decimal.</summary>
    private sealed class PasteRow : INotifyPropertyChanged, INotifyDataErrorInfo
    {
        private string code = string.Empty, amount = string.Empty;
        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;
        public string Code { get => code; set { code = value; PropertyChanged?.Invoke(this, new(nameof(Code))); } }
        public string Amount { get => amount; set { amount = value; PropertyChanged?.Invoke(this, new(nameof(Amount))); ErrorsChanged?.Invoke(this, new(nameof(Amount))); } }
        public bool HasErrors => !decimal.TryParse(Amount, NumberStyles.Number, CultureInfo.InvariantCulture, out _);
        public IEnumerable GetErrors(string? propertyName) =>
            propertyName == nameof(Amount) && HasErrors ? new[] { "invalid" } : Array.Empty<string>();
    }
}
