using PetraERP.ApiClient;
using PetraERP.Desktop.DocumentProcessing;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Tests;

/// <summary>
/// Behavior of the posting-management page: the server owns classification and posting, so these tests prove the page
/// forwards the operator's scope, never posts without the explicit confirmation, and reuses the same idempotency key
/// after an ambiguous failure instead of starting a second request.
/// </summary>
public sealed class PostingManagementTests
{
    private static readonly string[] Permissions = [PostingManagementViewModel.ManagePermission];
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static ShellContext Context() => new(
        new CompanyContextResponse { Id = Guid.NewGuid(), Code = "A", NameArabic = "شركة أ", BaseCurrencyCode = "JOD" },
        new BranchContextResponse { Id = Guid.NewGuid(), Code = "MAIN", NameArabic = "رئيسي" },
        Permissions.ToHashSet(StringComparer.Ordinal));

    private static (PostingManagementViewModel Model, FakePostingClient Client, ConfirmationStub Confirmation) Create()
    {
        var localization = new LocalizationService("en");
        var operation = new DesktopOperation(localization);
        var client = new FakePostingClient();
        var confirmation = new ConfirmationStub();
        return (new PostingManagementViewModel(client, confirmation, operation, localization), client, confirmation);
    }

    [Fact]
    public async Task NavigationScopeSelectsOneTypeAndForwardsDatesAndSearchToPreview()
    {
        var (model, client, _) = Create();
        await model.ActivateAsync(Context(), new BulkPostingNavigation(BulkPostingDocumentType.SalesInvoice,
            new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 31, 0, 0, 0, TimeSpan.Zero),
            "INV-10"), Cancellation).ConfigureAwait(true);

