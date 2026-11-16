using PetraERP.Api.Security;

namespace PetraERP.Api.Tests;

public sealed class WriteAdmissionTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static readonly Guid CompanyA = Guid.NewGuid();
    private static readonly Guid CompanyB = Guid.NewGuid();

    [Fact]
    public async Task RequestsBeyondTheLimitWaitInLineAndProceedWhenASlotFrees()
    {
        using var gate = new WriteAdmission(2, 2, TimeSpan.FromSeconds(5));
        var first = await gate.EnterAsync(CompanyA, Cancellation).ConfigureAwait(true);
        using var second = await gate.EnterAsync(CompanyA, Cancellation).ConfigureAwait(true);
        var third = gate.EnterAsync(CompanyA, Cancellation);
        await Task.Delay(100, Cancellation).ConfigureAwait(true);
        Assert.False(third.IsCompleted);
        var waiting = gate.Snapshot();
        Assert.Equal(2, waiting.InFlight);
        Assert.Equal(1, waiting.Waiting);

        first.Dispose();
        using var admitted = await third.WaitAsync(TimeSpan.FromSeconds(2), Cancellation).ConfigureAwait(true);
        var done = gate.Snapshot();
        Assert.Equal(3, done.Admitted);
        Assert.Equal(0, done.Waiting);
        Assert.True(done.WaitMaxMs >= 90L, "the third request's wait is recorded");
    }

    [Fact]
    public async Task ATimedOutRequestIsCountedAndLeaksNoSlot()
    {
        using var gate = new WriteAdmission(1, 1, TimeSpan.FromMilliseconds(150));
        var held = await gate.EnterAsync(CompanyA, Cancellation).ConfigureAwait(true);
        await Assert.ThrowsAsync<ServerBusyException>(() => gate.EnterAsync(CompanyA, Cancellation)).ConfigureAwait(true);
        var snapshot = gate.Snapshot();
        Assert.Equal(1, snapshot.TimedOut);
        Assert.Equal(0, snapshot.Waiting);
        held.Dispose();
        using var again = await gate.EnterAsync(CompanyA, Cancellation).ConfigureAwait(true); // no slot was lost to the timeout
        Assert.Equal(1, gate.Snapshot().InFlight);
    }

    [Fact]
    public async Task ABusyCompanyCannotTakeEverySlotFromAQuietOne()
    {
        using var gate = new WriteAdmission(3, 2, TimeSpan.FromSeconds(5));
        using var a1 = await gate.EnterAsync(CompanyA, Cancellation).ConfigureAwait(true);
        using var a2 = await gate.EnterAsync(CompanyA, Cancellation).ConfigureAwait(true);
        var a3 = gate.EnterAsync(CompanyA, Cancellation);          // waits for its own company allowance, holding no global slot
        await Task.Delay(100, Cancellation).ConfigureAwait(true);
        Assert.False(a3.IsCompleted);
        using var b1 = await gate.EnterAsync(CompanyB, Cancellation).WaitAsync(TimeSpan.FromSeconds(2), Cancellation).ConfigureAwait(true);
        Assert.Equal(3, gate.Snapshot().InFlight);
        a1.Dispose();
        using var admittedA3 = await a3.WaitAsync(TimeSpan.FromSeconds(2), Cancellation).ConfigureAwait(true);
        Assert.Equal(2, gate.Snapshot().CompaniesTracked);
    }

    [Fact]
    public async Task CancellationWhileWaitingReleasesNothingAndCountsNoTimeout()
    {
        using var gate = new WriteAdmission(1, 1, TimeSpan.FromSeconds(5));
        using var held = await gate.EnterAsync(CompanyA, Cancellation).ConfigureAwait(true);
        using var cancel = new CancellationTokenSource();
        var waiter = gate.EnterAsync(CompanyA, cancel.Token);
        await cancel.CancelAsync().ConfigureAwait(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter).ConfigureAwait(true);
        var snapshot = gate.Snapshot();
        Assert.Equal(0, snapshot.Waiting);
        Assert.Equal(0, snapshot.TimedOut);
        Assert.Equal(1, snapshot.InFlight);
    }
}
