using System.Net.Http;
using System.Text.Json;
using PetraERP.ApiClient;
using PetraERP.Desktop.GeneralLedger;

namespace PetraERP.Desktop.Tests;

public sealed class JournalStateContractTests
{
    private static readonly HashSet<string> All = ["Ledger.Journals.View", "Ledger.Journals.Manage", "Ledger.Journals.Approve", "Ledger.Journals.Post", "Ledger.Journals.Reverse"];

    [Fact]
    public void TransportStatesTravelByNameAndMatchTheDomainLifecycleExactly()
    {
        // Server Domain/Contracts: Draft=1..Reversed=5; the generated client numbers from zero. Only names may cross the wire.
        Assert.Equal(["Draft", "Validated", "Approved", "Posted", "Reversed"], Enum.GetNames<JournalState>());
        Assert.Equal(Enum.GetNames<PetraERP.Domain.GeneralLedger.JournalEntryState>(), Enum.GetNames<JournalState>());
        Assert.Equal(Enum.GetNames<PetraERP.Domain.GeneralLedger.JournalEntryState>(), Enum.GetNames<PetraERP.Contracts.Ledger.JournalState>());
        foreach (var domain in Enum.GetValues<PetraERP.Domain.GeneralLedger.JournalEntryState>())
        {
            Assert.Equal((int)domain, (int)Enum.Parse<PetraERP.Contracts.Ledger.JournalState>(domain.ToString()));
        }

        var options = new JsonSerializerOptions();
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
        foreach (var state in Enum.GetValues<JournalState>())
        {
            var json = JsonSerializer.Serialize(state, options);
            Assert.Equal($"\"{state}\"", json);
            Assert.Equal(state.ToString(), JsonSerializer.Deserialize<PetraERP.Contracts.Ledger.JournalState>(json, options).ToString());
        }

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<JournalState>("1", options));
    }

    [Theory]
    [InlineData(JournalState.Draft, true, true, true, false, false, false, false, false)]
    [InlineData(JournalState.Validated, false, false, false, true, true, false, false, false)]
    [InlineData(JournalState.Approved, false, false, false, false, true, true, false, false)]
    [InlineData(JournalState.Posted, false, false, false, false, false, false, false, true)]
    [InlineData(JournalState.Reversed, false, false, false, false, false, false, false, false)]
    public void CommandMatrixForEveryState(JournalState state, bool edit, bool validate, bool delete, bool approve, bool returnToDraft,
        bool post, bool postReversal, bool createReversal)
    {
        var actual = JournalCommandPolicy.Evaluate(state, isReversalDraft: false, isReversed: false, All);
        Assert.Equal(new JournalCommandAvailability(edit, validate, delete, approve, returnToDraft, post, postReversal, createReversal), actual);
    }

    [Fact]
    public void PermissionsReversalDraftsAndReversedOriginalsNarrowTheMatrix()
    {
        Assert.False(JournalCommandPolicy.Evaluate(JournalState.Approved, false, false, new HashSet<string> { "Ledger.Journals.Manage" }).Post);
        Assert.False(JournalCommandPolicy.Evaluate(JournalState.Validated, false, false, new HashSet<string> { "Ledger.Journals.Manage" }).Approve);
        Assert.False(JournalCommandPolicy.Evaluate(JournalState.Draft, false, false, new HashSet<string> { "Ledger.Journals.View" }).Edit);
        var reversalDraft = JournalCommandPolicy.Evaluate(JournalState.Draft, isReversalDraft: true, false, All);
        Assert.False(reversalDraft.Edit);
        Assert.True(reversalDraft.Validate);
        var approvedReversal = JournalCommandPolicy.Evaluate(JournalState.Approved, isReversalDraft: true, false, All);
        Assert.False(approvedReversal.Post);
        Assert.True(approvedReversal.PostReversal);
        Assert.False(JournalCommandPolicy.Evaluate(JournalState.Approved, true, false, new HashSet<string> { "Ledger.Journals.Post" }).PostReversal);
        Assert.False(JournalCommandPolicy.Evaluate(JournalState.Posted, false, isReversed: true, All).CreateReversal);
        Assert.True(JournalCommandPolicy.Evaluate(null, false, false, All).Edit);
    }

    [Fact]
    public void TrackerReusesKeysOnlyForAmbiguousOutcomesOfTheSameIntent()
    {
        var keys = new Queue<Guid>([Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()]);
        var tracker = new PostingAttemptTracker(keys.Dequeue);
        var intent = new PostingIntent(Guid.NewGuid(), "AAAAAAAAAAE=", null, null, "ترحيل");
        var first = tracker.KeyFor(intent);
        Assert.True(tracker.Complete(intent, new HttpRequestException("lost")));
        Assert.Equal(first, tracker.KeyFor(intent));
        Assert.True(tracker.Complete(intent, new PetraApiException("busy", 503, null, new Dictionary<string, IEnumerable<string>>(), null)));
        Assert.Equal(first, tracker.KeyFor(intent));

        var changed = intent with { Reason = "سبب آخر" };
        var second = tracker.KeyFor(changed);
        Assert.NotEqual(first, second);

        Assert.False(tracker.Complete(changed, new PetraApiException("closed", 422, null, new Dictionary<string, IEnumerable<string>>(), null)));
        Assert.False(tracker.HasPendingAttempt);
        Assert.NotEqual(second, tracker.KeyFor(changed));
        Assert.False(tracker.Complete(changed, null));
        Assert.False(tracker.HasPendingAttempt);
    }

    [Theory]
    [InlineData(409, false)]
    [InlineData(422, false)]
    [InlineData(403, false)]
    [InlineData(408, true)]
    [InlineData(500, true)]
    [InlineData(503, true)]
    public void AmbiguityClassification(int status, bool ambiguous) =>
        Assert.Equal(ambiguous, PostingAttemptTracker.IsAmbiguous(new PetraApiException("x", status, null, new Dictionary<string, IEnumerable<string>>(), null)));

    [Fact]
    public void LedgerErrorCodesMapToLocalizedMessages()
    {
        var localization = new PetraERP.Desktop.Localization.LocalizationService("ar");
        foreach (var (code, key) in new[]
        {
            ("ledger.period_closed", "PeriodClosed"), ("ledger.account_invalid", "AccountInvalid"), ("ledger.cost_center_required", "CostCenterRequired"),
            ("ledger.currency_invalid", "CurrencyInvalid"), ("ledger.journal_state_invalid", "JournalStateInvalid"),
            ("concurrency.conflict", "Conflict"), ("ledger.posting_conflict", "PostingConflict"),
        })
        {
            var exception = new PetraApiException("x", 422, $"{{\"code\":\"{code}\"}}", new Dictionary<string, IEnumerable<string>>(), null);
            Assert.Equal(key, PetraERP.Desktop.Connection.DesktopErrors.Key(exception));
            foreach (var language in new[] { "ar", "en" })
            {
                localization.SetLanguage(language);
                Assert.NotEqual("D_" + key, localization["D_" + key]);
            }
        }
    }
}
