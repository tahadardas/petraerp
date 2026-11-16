using System.Net.Http;
using PetraERP.ApiClient;
using PetraERP.Desktop.GeneralLedger;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Tests;

public sealed class GeneralLedgerViewModelTests
{
    private static readonly string[] AllJournalPermissions =
        ["Ledger.Journals.View", "Ledger.Journals.Manage", "Ledger.Journals.Approve", "Ledger.Journals.Post", "Ledger.Journals.Reverse"];

    private static (GeneralLedgerViewModel Model, FakeLedgerClient Client, ShellFixture Shell, Guid Company, Guid Branch) Create(params string[] permissions)
    {
        var localization = new LocalizationService("ar");
        var client = new FakeLedgerClient();
        var shell = new ShellFixture();
        var model = new GeneralLedgerViewModel(client, shell, new DesktopOperation(localization), localization);
        model.Configure(permissions.Length == 0 ? AllJournalPermissions : permissions);
        var company = Guid.NewGuid();
        client.Companies[company] = ("JOD", ["USD"]);
        return (model, client, shell, company, Guid.NewGuid());
    }

    private static async Task OpenAsync(GeneralLedgerViewModel model, JournalDetailsResponse journal)
    {
        await model.LoadPageAsync(journal.CompanyId, journal.BranchId, "JOD", CancellationToken.None).ConfigureAwait(true);
        model.SelectedJournal = model.Journals.Single(item => item.Id == journal.Id);
        await model.OpenJournalCommand.ExecuteAsync(null).ConfigureAwait(true);
    }

    [Fact]
    public void LineBalancingCalculatesDebitCreditAndDifference()
    {
        var (model, _, _, _, _) = Create();
        using var _model = model;
        var line1 = new JournalLineRow { Debit = 250m, Credit = 0m, Rate = 1.0m };
        var line2 = new JournalLineRow { Debit = 0m, Credit = 200m, Rate = 1.0m };
        model.Lines.Add(line1);
        model.Lines.Add(line2);
        Assert.Equal(250m, model.TotalDebit);
        Assert.Equal(200m, model.TotalCredit);
        Assert.Equal(50m, model.Difference);
        Assert.False(model.IsBalanced);
        line2.Credit = 250m;
        Assert.Equal(0m, model.Difference);
        Assert.True(model.IsBalanced);
    }

    [Fact]
    public async Task NewDraftRequiresContextAndUsesTheSelectedCompanyBaseCurrency()
    {
        var (model, _, _, company, branch) = Create();
        using var _model = model;
        Assert.False(model.NewDraftCommand.CanExecute(null));
        await model.LoadPageAsync(company, branch, "SYP", CancellationToken.None).ConfigureAwait(true);
        Assert.True(model.NewDraftCommand.CanExecute(null));
        model.NewDraftCommand.Execute(null);
        Assert.True(model.IsEditorOpen);
        Assert.True(model.IsNewDraft);
        Assert.Equal("SYP", model.BaseCurrencyCode);
        Assert.Equal(50, model.Lines.Count);
        Assert.All(model.Lines, line => Assert.Equal("SYP", line.CurrencyCode));
        Assert.True(model.CanEditDraft);
        Assert.False(model.IsDraftReadOnly);
        Assert.Null(model.CurrentState);
    }