        Assert.True(model.IncludeSales);
        Assert.False(model.IncludePurchases || model.IncludeVouchers || model.IncludeJournals || model.IncludeInventory);
        var filter = Assert.Single(client.PreviewFilters);
        Assert.Equal(new[] { BulkPostingDocumentType.SalesInvoice }, filter.DocumentTypes);
        Assert.Equal("INV-10", filter.Search);
        Assert.Equal(new DateTime(2026, 10, 1), filter.FromDateUtc!.Value.UtcDateTime.Date);
        Assert.Equal(new DateTime(2026, 10, 31), filter.ToDateUtc!.Value.UtcDateTime.Date);
        Assert.True(model.HasPreview);
        Assert.Equal(2, model.PreviewItems.Count);
        Assert.Equal(0, model.ReadyToPost);
        Assert.Equal(1, model.NeedsApproval);
        Assert.Equal(1, model.Ineligible);
        Assert.True(model.PreviewItems[0].Postable);
        Assert.False(model.PreviewItems[1].Postable);
    }

    [Fact]
    public async Task PostAllStaysDisabledUntilConfirmedAndTheDialogIsRequired()
    {
        var (model, client, confirmation) = Create();
        await model.ActivateAsync(Context(), new BulkPostingNavigation(BulkPostingDocumentType.SalesInvoice), Cancellation).ConfigureAwait(true);

        Assert.False(model.PostAllCommand.CanExecute(null)); // Not confirmed yet.
        model.IsConfirmed = true;
        Assert.True(model.PostAllCommand.CanExecute(null));

        confirmation.Confirm = false;
        await model.PostAllCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal("D_ConfirmBulkPosting", confirmation.LastKey);
        Assert.Empty(client.Starts);
        Assert.False(model.HasBatch);
    }

    [Fact]
    public async Task AmbiguousFailureKeepsTheSameKeyAndASafeRetrySucceedsOnce()
    {
        var (model, client, confirmation) = Create();
        await model.ActivateAsync(Context(), new BulkPostingNavigation(BulkPostingDocumentType.SalesInvoice), Cancellation).ConfigureAwait(true);
        model.IsConfirmed = true;
        client.Failure = new PetraApiException("network", 503, null, new Dictionary<string, IEnumerable<string>>(), null);

        await model.PostAllCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.True(model.IsUncertain);
        Assert.Equal(1, client.Attempts);
        Assert.Equal("ServerUnavailable", model.Operation.ErrorKey);

        // The retry must report the same key: a new key would create a second batch with a second set of postings.
        client.Failure = null;
        model.IsConfirmed = true;
        await model.PostAllCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.False(model.IsUncertain);
        Assert.Equal(2, client.Attempts);
        Assert.Equal(client.Keys[0], client.Keys[1]);
        Assert.Equal(client.BatchId, model.LastBatchId);
        Assert.True(model.HasBatch);
        Assert.Equal(1, model.Posted);
        Assert.Equal(BulkPostingBatchStatus.Completed, client.LastBatch!.Status);
        Assert.True(confirmation.Confirm);
    }

    /// <summary>
    /// A bulk batch is durable and keyed per document, so a 30-second HTTP timeout means the server was still working — not that the
    /// posting failed. The page must keep going with the same key instead of leaving the operator with a silent, half-done batch.
    /// </summary>
    [Fact]
    public async Task ResponseTimeoutResumesTheSameBatchKeyInsteadOfFailingSilently()
    {
        var (model, client, confirmation) = Create();
        await model.ActivateAsync(Context(), new BulkPostingNavigation(BulkPostingDocumentType.SalesInvoice), Cancellation).ConfigureAwait(true);
        model.IsConfirmed = true;
        client.Failure = new TaskCanceledException("timeout");

        await model.PostAllCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Equal(2, client.Attempts);
        Assert.Equal(client.Keys[0], client.Keys[1]);
        Assert.False(model.IsUncertain);
        Assert.True(model.HasBatch);
        Assert.Equal(client.BatchId, model.LastBatchId);
        Assert.True(confirmation.Confirm);
    }

    [Fact]
    public async Task PostSelectedSendsOnlyCheckedPostableRows()
    {
        var (model, client, _) = Create();
        await model.ActivateAsync(Context(), new BulkPostingNavigation(BulkPostingDocumentType.SalesInvoice), Cancellation).ConfigureAwait(true);
        model.IsConfirmed = true;
        var postableId = model.PreviewItems[0].DocumentId;
        model.PreviewItems[0].IsSelected = true;
        model.PreviewItems[1].IsSelected = true; // Not postable: must be ignored even if a row is somehow checked.
        Assert.Equal(1, model.SelectedCount);

        await model.PostSelectedCommand.ExecuteAsync(null).ConfigureAwait(true);
        var request = Assert.Single(client.Starts);
        var document = Assert.Single(request.Documents!);
        Assert.Equal(postableId, document.DocumentId!.Value);
        Assert.Equal(BulkPostingDocumentType.SalesInvoice, document.DocumentType);
    }

    [Fact]
    public async Task WithoutTheManageGrantNothingLoadsAndNoCommandRuns()
    {
        var (model, client, _) = Create();
        var denied = new ShellContext(
            new CompanyContextResponse { Id = Guid.NewGuid(), Code = "A", NameArabic = "شركة أ", BaseCurrencyCode = "JOD" },
            new BranchContextResponse { Id = Guid.NewGuid(), Code = "MAIN", NameArabic = "رئيسي" },
            new HashSet<string>(StringComparer.Ordinal));

        await model.ActivateAsync(denied, new BulkPostingNavigation(BulkPostingDocumentType.SalesInvoice), Cancellation).ConfigureAwait(true);

        Assert.False(model.HasContext);
        Assert.Equal("PermissionDenied", model.Operation.ErrorKey);
        Assert.Empty(client.PreviewFilters);
        Assert.False(model.PreviewCommand.CanExecute(null));
        Assert.False(model.PostAllCommand.CanExecute(null));
    }

    private sealed class ConfirmationStub : IUserConfirmation
    {
        public bool Confirm { get; set; } = true;
        public string? LastKey { get; private set; }
        public Task<bool> ConfirmAsync(string key) { LastKey = key; return Task.FromResult(Confirm); }
    }

    private sealed class FakePostingClient : IDocumentPostingClient
    {
        public Guid BatchId { get; } = Guid.NewGuid();
        public Exception? Failure { get; set; }
        public int Attempts { get; private set; }
        public List<string> Keys { get; } = [];
        public List<BulkPostingFilterDto> PreviewFilters { get; } = [];
        public List<StartBulkPostingRequest> Starts { get; } = [];
        public BulkPostingBatchDto? LastBatch { get; private set; }

        private static BulkPostingPreviewItemDto Item(Guid id, string number, bool postable, BulkPostingClassification classification) => new()
        {
            DocumentType = BulkPostingDocumentType.SalesInvoice,
            DocumentId = id,
            DocumentNumber = number,
            DocumentDateUtc = new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero),
            PartyName = "عميل",
            Amount = "200.0000",
            CurrencyCode = "SYP",
            CurrentStatus = "Draft",
            Classification = classification,
            Postable = postable,
            Reason = postable ? "جاهز" : "صلاحية ناقصة",
        };

        public Task<BulkPostingPreviewResponse> PreviewAsync(Guid companyId, Guid branchId, BulkPostingFilterDto filter, CancellationToken cancellationToken)
        {
            PreviewFilters.Add(filter);
            var ready = Item(Guid.NewGuid(), "INV-1001", true, BulkPostingClassification.CanBeApprovedAutomatically);
            var blocked = Item(Guid.NewGuid(), "INV-1002", false, BulkPostingClassification.MissingPermission);
            return Task.FromResult(new BulkPostingPreviewResponse
            {
                TotalExamined = 2, ReadyToPost = 0, NeedsApproval = 1, Ineligible = 1, Items = [ready, blocked],
            });
        }

        public Task<BulkPostingBatchDto> StartAsync(Guid companyId, Guid branchId, StartBulkPostingRequest request, CancellationToken cancellationToken)
        {
            Attempts++;
            Keys.Add(request.IdempotencyKey);
            Starts.Add(request);
            if (Failure is { } failure) { Failure = null; return Task.FromException<BulkPostingBatchDto>(failure); }
            LastBatch = new BulkPostingBatchDto
            {
                BatchId = BatchId, Status = BulkPostingBatchStatus.Completed, TotalExamined = 1, AlreadyPosted = 0, Eligible = 1,
                Posted = 1, Skipped = 0, Failed = 0, Unknown = 0, Message = "رُحّل 1 من 1.",
            };
            return Task.FromResult(LastBatch);
        }

        public Task<BulkPostingBatchDto> GetAsync(Guid companyId, Guid branchId, Guid batchId, CancellationToken cancellationToken) =>
            Task.FromResult(LastBatch ?? throw new InvalidOperationException("No batch started."));

        public Task<BulkPostingBatchDto> ResumeAsync(Guid companyId, Guid branchId, Guid batchId, bool retryFailed, CancellationToken cancellationToken) =>
            Task.FromResult(LastBatch ?? throw new InvalidOperationException("No batch started."));

        public Task<BulkPostingResultsResponse> GetResultsAsync(Guid companyId, Guid branchId, Guid batchId,
            BulkPostingItemStatus? status, BulkPostingDocumentType? documentType, int page, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult(new BulkPostingResultsResponse
            {
                Page = page, PageSize = pageSize, TotalCount = 1, Batch = LastBatch,
                Items = [new BulkPostingItemDto
                {
                    DocumentType = BulkPostingDocumentType.SalesInvoice, DocumentNumber = "INV-1001",
                    DocumentDateUtc = new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero), PartyName = "عميل",
                    Amount = "200.0000", CurrencyCode = "SYP", PreviousStatus = "Draft", NewStatus = "Posted",
                    JournalEntryId = Guid.NewGuid(), PostingNumber = "1", Status = BulkPostingItemStatus.Posted,
                    Classification = BulkPostingClassification.CanBeApprovedAutomatically, Reason = "رُحّل", AttemptCount = 1,
                }],
            });
    }
}
