using PetraERP.ApiClient;
using PetraERP.Desktop.Accounting;
using PetraERP.Desktop.Administration;
using PetraERP.Desktop.Connection;
using PetraERP.Desktop.GeneralLedger;
using PetraERP.Desktop.Inventory;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Parties;
using PetraERP.Desktop.Patterns;
using PetraERP.Desktop.Sales;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Tests;

internal static class TestPages
{
    internal static ShellPages Create(AdministrationViewModel admin, GeneralLedgerViewModel journals, IGeneralLedgerClient ledger,
        IUserConfirmation confirmation, DesktopOperation operation, LocalizationService localization, ShellNavigator navigator,
        IChartOfAccountsClient? chart = null, IFiscalCalendarClient? calendar = null, ILedgerReportsClient? reports = null,
        IPatternsClient? patterns = null, IPartiesClient? parties = null, IInventoryClient? inventory = null, ISalesInvoiceClient? salesInvoices = null, ISalesDocumentLinksClient? salesLinks = null,
        PetraERP.Desktop.Treasury.ITreasuryVouchersClient? vouchers = null, PetraERP.Desktop.Purchases.IPurchaseInvoicesClient? purchases = null,
        PetraERP.Desktop.Relationships.IRelatedDocumentsClient? relatedDocuments = null, CurrencyFixture? currencies = null,
        PetraERP.Desktop.Currencies.IFxRevaluationClient? fxRevaluation = null, IInventoryDocumentsClient? inventoryDocuments = null)
    {
        var fixture = new AccountingFixture(); chart ??= fixture; calendar ??= fixture; reports ??= fixture; patterns ??= fixture; parties ??= fixture; inventory ??= fixture; salesInvoices ??= fixture;
        var currencyFixture = currencies ?? new CurrencyFixture();
        var notifications = new NotificationCenter(localization);
        var links = new SalesDocumentLinksFactory(salesLinks ?? fixture, navigator, operation, localization);
        var relations = new Relationships.RelatedDocumentsFactory(relatedDocuments ?? new RelatedDocumentsTests.RelatedFixture(), navigator, operation, localization);
        return new(admin, journals, new(chart, ledger, navigator, operation, localization, notifications, confirmation), new(chart, navigator, operation, localization, notifications),
            new(calendar, confirmation, navigator, operation, localization, notifications, TimeProvider.System), new(reports, ledger, navigator, operation, localization),
            new(reports, ledger, navigator, operation, localization), new(reports, calendar, navigator, operation, localization, TimeProvider.System), calendar,
            new AccountCardFactory(chart, reports, navigator, operation, localization, confirmation),
            new JournalDocumentFactory(ledger, confirmation, operation, localization, navigator, calendar, links, relations),
            new DocumentPatternsViewModel(patterns, navigator, operation, localization, confirmation),
            new PatternCardFactory(patterns, chart, navigator, operation, localization, confirmation),
            new PartiesViewModel(parties, navigator, operation, localization, confirmation),
            new PartyCardFactory(parties, chart, navigator, operation, localization, confirmation, links, relations),
            new ItemsViewModel(inventory, navigator, operation, localization, confirmation),
            new ItemCardFactory(inventory, navigator, operation, localization, confirmation, links, chart),
            new ItemGroupCardFactory(inventory, chart, navigator, operation, localization, confirmation),
            new SalesInvoicesViewModel(salesInvoices, parties, inventory, patterns, navigator, operation, localization, confirmation),
            new SalesInvoiceCardFactory(salesInvoices, parties, inventory, patterns, chart, navigator, operation, localization, confirmation, links, relations, currencyFixture),
            new PetraERP.Desktop.Treasury.VouchersViewModel(vouchers ?? new TreasuryFixture(), parties, patterns, chart, currencyFixture, navigator, operation, localization, confirmation),
            new PetraERP.Desktop.Purchases.PurchasesViewModel(purchases ?? new PurchaseFixture(), parties, patterns, inventory, chart, currencyFixture, navigator, operation, localization, confirmation),
            inventoryDocuments is null ? null : new InventoryOperationsViewModel(inventoryDocuments, inventory, chart, confirmation, navigator, operation, localization),
            null, new PetraERP.Desktop.Currencies.CurrencySettingsViewModel(currencyFixture, notifications, operation, localization),
            fxRevaluation: fxRevaluation is null ? null : new PetraERP.Desktop.Currencies.FxRevaluationViewModel(fxRevaluation, currencyFixture, chart, confirmation, navigator, operation, localization),
            postingManagement: new PetraERP.Desktop.DocumentProcessing.PostingManagementViewModel(new PostingFixture(), confirmation, operation, localization),
            accountDirectory: new PetraERP.Desktop.Shell.AccountDirectoryViewModel(chart, navigator, operation, localization),
            itemDirectory: new PetraERP.Desktop.Shell.ItemDirectoryViewModel(inventory, navigator, operation, localization),
            patternManager: new PetraERP.Desktop.Patterns.PatternManagerViewModel(navigator, operation, localization),
            itemLimits: new PetraERP.Desktop.Shell.ItemLimitsViewModel(inventory, navigator, operation, localization),
            itemPriceTool: new PetraERP.Desktop.Shell.ItemPriceToolViewModel(inventory, confirmation, operation, localization));
    }
}

/// <summary>Empty posting-batch port for route and permission tests; real behavior is proven against SQL.</summary>
internal sealed class PostingFixture : PetraERP.Desktop.DocumentProcessing.IDocumentPostingClient
{
    public Task<BulkPostingPreviewResponse> PreviewAsync(Guid companyId, Guid branchId, BulkPostingFilterDto filter, CancellationToken cancellationToken) =>
        Task.FromResult(new BulkPostingPreviewResponse { TotalExamined = 0, ReadyToPost = 0, NeedsApproval = 0, Ineligible = 0, Items = [] });