    [Fact]
    public async Task SwitchingCompanyClearsStaleJournalAndCurrencyContext()
    {
        var (model, client, _, companyA, branchA) = Create();
        using var _model = model;
        var companyB = Guid.NewGuid();
        client.Companies[companyA] = ("USD", ["EUR"]);
        client.Companies[companyB] = ("SYP", []);
        var journalA = client.Add(companyA, branchA, JournalState.Draft, "USD");

        await model.LoadPageAsync(companyA, branchA, "USD", CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(["USD", "EUR"], model.Currencies);
        model.SelectedJournal = model.Journals.Single();
        await model.OpenJournalCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal(journalA.Id, model.Journals.Single().Id);
        Assert.Equal("USD", model.BaseCurrencyCode);

        await model.LoadPageAsync(companyB, Guid.NewGuid(), "SYP", CancellationToken.None).ConfigureAwait(true);
        Assert.False(model.IsEditorOpen);
        Assert.Empty(model.Journals);
        Assert.Empty(model.Lines);
        Assert.Equal(["SYP"], model.Currencies);
        Assert.Equal("SYP", model.BaseCurrencyCode);
        model.NewDraftCommand.Execute(null);
        Assert.All(model.Lines, line => Assert.Equal("SYP", line.CurrencyCode));
        Assert.DoesNotContain(model.Lines, line => line.CurrencyCode is "USD" or "JOD");
    }

    [Fact]
    public async Task ExistingJournalKeepsItsHistoricalBaseCurrency()
    {
        var (model, client, _, company, branch) = Create();
        using var _model = model;
        var historical = client.Add(company, branch, JournalState.Posted, "USD");
        await model.LoadPageAsync(company, branch, "SYP", CancellationToken.None).ConfigureAwait(true);
        model.SelectedJournal = model.Journals.Single();
        await model.OpenJournalCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal(historical.Id, model.SelectedJournal!.Id);
        Assert.Equal("USD", model.BaseCurrencyCode);
    }

    [Fact]
    public async Task SaveDraftCreatesThroughTheClientWithInvariantDecimalText()
    {
        var (model, client, _, company, branch) = Create();
        using var _model = model;
        await model.LoadPageAsync(company, branch, "JOD", CancellationToken.None).ConfigureAwait(true);
        model.NewDraftCommand.Execute(null);
        model.JournalDescription = "قيد مصروفات صيانة";
        model.Lines[0].AccountId = Guid.NewGuid();
        model.Lines[0].Debit = 100m;
        model.Lines[1].AccountId = Guid.NewGuid();
        model.Lines[1].Credit = 100m;
        await model.SaveDraftCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.NotNull(client.LastCreateRequest);
        Assert.Equal("100.0000", client.LastCreateRequest.Lines[0].TransactionDebit);
        Assert.Equal("100.0000", client.LastCreateRequest.Lines[1].TransactionCredit);
        Assert.Equal("JOD", client.LastCreateRequest.Lines[0].RateTargetCurrencyCode);
        Assert.Equal(JournalState.Draft, model.CurrentState);
    }

    [Theory]
    [InlineData(JournalState.Draft, true, true, true, false, false, false, false)]
    [InlineData(JournalState.Validated, false, false, false, true, true, false, false)]
    [InlineData(JournalState.Approved, false, false, false, false, true, true, false)]
    [InlineData(JournalState.Posted, false, false, false, false, false, false, true)]
    [InlineData(JournalState.Reversed, false, false, false, false, false, false, false)]
    public async Task OpenedJournalCommandsFollowTheTypedServerState(JournalState state, bool edit, bool validate, bool delete, bool approve,
        bool returnToDraft, bool post, bool createReversal)
    {
        var (model, client, _, company, branch) = Create();
        using var _model = model;
        var complete = client.Add(company, branch, state);
        complete.Lines = [new() { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), TransactionCurrencyCode = "JOD", RateValue = "1", TransactionDebit = "100", TransactionCredit = "0" },
            new() { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), TransactionCurrencyCode = "JOD", RateValue = "1", TransactionDebit = "0", TransactionCredit = "100" }];
        await OpenAsync(model, complete).ConfigureAwait(true);
        model.ActionReason = "سبب";
        Assert.Equal(state, model.CurrentState);
        Assert.Equal(edit, model.CanEditDraft);
        Assert.Equal(!edit, model.IsDraftReadOnly);
        Assert.Equal(validate, model.ValidateDraftCommand.CanExecute(null));
        Assert.Equal(delete, model.DeleteDraftCommand.CanExecute(null));
        Assert.Equal(approve, model.ApproveDraftCommand.CanExecute(null));
        Assert.Equal(returnToDraft, model.ReturnToDraftCommand.CanExecute(null));
        Assert.Equal(post, model.PostJournalCommand.CanExecute(null));
        Assert.Equal(createReversal, model.CreateReversalCommand.CanExecute(null));
        Assert.False(model.PostReversalCommand.CanExecute(null));
        Assert.Equal(edit, model.AddLineCommand.CanExecute(null));
        Assert.Equal(edit, model.SaveDraftCommand.CanExecute(null));
    }

    [Fact]
    public async Task WorkflowTransitionsRespectPermissionsAndReloadServerState()
    {
        var (model, client, _, company, branch) = Create("Ledger.Journals.View", "Ledger.Journals.Manage");
        using var _model = model;
        await OpenAsync(model, client.Add(company, branch, JournalState.Draft)).ConfigureAwait(true);
        Assert.True(model.CanValidateDraft);
        await model.ValidateDraftCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal(JournalState.Validated, model.CurrentState);
        Assert.False(model.CanApproveDraft);
        Assert.True(model.IsDraftReadOnly);

        model.Configure(["Ledger.Journals.View", "Ledger.Journals.Manage", "Ledger.Journals.Approve"]);
        Assert.True(model.CanApproveDraft);
        await model.ApproveDraftCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal(JournalState.Approved, model.CurrentState);
        Assert.True(model.IsDraftReadOnly);
        model.ActionReason = "ترحيل";
        Assert.False(model.PostJournalCommand.CanExecute(null)); // no Ledger.Journals.Post
        Assert.True(model.ReturnToDraftCommand.CanExecute(null));
    }

    [Fact]
    public async Task PostingRequiresReasonAndConfirmationThenReloadsTheJournal()
    {
        var (model, client, shell, company, branch) = Create();
        using var _model = model;
        var journal = client.Add(company, branch, JournalState.Approved);
        await OpenAsync(model, journal).ConfigureAwait(true);
        Assert.False(model.PostJournalCommand.CanExecute(null)); // reason required

        model.ActionReason = "ترحيل نهاية الشهر";
        shell.Confirm = false;
        await model.PostJournalCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal("D_ConfirmPostJournal", shell.LastConfirmation);
        Assert.Empty(client.PostCalls);

        shell.Confirm = true;
        await model.PostJournalCommand.ExecuteAsync(null).ConfigureAwait(true);
        var call = Assert.Single(client.PostCalls);
        Assert.NotEqual(Guid.Empty, call.Key);
        Assert.Equal("ترحيل نهاية الشهر", call.Reason);
        Assert.Equal(JournalState.Posted, model.CurrentState);
        Assert.Equal(client.Get(journal.Id).Version, model.CurrentVersion);
        Assert.True(model.IsDraftReadOnly);
        Assert.False(model.PostJournalCommand.CanExecute(null));
        Assert.NotNull(model.LastPostingReceipt);
        Assert.False(model.HasPendingPostingAttempt);
    }

    [Fact]
    public async Task AmbiguousTransportFailureRetriesWithTheSameIdempotencyKey()
    {
        var (model, client, _, company, branch) = Create();
        using var _model = model;
        var journal = client.Add(company, branch, JournalState.Approved);
        await OpenAsync(model, journal).ConfigureAwait(true);
        model.ActionReason = "ترحيل";
        // The request reached the server and committed, but the response was lost.
        client.PostingFailures.Enqueue((new HttpRequestException("connection reset"), true));

        await model.PostJournalCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.True(model.HasPendingPostingAttempt);
        Assert.Equal(JournalState.Approved, model.CurrentState);
        await model.PostJournalCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Equal(2, client.PostCalls.Count);
        Assert.Equal(client.PostCalls[0].Key, client.PostCalls[1].Key);
        Assert.Equal(JournalState.Posted, model.CurrentState);
        Assert.Equal(1, client.Get(journal.Id).PostingSequence);
        Assert.False(model.HasPendingPostingAttempt);
    }

    [Fact]
    public async Task DefinitiveRejectionReleasesTheKeyAndNewAttemptsGetFreshKeys()
    {
        var (model, client, _, company, branch) = Create();
        using var _model = model;
        await OpenAsync(model, client.Add(company, branch, JournalState.Approved)).ConfigureAwait(true);
        model.ActionReason = "ترحيل";
        client.PostingFailures.Enqueue((new PetraApiException("closed", 422, "{\"code\":\"ledger.period_closed\"}",
            new Dictionary<string, IEnumerable<string>>(), null), false));

        await model.PostJournalCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.False(model.HasPendingPostingAttempt);
        await model.PostJournalCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal(2, client.PostCalls.Count);
        Assert.NotEqual(client.PostCalls[0].Key, client.PostCalls[1].Key);
    }

    [Fact]
    public async Task ReversalFlowCreatesValidatesApprovesAndPostsThroughTheReversalEndpoint()
    {
        var (model, client, shell, company, branch) = Create();
        using var _model = model;
        var original = client.Add(company, branch, JournalState.Posted);
        await OpenAsync(model, original).ConfigureAwait(true);

        Assert.False(model.CreateReversalCommand.CanExecute(null)); // audit reason required
        model.ActionReason = "تصحيح قيد خاطئ";
        await model.CreateReversalCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal("D_ConfirmCreateReversal", shell.LastConfirmation);
        Assert.Equal(JournalState.Draft, model.CurrentState);
        Assert.False(model.CanEditDraft); // reversal content is fixed
        Assert.True(model.CanValidateDraft);

        await model.ValidateDraftCommand.ExecuteAsync(null).ConfigureAwait(true);
        await model.ApproveDraftCommand.ExecuteAsync(null).ConfigureAwait(true);
        model.ActionReason = "ترحيل العكس";
        Assert.False(model.PostJournalCommand.CanExecute(null)); // ordinary post rejects reversals
        Assert.True(model.PostReversalCommand.CanExecute(null));

        await model.PostReversalCommand.ExecuteAsync(null).ConfigureAwait(true);
        var call = Assert.Single(client.ReversalCalls);
        Assert.Equal(original.Id, call.OriginalId);
        Assert.Empty(client.PostCalls);
        Assert.Equal(JournalState.Posted, model.CurrentState);
        Assert.Equal(JournalState.Reversed, client.Get(original.Id).State);

        // The reversed original is immutable and cannot be reversed again.
        model.SelectedJournal = model.Journals.Single(item => item.Id == original.Id);
        await model.OpenJournalCommand.ExecuteAsync(null).ConfigureAwait(true);
        model.ActionReason = "مرة ثانية";
        Assert.Equal(JournalState.Reversed, model.CurrentState);
        Assert.False(model.CreateReversalCommand.CanExecute(null));
        Assert.True(model.IsDraftReadOnly);
    }

    [Fact]
    public async Task ReversalPermissionIsRequiredToPostAReversal()
    {
        var (model, client, _, company, branch) = Create("Ledger.Journals.View", "Ledger.Journals.Manage", "Ledger.Journals.Post");
        using var _model = model;
        var original = client.Add(company, branch, JournalState.Posted);
        await OpenAsync(model, client.Add(company, branch, JournalState.Approved, reverses: original.Id)).ConfigureAwait(true);
        model.ActionReason = "سبب";
        Assert.False(model.PostReversalCommand.CanExecute(null));
        Assert.False(model.PostJournalCommand.CanExecute(null));
    }
}
