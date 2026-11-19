using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PetraERP.ApiClient;
using PetraERP.Desktop.Patterns;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Tests;

/// <summary>
/// Renders the real pattern card (voucher settings + the server's effective preview) to a PNG for the owner's visual review.
/// Explicit: it writes into docs/closure/voucher-patterns/screens and is not part of the default run.
/// </summary>
[Collection(WpfApplicationGroup.Name)]
public sealed class PatternVoucherSettingsVisualTests
{
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    [Fact(Explicit = true)]
    public async Task RenderThePatternCardVoucherSettingsForReview()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Task.Run(() => WpfApplicationHost.Run(Render), cancellationToken).WaitAsync(TimeSpan.FromSeconds(300), cancellationToken).ConfigureAwait(true);
    }

    private static void Render()
    {
        WpfApplicationHost.ResetResources();
        try
        {
            WpfApplicationHost.MergeThemes("Themes/Light.xaml", "Themes/Dense.xaml", "Themes/Components.xaml");
            var fixture = new ShellFixture();
            fixture.User.Permissions = ["Patterns.Documents.View", "Patterns.Documents.Manage", "Patterns.Documents.Publish", "Ledger.Accounts.View", "Ledger.CostCenters.View"];
            var data = new AccountingFixture();
            var expense = Guid.NewGuid(); var center = Guid.NewGuid();
            data.Accounts.Add(new() { Id = expense, Code = "5211", NameArabic = "مصاريف تشغيل", NameEnglish = "Operating expenses", PostingAllowed = true, IsActive = true });
            var patternId = Guid.NewGuid();
            var revision = new PatternRevisionResponse
            {
                Id = Guid.NewGuid(), PatternId = patternId, RevisionNumber = 3, State = PatternRevisionState.Published, EffectiveFromUtc = DateTimeOffset.UtcNow,
                Configuration = new PatternConfigurationDto
                {
                    SchemaVersion = 2,
                    Voucher = new PatternVoucherPolicyDto
                    {
                        CaptionMode = VoucherCaptionMode.FromTo, PostingMode = VoucherPostingMode.ManualOnly, DenyMutationsOnOrBefore = new DateOnly(2026, 10, 31),
                        Lookup = new PatternLookupPolicyDto { EligibleAccountRootIds = [expense], EligibleCostCenterIds = [] },
                    },
                    NumberingPolicy = new PatternNumberingPolicyDto { Scope = PatternNumberingScope.Branch, Prefix = "PV-", MinimumDigits = 5 },
                    ApprovalPolicy = new PatternApprovalPolicyDto { RequiresApproval = true, ThresholdAmount = "50.0000" },
                    LineFields = [new PatternFieldRuleDto { FieldName = "CostCenter", IsVisible = true, IsRequired = true, DisplayOrder = 1 }],
                },
            };
            data.Patterns.Add(new DocumentPatternResponse
            {
                Id = patternId, CompanyId = fixture.Company.Id, Code = "PV-CASH", ShortCode = "PV", NameArabic = "سند دفع نقدي", NameEnglish = "Cash payment",
                OperationKind = BusinessOperationKind.CashPayment, Group = "سندات", IsActive = true, IsRunnable = true, Version = "AAAAAAAAB9A=", BranchIds = [], ActiveRevision = revision,
            });
            data.Revisions.Add(revision);
            using var shell = fixture.Create(new AdministrationFixture(), accounting: data);
            shell.UserName = "admin"; shell.Password = "test"; shell.LoginCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            shell.NavigateToAsync("patterns", new OpenPatternCardRequest(patternId)).GetAwaiter().GetResult();
            var card = Assert.IsType<PatternCardViewModel>(shell.CurrentView);
            card.LoadEffectivePreviewCommand.ExecuteAsync(null).GetAwaiter().GetResult();

            var view = new PatternCardView { DataContext = card, FlowDirection = shell.Localization.FlowDirection };
            var window = new Window { Content = view, Width = 1200, Height = 1500, Left = -10000, ShowActivated = false };
            window.Show(); Pump();
            try
            {
                var tabs = Assert.Single(Descendants<TabControl>(view));
                var found = false;
                for (var index = 0; index < tabs.Items.Count && !found; index++)
                {
                    tabs.SelectedIndex = index; Pump(); window.UpdateLayout();
                    found = Descendants<TextBlock>(view).Any(b => b.Text == "إعدادات السند");
                }

                Assert.True(found, "The voucher settings card was not found on any tab.");
                var viewer = Descendants<ScrollViewer>(view).FirstOrDefault(s => Descendants<TextBlock>(s).Any(b => b.Text == "إعدادات السند"));
                var title = Descendants<TextBlock>(view).First(b => b.Text == "إعدادات السند");
                title.BringIntoView(); Pump(); window.UpdateLayout();
                var folder = Path.Combine(FindRoot(), "docs", "closure", "voucher-patterns", "screens");
                Directory.CreateDirectory(folder);
                void Save(string name)
                {
                    var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(window);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(Path.Combine(folder, name)); encoder.Save(stream);
                }

                Save("pattern-card-voucher-settings-ar-light.png");
                // The settings card sits below the numbering and approval cards: scroll to its end for the second view (lookup lists + server preview).
                viewer?.ScrollToEnd(); Pump(); window.UpdateLayout(); Pump();
                Save("pattern-card-voucher-settings-ar-light-bottom.png");
            }
            finally { window.Close(); Pump(); }
        }
        finally { WpfApplicationHost.ResetResources(); }
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PetraERP.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