    public Task<BulkPostingBatchDto> StartAsync(Guid companyId, Guid branchId, StartBulkPostingRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Posting batches are exercised against real SQL.");

    public Task<BulkPostingBatchDto> GetAsync(Guid companyId, Guid branchId, Guid batchId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Posting batches are exercised against real SQL.");

    public Task<BulkPostingBatchDto> ResumeAsync(Guid companyId, Guid branchId, Guid batchId, bool retryFailed, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Posting batches are exercised against real SQL.");

    public Task<BulkPostingResultsResponse> GetResultsAsync(Guid companyId, Guid branchId, Guid batchId, BulkPostingItemStatus? status,
        BulkPostingDocumentType? documentType, int page, int pageSize, CancellationToken cancellationToken) =>
        Task.FromResult(new BulkPostingResultsResponse { Page = page, PageSize = pageSize, TotalCount = 0, Items = [] });
}

/// <summary>In-memory port fixture for page behavior; SQL acceptance uses real generated-client adapters.</summary>
internal sealed class AccountingFixture : IChartOfAccountsClient, IFiscalCalendarClient, ILedgerReportsClient, IPatternsClient, IPartiesClient, IInventoryClient, ISalesInvoiceClient, ISalesDocumentLinksClient
{
    public Task<SalesDocumentLinkPageResponse> ListAsync(Guid companyId, Guid branchId, SalesLinkSource source, Guid sourceId, int page, int pageSize, CancellationToken cancellationToken) => Task.FromResult(new SalesDocumentLinkPageResponse { Page = page, PageSize = pageSize, TotalCount = 0, Items = [] });
    internal List<CostCenterHistoryResponse> CenterHistory { get; } = [];
    internal List<(Guid Company, Guid Branch, Guid Center, int Page, int Size)> CenterHistoryQueries { get; } = [];
    internal TaskCompletionSource<CostCenterHistoryPageResponse>? PendingCenterHistory { get; set; }
    public Task<CostCenterHistoryPageResponse> GetCostCenterHistoryAsync(Guid companyId, Guid branchId, Guid costCenterId, int page, int pageSize, CancellationToken cancellationToken)
    {
        CenterHistoryQueries.Add((companyId, branchId, costCenterId, page, pageSize));
        var rows = CenterHistory.Where(item => item.CostCenterId == costCenterId).ToArray();
        return PendingCenterHistory?.Task ?? Task.FromResult(new CostCenterHistoryPageResponse { Page = page, PageSize = pageSize, TotalCount = rows.Length,
            Items = rows.Skip((page - 1) * pageSize).Take(pageSize).ToArray() });
    }
    internal List<(Guid Company, Guid Branch, Guid Account, int Page, int Size)> ContextQueries { get; } = [];
    internal TaskCompletionSource<AccountCardContextResponse>? PendingContext { get; set; }
    internal int TreeCalls { get; private set; }
    public Task<AccountCardContextResponse> GetAccountCardContextAsync(Guid companyId, Guid branchId, Guid accountId, int page, int pageSize, CancellationToken cancellationToken)
    {
        ContextQueries.Add((companyId, branchId, accountId, page, pageSize));
        var path = new List<AccountResponse>();
        for (var node = Accounts.Single(item => item.Id == accountId); node is not null;
             node = node.ParentId is { } parent ? Accounts.Single(item => item.Id == parent) : null) path.Insert(0, node);
        var children = Accounts.Where(item => item.ParentId == accountId).OrderBy(item => item.Code, StringComparer.Ordinal).ToArray();
        return PendingContext?.Task ?? Task.FromResult(new AccountCardContextResponse { Path = path,
            Children = new() { Items = children.Skip((page - 1) * pageSize).Take(pageSize).ToArray(), Page = page, PageSize = pageSize, TotalCount = children.Length } });
    }
    internal List<AccountHistoryResponse> History { get; } = [];
    internal List<(Guid Company, Guid Branch, Guid Account, int Page, int Size)> HistoryQueries { get; } = [];
    internal TaskCompletionSource<AccountHistoryPageResponse>? PendingHistory { get; set; }
    public Task<AccountHistoryPageResponse> GetAccountHistoryAsync(Guid companyId, Guid branchId, Guid accountId, int page, int pageSize, CancellationToken cancellationToken)
    {
        HistoryQueries.Add((companyId, branchId, accountId, page, pageSize));
        var rows = History.Where(item => item.AccountId == accountId).ToArray();
        return PendingHistory?.Task ?? Task.FromResult(new AccountHistoryPageResponse { Page = page, PageSize = pageSize, TotalCount = rows.Length,
            Items = rows.Skip((page - 1) * pageSize).Take(pageSize).ToArray() });
    }
    internal List<AccountResponse> Accounts { get; } = [];
    internal List<CostCenterResponse> CostCenters { get; } = [];
    internal List<FiscalYearResponse> Years { get; } = [];
    internal GeneralJournalResponse GeneralJournal { get; set; } = new() { Lines = [], Page = 1, PageSize = 100, TotalBaseDebit = "0", TotalBaseCredit = "0" };
    internal AccountLedgerResponse Ledger { get; set; } = new() { Account = new(), Lines = [], Page = 1, PageSize = 100 };
    internal TrialBalanceResponse Balance { get; set; } = new() { Rows = [], IsBalanced = true };
    internal GeneralJournalFilter? LastFilter { get; private set; }
    internal PeriodTransitionRequest? LastTransition { get; private set; }
    internal TaskCompletionSource<GeneralJournalResponse>? PendingReport { get; set; }
    public Task<IReadOnlyList<AccountResponse>> GetAccountTreeAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken)
    {
        TreeCalls++;
        return Task.FromResult<IReadOnlyList<AccountResponse>>(Accounts.ToArray());
    }
    public Task<AccountResponse> GetAccountAsync(Guid companyId, Guid branchId, Guid accountId, CancellationToken cancellationToken) => Task.FromResult(Accounts.Single(a => a.Id == accountId));
    public Task<AccountResponse> CreateAccountAsync(Guid companyId, Guid branchId, AccountRequest request, CancellationToken cancellationToken)
    {
        var parent = request.ParentId is { } id ? Accounts.Single(a => a.Id == id) : null;
        var created = new AccountResponse { Id = Guid.NewGuid(), CompanyId = companyId, Code = request.Code, NameArabic = request.NameArabic, NameEnglish = request.NameEnglish,
            AccountType = request.AccountType, PostingAllowed = request.PostingAllowed, CostCenterPolicy = request.CostCenterPolicy, CurrencyMode = request.CurrencyMode,
            ParentId = request.ParentId, Level = parent is null ? 0 : parent.Level + 1, IsActive = true, Version = "AAAAAAAAB9E=" };
        if (parent is not null) parent.HasChildren = true;
        Accounts.Add(created);
        return Task.FromResult(created);
    }
    internal List<AccountRequest> Updates { get; } = [];
    public Task<AccountResponse> UpdateAccountAsync(Guid companyId, Guid branchId, Guid accountId, string expectedVersion, AccountRequest request, CancellationToken cancellationToken)
    {
        var account = Accounts.Single(a => a.Id == accountId);
        if (account.Version != expectedVersion) throw new PetraApiException("conflict", 409, "{\"code\":\"concurrency.conflict\"}", new Dictionary<string, IEnumerable<string>>(), null);
        Updates.Add(request);
        account.Code = request.Code; account.NameArabic = request.NameArabic; account.NameEnglish = request.NameEnglish; account.CostCenterPolicy = request.CostCenterPolicy;
        account.Version = Convert.ToBase64String(BitConverter.GetBytes(Updates.Count + 100L));
        return Task.FromResult(account);
    }
    public Task<AccountResponse> SetAccountActiveAsync(Guid companyId, Guid branchId, Guid accountId, SetActiveRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    internal List<(Guid AccountId, DeleteAccountRequest Request)> Deletions { get; } = [];
    internal int NavigationCalls { get; private set; }
    /// <summary>Keyset navigation in ordinal code order, like the server.</summary>
    public Task<AccountNavigationResponse> NavigateAccountAsync(Guid companyId, Guid branchId, AccountNavigationDirection direction, Guid? fromAccountId, CancellationToken cancellationToken)
    {
        NavigationCalls++;
        var ordered = Accounts.OrderBy(a => a.Code, StringComparer.Ordinal).ToList();
        var from = fromAccountId is { } id ? Accounts.Single(a => a.Id == id).Code : null;
        var account = direction switch
        {
            AccountNavigationDirection.First => ordered.FirstOrDefault(),
            AccountNavigationDirection.Last => ordered.LastOrDefault(),
            AccountNavigationDirection.Next => ordered.FirstOrDefault(a => string.CompareOrdinal(a.Code, from) > 0),
            _ => ordered.LastOrDefault(a => string.CompareOrdinal(a.Code, from) < 0),
        };
        return Task.FromResult(new AccountNavigationResponse { Account = account });
    }
    /// <summary>Mirrors the server rule: only an unused leaf is deleted; the parent's child flag is recomputed.</summary>
    public Task DeleteAccountAsync(Guid companyId, Guid branchId, Guid accountId, DeleteAccountRequest request, CancellationToken cancellationToken)
    {
        var account = Accounts.Single(a => a.Id == accountId);
        if (account.HasChildren || account.HasJournalLines) throw new PetraApiException("in use", 422, "{\"code\":\"ledger.account_in_use\"}", new Dictionary<string, IEnumerable<string>>(), null);
        if (account.Version != request.ExpectedVersion) throw new PetraApiException("conflict", 409, "{\"code\":\"concurrency.conflict\"}", new Dictionary<string, IEnumerable<string>>(), null);
        Accounts.Remove(account); Deletions.Add((accountId, request));
        if (account.ParentId is { } parent && Accounts.SingleOrDefault(a => a.Id == parent) is { } owner) owner.HasChildren = Accounts.Any(a => a.ParentId == parent);
        return Task.CompletedTask;
    }
    public Task<CostCenterPageResponse> SearchCostCentersAsync(Guid companyId, Guid branchId, string? search, bool? isActive, int page, int pageSize, CancellationToken cancellationToken) => Task.FromResult(new CostCenterPageResponse { Items = CostCenters.ToArray(), Page = page, PageSize = pageSize, TotalCount = CostCenters.Count });
    public Task<CostCenterResponse> CreateCostCenterAsync(Guid companyId, Guid branchId, CostCenterRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<CostCenterResponse> UpdateCostCenterAsync(Guid companyId, Guid branchId, Guid costCenterId, string expectedVersion, CostCenterRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<CostCenterResponse> SetCostCenterActiveAsync(Guid companyId, Guid branchId, Guid costCenterId, SetActiveRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<CostCenterResponse> GetCostCenterAsync(Guid companyId, Guid branchId, Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(CostCenters.FirstOrDefault(c => c.Id == id) ?? new CostCenterResponse { Id = id, CompanyId = companyId, Code = "CC", NameArabic = "مركز" });
    public Task<IReadOnlyList<CostCenterResponse>> GetCostCenterTreeAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CostCenterResponse>>(CostCenters.ToArray());
    public Task<IReadOnlyList<CostCenterAllocationResponse>> GetCostCenterAllocationsAsync(Guid companyId, Guid branchId, Guid id, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CostCenterAllocationResponse>>([]);
    public Task<IReadOnlyList<CostCenterAllocationResponse>> SetCostCenterAllocationsAsync(Guid companyId, Guid branchId, Guid id, SetCostCenterAllocationsRequest request, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CostCenterAllocationResponse>>([]);
    public Task<IReadOnlyList<FiscalYearResponse>> ListFiscalYearsAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<FiscalYearResponse>>(Years.ToArray());
    public Task<FiscalYearResponse> CreateFiscalYearAsync(Guid companyId, Guid branchId, CreateFiscalYearRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<AccountingPeriodResponse> TransitionAsync(Guid companyId, Guid branchId, Guid periodId, PeriodAction action, PeriodTransitionRequest request, CancellationToken cancellationToken)
    {
        LastTransition = request;
        var period = Years.SelectMany(y => y.Periods).Single(p => p.Id == periodId);
        period.State = action switch { PeriodAction.SoftClose => AccountingPeriodState.SoftClosed, PeriodAction.Close => AccountingPeriodState.Closed, _ => AccountingPeriodState.Open };
        return Task.FromResult(period);
    }
    public Task<IReadOnlyList<PeriodAuditResponse>> GetPeriodAuditAsync(Guid companyId, Guid branchId, Guid periodId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PeriodAuditResponse>>([]);
    public Task<GeneralJournalResponse> GetGeneralJournalAsync(Guid companyId, Guid branchId, GeneralJournalFilter filter, int page, int pageSize, CancellationToken cancellationToken)
    { LastFilter = filter; if (PendingReport is { } pending) { PendingReport = null; return pending.Task; } return Task.FromResult(GeneralJournal); }
    public Task<AccountLedgerResponse> GetAccountLedgerAsync(Guid companyId, Guid branchId, Guid accountId, DateOnly? from, DateOnly? until, bool consolidated, int page, int pageSize, string? cursor, CancellationToken cancellationToken) { LedgerCursors.Add((page, cursor)); return Task.FromResult(Ledger); }
    internal List<(int Page, string? Cursor)> LedgerCursors { get; } = [];
    internal TrialBalanceFilter? LastBalanceFilter { get; private set; }
    public Task<TrialBalanceResponse> GetTrialBalanceAsync(Guid companyId, Guid branchId, TrialBalanceFilter filter, CancellationToken cancellationToken) { LastBalanceFilter = filter; return Task.FromResult(Balance); }

    internal List<DocumentPatternResponse> Patterns { get; } = [];
    internal List<PatternRevisionResponse> Revisions { get; } = [];

    public Task<DocumentPatternPageResponse> ListPatternsAsync(Guid companyId, Guid branchId, int page, int pageSize, BusinessOperationKind? kind, bool? activeOnly, CancellationToken cancellationToken) =>
        Task.FromResult(new DocumentPatternPageResponse
        {
            Items = Patterns.Select(p => new DocumentPatternSummaryResponse
            {
                Id = p.Id,
                Code = p.Code,
                NameArabic = p.NameArabic,
                NameEnglish = p.NameEnglish,
                ShortCode = p.ShortCode,
                Icon = p.Icon,
                Group = p.Group,
                OperationKind = p.OperationKind,
                IsActive = p.IsActive,
                IsDefault = p.IsDefault,
                IsRunnable = p.IsRunnable,
                ActiveRevisionNumber = p.ActiveRevision?.RevisionNumber,
                Version = p.Version,
            }).ToArray(),
            Page = page,
            PageSize = pageSize,
            TotalCount = Patterns.Count
        });

    public Task<DocumentPatternResponse> GetPatternAsync(Guid companyId, Guid branchId, Guid patternId, CancellationToken cancellationToken) =>
        Task.FromResult(Patterns.Single(p => p.Id == patternId));

    public Task<DocumentPatternResponse> CreatePatternAsync(Guid companyId, Guid branchId, CreateDocumentPatternRequest request, CancellationToken cancellationToken)
    {
        var pattern = new DocumentPatternResponse
        {
            Id = Guid.NewGuid(),
            OperationKind = request.OperationKind,
            Code = request.Code,
            NameArabic = request.NameArabic,
            NameEnglish = request.NameEnglish,
            ShortCode = request.ShortCode,
            Icon = request.Icon,
            Group = request.Group,
            IsActive = true,
            IsDefault = request.IsDefault,
            IsRunnable = request.OperationKind == BusinessOperationKind.ManualJournal,
            BranchIds = request.BranchIds?.ToList() ?? [],
            Version = "AAAAAAAAB9E=",
        };
        Patterns.Add(pattern);
        return Task.FromResult(pattern);
    }

    public Task<DocumentPatternResponse> UpdatePatternAsync(Guid companyId, Guid branchId, Guid patternId, string expectedVersion, UpdateDocumentPatternRequest request, CancellationToken cancellationToken)
    {
        var pattern = Patterns.Single(p => p.Id == patternId);
        pattern.NameArabic = request.NameArabic;
        pattern.NameEnglish = request.NameEnglish;
        pattern.ShortCode = request.ShortCode;
        pattern.Icon = request.Icon;
        pattern.Group = request.Group;
        pattern.IsActive = request.IsActive;
        pattern.IsDefault = request.IsDefault;
        pattern.BranchIds = request.BranchIds?.ToList() ?? [];
        pattern.Version = "AAAAAAAAB9F=";
        return Task.FromResult(pattern);
    }

    public Task<PatternRevisionResponse> CreateRevisionAsync(Guid companyId, Guid branchId, Guid patternId, CreatePatternRevisionRequest request, CancellationToken cancellationToken)
    {
        var pattern = Patterns.Single(p => p.Id == patternId);
        var rev = new PatternRevisionResponse
        {
            Id = Guid.NewGuid(),
            PatternId = patternId,
            RevisionNumber = (pattern.ActiveRevision?.RevisionNumber ?? 0) + 1,
            State = PatternRevisionState.Draft,
            Configuration = request.Configuration,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        Revisions.Add(rev);
        return Task.FromResult(rev);
    }

    public Task<DocumentPatternResponse> PublishRevisionAsync(Guid companyId, Guid branchId, Guid patternId, PublishPatternRevisionRequest request, CancellationToken cancellationToken)
    {
        var pattern = Patterns.Single(p => p.Id == patternId);
        var rev = Revisions.Single(r => r.Id == request.RevisionId);
        rev.State = PatternRevisionState.Published;
        rev.EffectiveFromUtc = DateTimeOffset.UtcNow;
        pattern.ActiveRevision = rev;
        pattern.Version = "AAAAAAAAB9G=";
        return Task.FromResult(pattern);
    }

    public Task<DocumentPatternResponse> RetirePatternAsync(Guid companyId, Guid branchId, Guid patternId, RetirePatternRequest request, CancellationToken cancellationToken)
    {
        var pattern = Patterns.Single(p => p.Id == patternId);
        pattern.IsActive = false;
        pattern.Version = "AAAAAAAAB9H=";
        return Task.FromResult(pattern);
    }

    public Task<IReadOnlyList<DocumentPatternSummaryResponse>> GetRunnablePatternsAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DocumentPatternSummaryResponse>>(
            Patterns.Where(p => p.IsActive == true && p.IsRunnable == true)
                .Select(p => new DocumentPatternSummaryResponse
                {
                    Id = p.Id,
                    Code = p.Code,
                    NameArabic = p.NameArabic,
                    NameEnglish = p.NameEnglish,
                    ShortCode = p.ShortCode,
                    Icon = p.Icon,
                    Group = p.Group,
                    OperationKind = p.OperationKind,
                    ActiveRevisionNumber = p.ActiveRevision?.RevisionNumber,
                    IsActive = p.IsActive,
                    IsDefault = p.IsDefault,
                    IsRunnable = p.IsRunnable,
                    Version = p.Version,
                }).ToArray());

    /// <summary>Counts preview reads so a test can prove the estimate never reaches the reserving endpoint.</summary>
    internal int NumberPreviewCalls { get; private set; }

    internal int NumberGenerationCalls { get; private set; }

    public Task<NextNumberResponse> GenerateNextNumberAsync(Guid companyId, Guid branchId, Guid patternId, Guid? fiscalYearId, CancellationToken cancellationToken)
    {
        NumberGenerationCalls++;
        return Task.FromResult(new NextNumberResponse { FormattedNumber = "SEQ-00001", SequenceNumber = 1 });
    }

    public Task<NextNumberPreviewResponse> PreviewNextNumberAsync(Guid companyId, Guid branchId, Guid patternId, Guid? fiscalYearId,
        DateOnly? accountingDate, Guid? revisionId, CancellationToken cancellationToken)
    {
        NumberPreviewCalls++;
        return Task.FromResult(new NextNumberPreviewResponse { IsEstimate = true, FormattedNumber = "SEQ-00001", SequenceNumber = 1 });
    }

    internal List<PartyDetailDto> Parties { get; } = [];

    public Task<PartyListResponse> ListPartiesAsync(Guid companyId, Guid branchId, int page, int pageSize, string? search, PartyRole? role, bool? isActive, CancellationToken cancellationToken)
    {
        var filtered = Parties
            .Where(p => role is null || p.Role == role)
            .Where(p => isActive is null || p.IsActive == isActive)
            .Where(p => string.IsNullOrWhiteSpace(search) || (p.Code?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) || (p.NameArabic?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();

        var pageItems = filtered.Skip((page - 1) * pageSize).Take(pageSize).Select(p => new PartySummaryDto
        {
            Id = p.Id,
            Code = p.Code,
            NameArabic = p.NameArabic,
            NameEnglish = p.NameEnglish,
            Role = p.Role,
            IsActive = p.IsActive,
            TaxNumber = p.TaxNumber,
            DefaultCurrencyCode = p.DefaultCurrencyCode,
            ReceivableAccountId = p.ReceivableAccountId,
            PayableAccountId = p.PayableAccountId,
            CreditLimit = p.CreditLimit,
            PaymentTermDays = p.PaymentTermDays,
            PolicyKind = p.PolicyKind,
            CurrentBalance = p.CurrentBalance,
            AvailableCredit = p.AvailableCredit,
        }).ToList();

        return Task.FromResult(new PartyListResponse
        {
            PageNumber = page,
            PageSize = pageSize,
            TotalCount = filtered.Count,
            Items = pageItems,
        });
    }

    public Task<PartyDetailDto> GetPartyAsync(Guid companyId, Guid branchId, Guid partyId, CancellationToken cancellationToken) =>
        Task.FromResult(Parties.First(p => p.Id == partyId));

    public Task<IReadOnlyList<PartyLookupDto>> LookupPartiesAsync(Guid companyId, Guid branchId, int maxResults, string? search, PartyRole? role, CancellationToken cancellationToken)
    {
        var items = Parties
            .Where(p => role is null || p.Role == role)
            .Take(maxResults)
            .Select(p => new PartyLookupDto { Id = p.Id, Code = p.Code, NameArabic = p.NameArabic, Role = p.Role })
            .ToList();
        return Task.FromResult<IReadOnlyList<PartyLookupDto>>(items);
    }

    public Task<PartyDetailDto> CreatePartyAsync(Guid companyId, Guid branchId, CreatePartyRequest request, CancellationToken cancellationToken)
    {
        var dto = new PartyDetailDto
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            Code = request.Code,
            NameArabic = request.NameArabic,
            NameEnglish = request.NameEnglish,
            Role = request.Role,
            IsActive = true,
            TaxNumber = request.TaxNumber,
            CommercialRegistration = request.CommercialRegistration,
            DefaultCurrencyCode = request.DefaultCurrencyCode,
            // Like the server: a role without a chosen account gets the party's own new account under the planned heading.
            ReceivableAccountId = request.ReceivableAccountId ?? (request.Role is PartyRole.Customer or PartyRole.Both ? CreatePartyAccount(AccountPlan.Receivable, request.NameArabic) : null),
            PayableAccountId = request.PayableAccountId ?? (request.Role is PartyRole.Supplier or PartyRole.Both ? CreatePartyAccount(AccountPlan.Payable, request.NameArabic) : null),
            CreditLimit = request.CreditLimit,
            PaymentTermDays = request.PaymentTermDays,
            PolicyKind = request.PolicyKind,
            CurrentBalance = "0.0000",
            AvailableCredit = request.CreditLimit,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            VersionToken = "v1",
        };
        Parties.Add(dto);
        return Task.FromResult(dto);
    }

    public Task<PartyDetailDto> UpdatePartyAsync(Guid companyId, Guid branchId, Guid partyId, UpdatePartyRequest request, CancellationToken cancellationToken)
    {
        var existing = Parties.First(p => p.Id == partyId);
        existing.NameArabic = request.NameArabic;
        existing.NameEnglish = request.NameEnglish;
        existing.Role = request.Role;
        existing.TaxNumber = request.TaxNumber;
        existing.CommercialRegistration = request.CommercialRegistration;
        existing.DefaultCurrencyCode = request.DefaultCurrencyCode;
        existing.ReceivableAccountId = request.ReceivableAccountId;
        existing.PayableAccountId = request.PayableAccountId;
        existing.PolicyKind = request.PolicyKind;
        existing.CreditLimit = request.CreditLimit;
        existing.PaymentTermDays = request.PaymentTermDays;
        existing.UpdatedAtUtc = DateTimeOffset.UtcNow;
        return Task.FromResult(existing);
    }

    public Task DeactivatePartyAsync(Guid companyId, Guid branchId, Guid partyId, CancellationToken cancellationToken)
    {
        var existing = Parties.First(p => p.Id == partyId);
        existing.IsActive = false;
        return Task.CompletedTask;
    }

    public Task ReactivatePartyAsync(Guid companyId, Guid branchId, Guid partyId, CancellationToken cancellationToken)
    {
        var existing = Parties.First(p => p.Id == partyId);
        existing.IsActive = true;
        return Task.CompletedTask;
    }

    /// <summary>The plan the party card previews; a test replaces it to prove the card shows what the server states.</summary>
    internal PartyAccountPlanDto AccountPlan { get; set; } = new()
    {
        Receivable = new() { ParentAccountId = Guid.NewGuid(), ParentCode = "121", ParentNameArabic = "الزبائن", NextAccountCode = "121001" },
        Payable = new() { ParentAccountId = Guid.NewGuid(), ParentCode = "221", ParentNameArabic = "الموردون", NextAccountCode = "221001" },
    };

    private Guid CreatePartyAccount(PartyAccountPlanSideDto? side, string? name)
    {
        var id = Guid.NewGuid();
        Accounts.Add(new AccountResponse { Id = id, Code = side?.NextAccountCode ?? string.Empty, NameArabic = name ?? string.Empty, ParentId = side?.ParentAccountId,
            PostingAllowed = true, IsActive = true, Version = "AAAAAAAAB9E=" });
        return id;
    }

    public Task<PartyAccountPlanDto> GetAccountPlanAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken) => Task.FromResult(AccountPlan);

    public Task<PartyCreditExposureDto> GetPartyCreditExposureAsync(Guid companyId, Guid branchId, Guid partyId, CancellationToken cancellationToken)
    {
        var party = Parties.FirstOrDefault(p => p.Id == partyId);
        return Task.FromResult(new PartyCreditExposureDto
        {
            PartyId = partyId,
            CreditLimit = party?.CreditLimit ?? "0.0000",
            TotalDebit = "0.0000",
            TotalCredit = "0.0000",
            CurrentBalance = party?.CurrentBalance ?? "0.0000",
            AvailableCredit = party?.AvailableCredit ?? "0.0000",
            PolicyKind = party?.PolicyKind ?? CreditPolicyKind.AllowWithWarning,
            IsExceeded = false,
        });
    }

    // --- IInventoryClient ---
    internal List<ItemDetailDto> Items { get; } = [];
    internal List<ItemGroupDto> ItemGroups { get; } = [];
    internal List<UnitOfMeasureDto> Units { get; } = [];
    internal List<UnitConversionDto> UnitConversions { get; } = [];
    internal List<WarehouseDto> Warehouses { get; } = [];
    internal List<StockMovementDto> StockMovements { get; } = [];

    public Task<ItemPageResponse> ListItemsAsync(Guid companyId, Guid branchId, int page, int pageSize, string? search, Guid? groupId, Guid? warehouseId, bool? isActive, CancellationToken cancellationToken)
    {
        var filtered = Items.AsEnumerable();
        if (groupId.HasValue) filtered = filtered.Where(i => i.GroupId == groupId.Value);
        if (isActive.HasValue) filtered = filtered.Where(i => i.IsActive == isActive.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            filtered = filtered.Where(i =>
                (i.Code != null && i.Code.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (i.NameArabic != null && i.NameArabic.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (i.NameEnglish != null && i.NameEnglish.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }

        var list = filtered.ToList();
        var pageItems = list.Skip((page - 1) * pageSize).Take(pageSize).Select(i => new ItemSummaryDto
        {
            Id = i.Id,
            CompanyId = i.CompanyId,
            GroupId = i.GroupId,
            GroupName = ItemGroups.FirstOrDefault(g => g.Id == i.GroupId)?.NameArabic,
            Code = i.Code,
            NameArabic = i.NameArabic,
            NameEnglish = i.NameEnglish,
            BaseUnitId = i.BaseUnitId,
            BaseUnitCode = Units.FirstOrDefault(u => u.Id == i.BaseUnitId)?.Code,
            BaseUnitSymbol = Units.FirstOrDefault(u => u.Id == i.BaseUnitId)?.Symbol,
            CostMethod = i.CostMethod,
            MinStock = i.MinStock,
            MaxStock = i.MaxStock,
            ReorderPoint = i.ReorderPoint,
            CurrentStock = i.CurrentStock,
            IsActive = i.IsActive,
        }).ToList();

        return Task.FromResult(new ItemPageResponse
        {
            Page = page,
            PageSize = pageSize,
            TotalCount = list.Count,
            Items = pageItems,
        });
    }

    public Task DeactivateItemAsync(Guid companyId, Guid branchId, Guid itemId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Items.Single(i => i.Id == itemId && i.CompanyId == companyId).IsActive = false;
        return Task.CompletedTask;
    }

    public Task ReactivateItemAsync(Guid companyId, Guid branchId, Guid itemId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Items.Single(i => i.Id == itemId && i.CompanyId == companyId).IsActive = true;
        return Task.CompletedTask;
    }

    public Task<ItemDetailDto> GetItemAsync(Guid companyId, Guid branchId, Guid itemId, CancellationToken cancellationToken)
    {
        var item = Items.First(i => i.Id == itemId);
        return Task.FromResult(item);
    }

    public Task<IReadOnlyList<ItemLookupDto>> LookupItemsAsync(Guid companyId, Guid branchId, int maxResults, string? search, CancellationToken cancellationToken)
    {
        var filtered = Items.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            filtered = filtered.Where(i =>
                (i.Code != null && i.Code.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (i.NameArabic != null && i.NameArabic.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }

        var results = filtered.Take(maxResults).Select(i => new ItemLookupDto
        {
            Id = i.Id,
            Code = i.Code,
            NameArabic = i.NameArabic,
            NameEnglish = i.NameEnglish,
            BaseUnitId = i.BaseUnitId,
            BaseUnitCode = Units.FirstOrDefault(u => u.Id == i.BaseUnitId)?.Code,
            BaseUnitSymbol = Units.FirstOrDefault(u => u.Id == i.BaseUnitId)?.Symbol,
            CurrentStock = i.CurrentStock,
        }).ToList();

        return Task.FromResult<IReadOnlyList<ItemLookupDto>>(results);
    }

    internal CreateItemRequest? LastCreateItem { get; private set; }
    internal UpdateItemRequest? LastUpdateItem { get; private set; }
    internal List<Guid> DeletedItems { get; } = [];

    internal List<AdjustItemPricesRequest> PriceAdjustments { get; } = [];
    public Task<AdjustItemPricesResponse> AdjustItemPricesAsync(Guid companyId, Guid branchId, AdjustItemPricesRequest request, CancellationToken cancellationToken)
    {
        PriceAdjustments.Add(new AdjustItemPricesRequest { GroupId = request.GroupId, PriceListCode = request.PriceListCode, Percent = request.Percent, RoundingScale = request.RoundingScale, PreviewOnly = request.PreviewOnly });
        return Task.FromResult(new AdjustItemPricesResponse { ItemCount = 3, RowCount = 5, Applied = request.PreviewOnly == false });
    }

    public Task DeleteItemAsync(Guid companyId, Guid branchId, Guid itemId, CancellationToken cancellationToken)
    {
        DeletedItems.Add(itemId);
        Items.RemoveAll(i => i.Id == itemId);
        return Task.CompletedTask;
    }

    public Task<ItemDetailDto> CreateItemAsync(Guid companyId, Guid branchId, CreateItemRequest request, CancellationToken cancellationToken)
    {
        LastCreateItem = request;
        var id = Guid.NewGuid();
        var barcodes = request.Barcodes?.Select(b => new ItemBarcodeDto
        {
            Id = Guid.NewGuid(),
            ItemId = id,
            Barcode = b.Barcode,
            UnitId = b.UnitId,
            UnitCode = Units.FirstOrDefault(u => u.Id == b.UnitId)?.Code,
            IsDefault = b.IsDefault,
        }).ToList() ?? [];

        var prices = request.Prices?.Select(p => new ItemPriceDto
        {
            Id = Guid.NewGuid(),
            ItemId = id,
            PriceListCode = p.PriceListCode,
            Price = p.Price,
            CurrencyCode = p.CurrencyCode,
            UnitId = p.UnitId,
            UnitCode = Units.FirstOrDefault(u => u.Id == p.UnitId)?.Code,
            MinQuantity = p.MinQuantity,
        }).ToList() ?? [];

        var dto = new ItemDetailDto
        {
            Id = id,
            CompanyId = companyId,
            GroupId = request.GroupId,
            Code = request.Code,
            NameArabic = request.NameArabic,
            NameEnglish = request.NameEnglish,
            BaseUnitId = request.BaseUnitId,
            DefaultWarehouseId = request.DefaultWarehouseId,
            CostMethod = request.CostMethod,
            MinStock = request.MinStock,
            MaxStock = request.MaxStock,
            ReorderPoint = request.ReorderPoint,
            IsActive = true,
            CurrentStock = "0.0000",
            Barcodes = barcodes,
            Prices = prices,
            Profile = request.Profile,
            Conversions = UnitConversions.Where(c => c.ItemId == id || c.ItemId == null).ToList(),
        };

        Items.Add(dto);
        return Task.FromResult(dto);
    }

    public Task<ItemDetailDto> UpdateItemAsync(Guid companyId, Guid branchId, Guid itemId, UpdateItemRequest request, CancellationToken cancellationToken)
    {
        var existing = Items.First(i => i.Id == itemId);
        LastUpdateItem = request;
        if (request.Profile is not null) existing.Profile = request.Profile;
        existing.NameArabic = request.NameArabic;
        existing.NameEnglish = request.NameEnglish;
        existing.GroupId = request.GroupId;
        existing.BaseUnitId = request.BaseUnitId;
        existing.DefaultWarehouseId = request.DefaultWarehouseId;
        existing.CostMethod = request.CostMethod;
        existing.MinStock = request.MinStock;
        existing.MaxStock = request.MaxStock;
        existing.ReorderPoint = request.ReorderPoint;
        return Task.FromResult(existing);
    }

    public Task<ItemStockSummaryDto> GetItemStockAsync(Guid companyId, Guid branchId, Guid itemId, Guid? warehouseId, CancellationToken cancellationToken)
    {
        var item = Items.FirstOrDefault(i => i.Id == itemId);
        var unit = Units.FirstOrDefault(u => u.Id == item?.BaseUnitId);
        var whList = Warehouses.Select(w => new WarehouseStockDto
        {
            WarehouseId = w.Id,
            WarehouseCode = w.Code,
            WarehouseName = w.NameArabic,
            Quantity = "0.0000",
            UnitCost = "0.0000",
            TotalCost = "0.0000",
        }).ToList();

        return Task.FromResult(new ItemStockSummaryDto
        {
            ItemId = itemId,
            BaseUnitSymbol = unit?.Symbol ?? "PCS",
            TotalQuantity = item?.CurrentStock ?? "0.0000",
            Warehouses = whList,
        });
    }

    public Task<IReadOnlyList<ItemGroupDto>> ListItemGroupsAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ItemGroupDto>>(ItemGroups);

    public Task<ItemGroupDetailDto> GetItemGroupAsync(Guid companyId, Guid branchId, Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(new ItemGroupDetailDto { Id = id, CompanyId = companyId, Code = "GRP", NameArabic = "مجموعة" });
    public Task<ItemGroupDto> UpdateItemGroupAsync(Guid companyId, Guid branchId, Guid id, UpdateItemGroupRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new ItemGroupDto { Id = id, Code = "GRP", NameArabic = request.NameArabic });
    public Task<bool> DeactivateItemGroupAsync(Guid companyId, Guid branchId, Guid id, CancellationToken cancellationToken) => Task.FromResult(true);
    public Task<bool> ReactivateItemGroupAsync(Guid companyId, Guid branchId, Guid id, CancellationToken cancellationToken) => Task.FromResult(true);
    public Task<bool> DeleteItemGroupAsync(Guid companyId, Guid branchId, Guid id, CancellationToken cancellationToken) => Task.FromResult(true);
    public Task<IReadOnlyList<ItemAccountMappingDto>> GetAccountMappingsAsync(Guid companyId, Guid branchId, int ownerType, Guid ownerId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ItemAccountMappingDto>>([]);
    public Task<bool> SetAccountMappingsAsync(Guid companyId, Guid branchId, SetAccountMappingsRequest request, CancellationToken cancellationToken) => Task.FromResult(true);
    public Task<EffectiveItemAccountMappingDto> GetEffectiveAccountMappingAsync(Guid companyId, Guid branchId, Guid itemId, CancellationToken cancellationToken) =>
        Task.FromResult(new EffectiveItemAccountMappingDto { ItemId = itemId, Accounts = [] });

    public Task<ItemGroupDto> CreateItemGroupAsync(Guid companyId, Guid branchId, CreateItemGroupRequest request, CancellationToken cancellationToken)
    {
        var dto = new ItemGroupDto
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            ParentId = request.ParentId,
            Code = request.Code,
            NameArabic = request.NameArabic,
            NameEnglish = request.NameEnglish,
            IsActive = true,
        };
        ItemGroups.Add(dto);
        return Task.FromResult(dto);
    }

    public Task<IReadOnlyList<UnitOfMeasureDto>> ListUnitsAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<UnitOfMeasureDto>>(Units);

    public Task<UnitOfMeasureDto> CreateUnitAsync(Guid companyId, Guid branchId, CreateUnitRequest request, CancellationToken cancellationToken)
    {
        var dto = new UnitOfMeasureDto
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            Code = request.Code,
            NameArabic = request.NameArabic,
            NameEnglish = request.NameEnglish,
            Symbol = request.Symbol,
            DecimalPlaces = request.DecimalPlaces,
            IsActive = true,
        };
        Units.Add(dto);
        return Task.FromResult(dto);
    }

    public Task<IReadOnlyList<UnitConversionDto>> ListUnitConversionsAsync(Guid companyId, Guid branchId, Guid? itemId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<UnitConversionDto>>(UnitConversions.Where(c => itemId == null || c.ItemId == itemId || c.ItemId == null).ToList());

    public Task<UnitConversionDto> CreateUnitConversionAsync(Guid companyId, Guid branchId, CreateUnitConversionRequest request, CancellationToken cancellationToken)
    {
        var dto = new UnitConversionDto
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            ItemId = request.ItemId,
            FromUnitId = request.FromUnitId,
            ToUnitId = request.ToUnitId,
            FromUnitCode = Units.FirstOrDefault(u => u.Id == request.FromUnitId)?.Code,
            ToUnitCode = Units.FirstOrDefault(u => u.Id == request.ToUnitId)?.Code,
            Factor = request.Factor,
        };
        UnitConversions.Add(dto);
        return Task.FromResult(dto);
    }

    public Task<IReadOnlyList<WarehouseDto>> ListWarehousesAsync(Guid companyId, Guid branchId, Guid? filterBranchId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<WarehouseDto>>(Warehouses);

    public Task<WarehouseDto> CreateWarehouseAsync(Guid companyId, Guid branchId, CreateWarehouseRequest request, CancellationToken cancellationToken)
    {
        var dto = new WarehouseDto
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            BranchId = branchId,
            Code = request.Code,
            NameArabic = request.NameArabic,
            NameEnglish = request.NameEnglish,
            Location = request.Location,
            IsActive = true,
        };
        Warehouses.Add(dto);
        return Task.FromResult(dto);
    }

    public Task<WarehouseDto> UpdateWarehouseAsync(Guid companyId, Guid branchId, Guid warehouseId, UpdateWarehouseRequest request, CancellationToken cancellationToken)
    {
        var wh = Warehouses.First(w => w.Id == warehouseId);
        wh.NameArabic = request.NameArabic;
        wh.NameEnglish = request.NameEnglish;
        wh.Location = request.Location;
        return Task.FromResult(wh);
    }

    public Task<StockMovementPageResponse> ListStockMovementsAsync(Guid companyId, Guid branchId, int page, int pageSize, Guid? warehouseId, Guid? itemId, DateTimeOffset? fromDate, DateTimeOffset? toDate, CancellationToken cancellationToken)
    {
        var filtered = StockMovements.AsEnumerable();
        if (warehouseId.HasValue) filtered = filtered.Where(m => m.WarehouseId == warehouseId.Value);
        if (itemId.HasValue) filtered = filtered.Where(m => m.ItemId == itemId.Value);
        var list = filtered.ToList();
        return Task.FromResult(new StockMovementPageResponse
        {
            Page = page,
            PageSize = pageSize,
            TotalCount = list.Count,
            Items = list.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
        });
    }

    public Task<StockTransferDto> CreateStockTransferAsync(Guid companyId, Guid branchId, CreateStockTransferRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new StockTransferDto { Id = Guid.NewGuid(), TransferNumber = "TR-001" });

    public Task<StockAdjustmentDto> CreateStockAdjustmentAsync(Guid companyId, Guid branchId, CreateStockAdjustmentRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new StockAdjustmentDto { Id = Guid.NewGuid(), AdjustmentNumber = "ADJ-001" });

    public Task<StockAdjustmentDto> ApproveStockAdjustmentAsync(Guid companyId, Guid branchId, Guid adjustmentId, InventoryDocumentActionRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new StockAdjustmentDto { Id = adjustmentId, AdjustmentNumber = "ADJ-001", Status = StockAdjustmentStatus.Approved });

    public Task<StockMovementDto> PostInternalIssueAsync(Guid companyId, Guid branchId, PostInternalIssueRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new StockMovementDto { Id = Guid.NewGuid(), Quantity = request.Quantity, UnitCost = request.UnitCost, TotalCost = "0.0000" });

    internal List<SalesInvoiceSummaryDto> Invoices { get; } = [];
    internal List<(int Page, string? Search, string? SortBy, bool? SortDescending)> SearchCalls { get; } = [];
    internal Func<int, string?, Guid?, Task<SalesInvoicePageResponse>?>? SearchOverride { get; set; }

    public Task<SalesInvoicePageResponse> ListSalesInvoicesAsync(Guid companyId, Guid branchId, int page, int pageSize, Guid? customerId, Guid? warehouseId, Guid? patternRevisionId, SalesInvoiceStatus? status, DateTimeOffset? fromDate, DateTimeOffset? toDate, CancellationToken cancellationToken)
    {
        var filtered = Invoices.AsEnumerable();
        if (customerId.HasValue) filtered = filtered.Where(i => i.CustomerId == customerId.Value);
        if (warehouseId.HasValue) filtered = filtered.Where(i => i.WarehouseId == warehouseId.Value);
        if (status.HasValue) filtered = filtered.Where(i => i.Status == status.Value);
        var list = filtered.ToList();
        return Task.FromResult(new SalesInvoicePageResponse
        {
            Page = page,
            PageSize = pageSize,
            TotalCount = list.Count,
            Items = list.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
        });
    }

    public Task<SalesInvoicePageResponse> SearchSalesInvoicesAsync(Guid companyId, Guid branchId, int page, int pageSize, string? search, Guid? customerId, Guid? warehouseId, Guid? patternRevisionId, SalesInvoiceStatus? status, DateTimeOffset? fromDate, DateTimeOffset? toDate, string? sortBy, bool? sortDescending, CancellationToken cancellationToken)
    {
        SearchCalls.Add((page, search, sortBy, sortDescending));
        if (SearchOverride?.Invoke(page, search, customerId) is { } overridden) return overridden;
        return SearchCore(companyId, branchId, page, pageSize, search, customerId, warehouseId, patternRevisionId, status, fromDate, toDate, cancellationToken);
    }

    private Task<SalesInvoicePageResponse> SearchCore(Guid companyId, Guid branchId, int page, int pageSize, string? search, Guid? customerId, Guid? warehouseId, Guid? patternRevisionId, SalesInvoiceStatus? status, DateTimeOffset? fromDate, DateTimeOffset? toDate, CancellationToken cancellationToken) =>
        ListSalesInvoicesAsync(companyId, branchId, page, pageSize, customerId, warehouseId, patternRevisionId, status, fromDate, toDate, cancellationToken)
            .ContinueWith(task =>
            {
                var response = task.Result;
                if (string.IsNullOrWhiteSpace(search)) return response;
                var term = search.Trim();
                var all = Invoices.Where(i => (customerId is null || i.CustomerId == customerId) && (warehouseId is null || i.WarehouseId == warehouseId) && (status is null || i.Status == status)
                    && ((i.InvoiceNumber?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) || (i.CustomerName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                        || (i.CustomerCode?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false))).ToList();
                return new SalesInvoicePageResponse { Page = page, PageSize = pageSize, TotalCount = all.Count, Items = all.Skip((page - 1) * pageSize).Take(pageSize).ToList() };
            }, TaskScheduler.Default);

    internal Func<string, Guid?, Guid?>? NavigationOverride { get; set; }
    internal List<SalesInvoiceListScope?> NavigationScopes { get; } = [];
    public Task<Guid?> NavigateSalesInvoiceAsync(Guid companyId, Guid branchId, string direction, Guid? fromInvoiceId, SalesInvoiceListScope? scope, CancellationToken cancellationToken)
    {
        NavigationScopes.Add(scope);
        if (NavigationOverride is not null) return Task.FromResult(NavigationOverride(direction, fromInvoiceId));
        var ids = Invoices.Select(i => i.Id).Where(i => i.HasValue).Select(i => i!.Value).ToList();
        if (ids.Count == 0) return Task.FromResult<Guid?>(null);
        var at = fromInvoiceId.HasValue ? ids.IndexOf(fromInvoiceId.Value) : -1;
        return Task.FromResult<Guid?>(direction.ToLowerInvariant() switch
        {
            "first" => ids[0],
            "last" => ids[^1],
            "previous" => at > 0 ? ids[at - 1] : null,
            _ => at >= 0 && at < ids.Count - 1 ? ids[at + 1] : null,
        });
    }

    internal SalesInvoiceDetailDto? SalesDetail { get; set; }
    public Task<SalesInvoiceDetailDto> GetSalesInvoiceAsync(Guid companyId, Guid branchId, Guid invoiceId, CancellationToken cancellationToken) =>
        Task.FromResult(SalesDetail ?? new SalesInvoiceDetailDto { Id = invoiceId, InvoiceNumber = "INV-001", Status = SalesInvoiceStatus.Draft, SubTotal = "0.0000", DiscountTotal = "0.0000", TaxTotal = "0.0000", AdditionsTotal = "0.0000", GrandTotal = "0.0000", Lines = [] });

    internal List<CreateSalesInvoiceRequest> SalesCreateRequests { get; } = [];

    public Task<SalesInvoiceDetailDto> CreateSalesInvoiceAsync(Guid companyId, Guid branchId, CreateSalesInvoiceRequest request, CancellationToken cancellationToken)
    {
        SalesCreateRequests.Add(request);
        return Task.FromResult(new SalesInvoiceDetailDto { Id = Guid.NewGuid(), InvoiceNumber = "INV-001", Status = SalesInvoiceStatus.Draft, SubTotal = "0.0000", DiscountTotal = "0.0000", TaxTotal = "0.0000", AdditionsTotal = "0.0000", GrandTotal = "0.0000", Lines = [] });
    }

    internal List<UpdateSalesInvoiceRequest> SalesUpdateRequests { get; } = [];
    internal List<string> SalesApproveVersions { get; } = [];

    public Task<SalesInvoiceDetailDto> UpdateSalesInvoiceAsync(Guid companyId, Guid branchId, Guid invoiceId, UpdateSalesInvoiceRequest request, CancellationToken cancellationToken)
    {
        SalesUpdateRequests.Add(request);
        return Task.FromResult(new SalesInvoiceDetailDto { Id = invoiceId, InvoiceNumber = "INV-001", Status = SalesInvoiceStatus.Draft, SubTotal = "0.0000", DiscountTotal = "0.0000", TaxTotal = "0.0000", AdditionsTotal = "0.0000", GrandTotal = "0.0000", Lines = [] });
    }

    internal List<SaveAndProcessSalesInvoiceRequest> SalesProcessingRequests { get; } = [];

    public Task<DocumentProcessingResultDto> SaveAndProcessSalesInvoiceAsync(Guid companyId, Guid branchId, SaveAndProcessSalesInvoiceRequest request, CancellationToken cancellationToken)
    {
        SalesProcessingRequests.Add(request);
        SalesCreateRequests.Add(request.Document!);
        return Task.FromResult(Processed(request, Guid.NewGuid()));
    }

    public Task<DocumentProcessingResultDto> SaveAndProcessUpdateSalesInvoiceAsync(Guid companyId, Guid branchId, Guid invoiceId, SaveAndProcessSalesInvoiceRequest request, CancellationToken cancellationToken)
    {
        SalesProcessingRequests.Add(request);
        var document = request.Document!;
        SalesUpdateRequests.Add(new UpdateSalesInvoiceRequest
        {
            ExpectedVersion = request.ExpectedVersion ?? string.Empty, CustomerId = document.CustomerId,
            WarehouseId = document.WarehouseId, InvoiceDateUtc = document.InvoiceDateUtc,
            PaymentMethod = document.PaymentMethod, CurrencyCode = document.CurrencyCode,
            ExchangeRate = document.ExchangeRate, CostCenterId = document.CostCenterId,
            Notes = document.Notes, Lines = document.Lines,
        });
        return Task.FromResult(Processed(request, invoiceId));
    }

    private static DocumentProcessingResultDto Processed(SaveAndProcessSalesInvoiceRequest request, Guid id) => new()
    {
        DocumentType = "SalesInvoice", DocumentId = id, DocumentNumber = "INV-001",
        Status = request.SaveAsDraft == true ? "Draft" : "Approved",
        Outcome = request.SaveAsDraft == true ? "SavedDraft" : "Approved",
        ApprovalApplied = request.SaveAsDraft != true, EffectivePostingMode = "ApproveOnly",
        Version = "AAAAAAAAB9E=", Message = request.SaveAsDraft == true ? "draft" : "approved",
    };

    public Task<SalesInvoiceDetailDto> ApproveSalesInvoiceAsync(Guid companyId, Guid branchId, Guid invoiceId, string expectedVersion, CancellationToken cancellationToken)
    {
        SalesApproveVersions.Add(expectedVersion);
        return Task.FromResult(new SalesInvoiceDetailDto { Id = invoiceId, InvoiceNumber = "INV-001", Status = SalesInvoiceStatus.Approved, SubTotal = "0.0000", DiscountTotal = "0.0000", TaxTotal = "0.0000", AdditionsTotal = "0.0000", GrandTotal = "0.0000", Lines = [] });
    }

    internal List<(Guid InvoiceId, string Version)> SalesDeleteRequests { get; } = [];
    public Task DeleteSalesInvoiceAsync(Guid companyId, Guid branchId, Guid invoiceId, string expectedVersion, CancellationToken cancellationToken)
    {
        SalesDeleteRequests.Add((invoiceId, expectedVersion));
        return Task.CompletedTask;
    }

    internal List<PostSalesInvoiceRequest> SalesPostRequests { get; } = [];
    internal bool FailSalesPostAfterSend { get; set; }
    public Task<SalesInvoiceDetailDto> PostSalesInvoiceAsync(Guid companyId, Guid branchId, Guid invoiceId, PostSalesInvoiceRequest request, CancellationToken cancellationToken)
    {
        SalesPostRequests.Add(request);
        if (FailSalesPostAfterSend) return Task.FromException<SalesInvoiceDetailDto>(new HttpRequestException("Response lost after sending the request."));
        return Task.FromResult(new SalesInvoiceDetailDto { Id = invoiceId, InvoiceNumber = "INV-001", Status = SalesInvoiceStatus.Posted, SubTotal = "0.0000", DiscountTotal = "0.0000", TaxTotal = "0.0000", AdditionsTotal = "0.0000", GrandTotal = "0.0000", Lines = [] });
    }

    public Task<SalesInvoiceDetailDto> ReverseSalesInvoiceAsync(Guid companyId, Guid branchId, Guid invoiceId, ReverseSalesInvoiceRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new SalesInvoiceDetailDto { Id = invoiceId, InvoiceNumber = "INV-001", Status = SalesInvoiceStatus.Reversed, SubTotal = "0.0000", DiscountTotal = "0.0000", TaxTotal = "0.0000", AdditionsTotal = "0.0000", GrandTotal = "0.0000", Lines = [] });

    internal List<CreateSalesReturnRequest> SalesReturnRequests { get; } = [];
    internal Guid SalesReturnResultId { get; } = Guid.NewGuid();
    internal bool FailSalesReturnAfterSend { get; set; }

    public Task<SalesInvoiceDetailDto> ReturnSalesInvoiceAsync(Guid companyId, Guid branchId, Guid invoiceId, CreateSalesReturnRequest request, CancellationToken cancellationToken)
    {
        SalesReturnRequests.Add(request);
        if (FailSalesReturnAfterSend) throw new HttpRequestException("lost response");
        return Task.FromResult(new SalesInvoiceDetailDto { Id = SalesReturnResultId, InvoiceNumber = "RET-001", Status = SalesInvoiceStatus.Posted, ReturnOfInvoiceId = invoiceId,
            SubTotal = "0.0000", DiscountTotal = "0.0000", TaxTotal = "0.0000", AdditionsTotal = "0.0000", GrandTotal = "0.0000", Lines = [] });
    }
}
