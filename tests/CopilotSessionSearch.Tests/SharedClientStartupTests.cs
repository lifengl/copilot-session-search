#nullable enable

using CopilotSessionSearch.Services;

namespace CopilotSessionSearch.Tests;

public sealed class SharedClientStartupTests
{
    [Fact]
    public async Task CancelingOneWaitDoesNotCancelOrPoisonSharedStartup()
    {
        var startupInvoked = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var startupCompletion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken startupToken = default;
        int startupCallCount = 0;
        using var lifetimeCancellationSource =
            new CancellationTokenSource();
        var startup = new SharedClientStartup(
            cancellationToken =>
            {
                Interlocked.Increment(
                    ref startupCallCount);
                startupToken = cancellationToken;
                startupInvoked.TrySetResult();
                return startupCompletion.Task;
            },
            () => Task.CompletedTask,
            lifetimeCancellationSource.Token);
        using var firstWaitCancellation =
            new CancellationTokenSource();

        Task firstWait = startup.WaitAsync(
            firstWaitCancellation.Token);
        await startupInvoked.Task;
        firstWaitCancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => firstWait);
        Assert.Equal(
            lifetimeCancellationSource.Token,
            startupToken);
        Assert.Equal(1, Volatile.Read(ref startupCallCount));

        startupCompletion.TrySetResult();
        await startup.WaitAsync(CancellationToken.None);

        Assert.Equal(1, Volatile.Read(ref startupCallCount));
    }

    [Fact]
    public async Task FaultedStartupIsRetriedByNextCaller()
    {
        int startupCallCount = 0;
        int resetCallCount = 0;
        Task? cachedClientStartup = null;
        using var lifetimeCancellationSource =
            new CancellationTokenSource();
        var startup = new SharedClientStartup(
            cancellationToken =>
            {
                Assert.Equal(
                    lifetimeCancellationSource.Token,
                    cancellationToken);
                int callCount = Interlocked.Increment(
                    ref startupCallCount);
                cachedClientStartup ??= callCount == 1
                    ? Task.FromException(
                        new IOException(
                            "Transient startup failure."))
                    : Task.CompletedTask;
                return cachedClientStartup;
            },
            () =>
            {
                Interlocked.Increment(ref resetCallCount);
                cachedClientStartup = null;
                return Task.CompletedTask;
            },
            lifetimeCancellationSource.Token);

        await Assert.ThrowsAsync<IOException>(
            () => startup.WaitAsync(CancellationToken.None));
        await startup.WaitAsync(CancellationToken.None);

        Assert.Equal(2, Volatile.Read(ref startupCallCount));
        Assert.Equal(1, Volatile.Read(ref resetCallCount));
    }

    [Fact]
    public async Task LifetimeCancellationStopsPendingStartup()
    {
        using var lifetimeCancellationSource =
            new CancellationTokenSource();
        var startup = new SharedClientStartup(
            cancellationToken => Task.Delay(
                Timeout.InfiniteTimeSpan,
                cancellationToken),
            () => Task.CompletedTask,
            lifetimeCancellationSource.Token);
        using var waitCancellationSource =
            new CancellationTokenSource();

        Task firstWait = startup.WaitAsync(
            waitCancellationSource.Token);
        waitCancellationSource.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => firstWait);

        lifetimeCancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => startup.WaitAsync(CancellationToken.None));
    }
}
