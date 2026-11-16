using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PetraERP.ApiClient;
using PetraERP.Desktop.DocumentProcessing;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Tests;

/// <summary>
/// P32: renders the real posting-management view, proves its buttons follow the confirmation gate, and proves the
/// results grid is bound to the posted rows with the required per-document columns.
/// </summary>
[Collection(WpfApplicationGroup.Name)]
public sealed class PostingManagementViewTests
{
    [Fact]
    public async Task P32PostingManagementViewShowsButtonsAndRenderedResults()
    {
        await WpfApplicationHost.RunAsync(async () =>
        {
            WpfApplicationHost.ResetResources();
            WpfApplicationHost.MergeThemes("Themes/Light.xaml", "Themes/Dense.xaml", "Themes/Components.xaml");
            var localization = new LocalizationService("ar");
            var client = new ViewClient();
            var model = new PostingManagementViewModel(client, new Confirm(), new DesktopOperation(localization), localization);
            await model.ActivateAsync(new ShellContext(
                    new CompanyContextResponse { Id = Guid.NewGuid(), Code = "A", NameArabic = "شركة أ", BaseCurrencyCode = "SYP" },
                    new BranchContextResponse { Id = Guid.NewGuid(), Code = "MAIN", NameArabic = "رئيسي" },
                    new HashSet<string>(StringComparer.Ordinal) { PostingManagementViewModel.ManagePermission }),
                new BulkPostingNavigation(BulkPostingDocumentType.SalesInvoice),
                TestContext.Current.CancellationToken).ConfigureAwait(true);

            var window = new Window
            {
                Content = new PostingManagementView { DataContext = model },
                Width = 1280,
                Height = 800,
                Left = -20000,
                Top = -20000,
            };
            try
            {
                window.Show();
                window.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

                var buttons = Descendants<Button>(window).ToList();
                Assert.Contains(buttons, b => ReferenceEquals(b.Command, model.PreviewCommand) && b.IsEnabled);
                Assert.Contains(buttons, b => ReferenceEquals(b.Command, model.PostSelectedCommand));
                Assert.Contains(buttons, b => ReferenceEquals(b.Command, model.ResumeCommand));
                Assert.Contains(buttons, b => ReferenceEquals(b.Command, model.RefreshResultsCommand));
                var postAll = Assert.Single(buttons, b => ReferenceEquals(b.Command, model.PostAllCommand));
                Assert.False(postAll.IsEnabled);

                model.IsConfirmed = true;
                window.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.True(postAll.IsEnabled);

                await model.PostAllCommand.ExecuteAsync(null).ConfigureAwait(true);
                window.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

                Assert.True(model.HasBatch);
                Assert.Equal(1, model.Posted);
                var row = Assert.Single(model.Results);
                Assert.Equal("INV-1001", row.DocumentNumber);
                Assert.Equal("مرحّل", row.NewStatus);

                var tabs = Assert.Single(Descendants<TabControl>(window));
                tabs.SelectedIndex = 1;
                window.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

                var grid = Assert.Single(Descendants<DataGrid>(window), g => ReferenceEquals(g.ItemsSource, model.Results));
                Assert.Equal(14, grid.Columns.Count);
                Assert.Contains(grid.Columns, c => string.Equals(c.Header as string, "رقم الترحيل", StringComparison.Ordinal));
                Assert.Contains(grid.Columns, c => string.Equals(c.Header as string, "النتيجة", StringComparison.Ordinal));
                Assert.Single(grid.Items);
                var resultsButtons = Descendants<Button>(window).ToList();
                var export = Assert.Single(resultsButtons, b => string.Equals(b.Content as string, localization["D_BulkExportExcel"], StringComparison.Ordinal));
                Assert.True(export.IsEnabled);
                Assert.Contains(resultsButtons, b => string.Equals(b.Content as string, localization["D_BulkPrint"], StringComparison.Ordinal) && b.IsEnabled);
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromSeconds(90), TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private sealed class Confirm : IUserConfirmation
    {
        public Task<bool> ConfirmAsync(string key) => Task.FromResult(true);
    }

    private sealed class ViewClient : IDocumentPostingClient
    {
        private static readonly Guid BatchId = Guid.NewGuid();
        private static readonly Guid DocumentId = Guid.NewGuid();

        public Task<BulkPostingPreviewResponse> PreviewAsync(Guid companyId, Guid branchId, BulkPostingFilterDto filter,
            CancellationToken cancellationToken) => Task.FromResult(new BulkPostingPreviewResponse
        {
            TotalExamined = 1, ReadyToPost = 0, NeedsApproval = 1, Ineligible = 0,
            Items = [new BulkPostingPreviewItemDto
            {
                DocumentType = BulkPostingDocumentType.SalesInvoice,
                DocumentId = DocumentId,
                DocumentNumber = "INV-1001",
                DocumentDateUtc = new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero),
                PartyName = "عميل الاختبار",
                Amount = "200.0000",
                CurrencyCode = "SYP",
                CurrentStatus = "Draft",
                Classification = BulkPostingClassification.CanBeApprovedAutomatically,
                Postable = true,
                Reason = "جاهز",
            }],
        });

        public Task<BulkPostingBatchDto> StartAsync(Guid companyId, Guid branchId, StartBulkPostingRequest request,
            CancellationToken cancellationToken) => Task.FromResult(new BulkPostingBatchDto
        {
            BatchId = BatchId, Status = BulkPostingBatchStatus.Completed, TotalExamined = 1, AlreadyPosted = 0,
            Eligible = 1, Posted = 1, Skipped = 0, Failed = 0, Unknown = 0, Message = "رُحّل 1 من 1.",
        });

        public Task<BulkPostingBatchDto> GetAsync(Guid companyId, Guid branchId, Guid batchId,
            CancellationToken cancellationToken) => throw new InvalidOperationException("No batch requested.");

        public Task<BulkPostingBatchDto> ResumeAsync(Guid companyId, Guid branchId, Guid batchId, bool retryFailed,
            CancellationToken cancellationToken) => throw new InvalidOperationException("No batch requested.");

        public Task<BulkPostingResultsResponse> GetResultsAsync(Guid companyId, Guid branchId, Guid batchId,
            BulkPostingItemStatus? status, BulkPostingDocumentType? documentType, int page, int pageSize,
            CancellationToken cancellationToken) => Task.FromResult(new BulkPostingResultsResponse
        {
            Page = page,
            PageSize = pageSize,
            TotalCount = 1,
            Batch = new BulkPostingBatchDto
            {
                BatchId = BatchId, Status = BulkPostingBatchStatus.Completed, TotalExamined = 1, AlreadyPosted = 0,
                Eligible = 1, Posted = 1, Skipped = 0, Failed = 0, Unknown = 0, Message = "رُحّل 1 من 1.",
            },
            Items = [new BulkPostingItemDto
            {
                DocumentType = BulkPostingDocumentType.SalesInvoice,
                DocumentId = DocumentId,
                DocumentNumber = "INV-1001",
                DocumentDateUtc = new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero),
                PartyName = "عميل الاختبار",
                Amount = "200.0000",
                CurrencyCode = "SYP",
                PreviousStatus = "Draft",
                NewStatus = "Posted",
                JournalEntryId = Guid.NewGuid(),
                PostingNumber = "1",
                Status = BulkPostingItemStatus.Posted,
                Classification = BulkPostingClassification.CanBeApprovedAutomatically,
                Reason = "تم اعتماد الفاتورة وترحيلها.",
                AttemptCount = 1,
            }],
        });
    }
}
