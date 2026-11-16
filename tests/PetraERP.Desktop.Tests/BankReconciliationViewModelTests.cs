using PetraERP.ApiClient;
using PetraERP.Application.Security;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Management;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Tests;

public sealed class BankReconciliationViewModelTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>Server stand-in: holds the cleared set and recomputes the difference the way the real service does.</summary>
    private sealed class FakeClient : IBankReconciliationClient
    {
        private readonly Guid first = Guid.NewGuid();
        private readonly Guid second = Guid.NewGuid();
        private readonly HashSet<Guid> cleared = [];
        private int version = 1;
        private bool completed;
        public int Starts { get; private set; }
        public int Discards { get; private set; }

        private BankReconciliationResponse Snapshot()
        {
            var amounts = new Dictionary<Guid, decimal> { [first] = 1000m, [second] = -300m };
            var clearedTotal = cleared.Sum(id => amounts[id]);
            return new()
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), BranchId = Guid.Empty, AccountId = Guid.Empty, AccountCode = "1102",
                StatementDate = new DateOnly(2026, 1, 31), StatementBalance = "700.0000", PriorClearedBalance = "0.0000",
                ClearedInThisReconciliation = clearedTotal.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture),
                Difference = (700m - clearedTotal).ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture),
                BookBalanceAtStatementDate = "700.0000", Status = completed ? "Completed" : "Draft", Version = version.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Lines = amounts.Select(a => new BankReconciliationLineResponse
                {
                    JournalLineId = a.Key, JournalId = Guid.NewGuid(), AccountingDate = new DateOnly(2026, 1, 5), Description = "line",
                    Amount = a.Value.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture), Cleared = cleared.Contains(a.Key),
                }).ToArray(),
            };
        }

        public Task<IReadOnlyList<BankReconciliationSummaryResponse>> ListAsync(Guid companyId, Guid branchId, Guid? accountId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BankReconciliationSummaryResponse>>(Starts == 0 ? [] : [new() { Id = Guid.Empty, AccountId = Guid.Empty, AccountCode = "1102", StatementDate = new DateOnly(2026, 1, 31), StatementBalance = "700.0000", Difference = "0", Status = completed ? "Completed" : "Draft" }]);

        public Task<BankReconciliationResponse> StartAsync(Guid companyId, Guid branchId, Guid accountId, DateOnly statementDate, string statementBalance, CancellationToken cancellationToken)
        {
            Starts++;
            LastStatementBalance = statementBalance;
            return Task.FromResult(Snapshot());
        }

        public string? LastStatementBalance { get; private set; }

        public Task<BankReconciliationResponse> GetAsync(Guid companyId, Guid branchId, Guid reconciliationId, CancellationToken cancellationToken) => Task.FromResult(Snapshot());

        public Task<BankReconciliationResponse> SetClearedAsync(Guid companyId, Guid branchId, Guid reconciliationId, IReadOnlyList<Guid> lineIds, bool clear, string expectedVersion, CancellationToken cancellationToken)
        {
            Assert.Equal(version.ToString(System.Globalization.CultureInfo.InvariantCulture), expectedVersion);
            foreach (var id in lineIds) { if (clear) cleared.Add(id); else cleared.Remove(id); }
            version++;
            return Task.FromResult(Snapshot());
        }

        public Task<BankReconciliationResponse> CompleteAsync(Guid companyId, Guid branchId, Guid reconciliationId, string expectedVersion, CancellationToken cancellationToken)
        {
            completed = true;
            version++;
            return Task.FromResult(Snapshot());
        }

        public Task DiscardAsync(Guid companyId, Guid branchId, Guid reconciliationId, string expectedVersion, CancellationToken cancellationToken)
        {
            Discards++;
            return Task.CompletedTask;
        }
    }

    private static async Task<(BankReconciliationViewModel Model, FakeClient Client)> OpenAsync(params string[] permissions)
    {
        var fixture = new ShellFixture();
        var loc = new LocalizationService("en");
        var client = new FakeClient();
        var model = new BankReconciliationViewModel(client, new FakeLedgerClient(), new NotificationCenter(loc), new(loc), loc);
        await model.ActivateAsync(new ShellContext(fixture.Company, fixture.Branch, permissions.ToHashSet(StringComparer.Ordinal)), null, Cancellation).ConfigureAwait(true);
        model.AccountId = Guid.NewGuid();
        model.StatementDate = new DateTime(2026, 1, 31);
        model.StatementBalanceText = "700";
        return (model, client);
    }

    [Fact]
    public async Task CompleteIsOfferedOnlyWhenTheServerDifferenceIsZero()
    {
        var (model, client) = await OpenAsync(BankReconciliationViewModel.ViewPermission, BankReconciliationViewModel.ManagePermission).ConfigureAwait(true);
        using var _ = model;
        await model.OpenCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal("700.0000", client.LastStatementBalance);
        Assert.True(model.IsDraft);
        Assert.Equal(2, model.Rows.Count);
        Assert.False(model.CompleteCommand.CanExecute(null));

        model.SelectedRow = model.Rows[0];
        await model.ToggleCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.True(model.Rows[0].Cleared);
        Assert.False(model.CompleteCommand.CanExecute(null)); // 700 - 1000 = -300, still not zero.

        await model.ClearAllCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.True(model.Rows.All(r => r.Cleared));
        Assert.True(model.CompleteCommand.CanExecute(null));

        await model.CompleteCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.False(model.IsDraft);
        Assert.False(model.ToggleCommand.CanExecute(null));
        Assert.False(model.DiscardCommand.CanExecute(null));
    }

    [Fact]
    public async Task ViewOnlyUsersCannotStartOrChangeAReconciliation()
    {
        var (model, client) = await OpenAsync(BankReconciliationViewModel.ViewPermission).ConfigureAwait(true);
        using var _ = model;
        Assert.False(model.OpenCommand.CanExecute(null));
        Assert.Equal(0, client.Starts);
    }

    [Fact]
    public async Task DiscardReturnsToTheStartForm()
    {
        var (model, client) = await OpenAsync(BankReconciliationViewModel.ViewPermission, BankReconciliationViewModel.ManagePermission).ConfigureAwait(true);
        using var _ = model;
        await model.OpenCommand.ExecuteAsync(null).ConfigureAwait(true);
        await model.DiscardCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal(1, client.Discards);
        Assert.False(model.IsOpen);
        Assert.Empty(model.Rows);
    }
}
