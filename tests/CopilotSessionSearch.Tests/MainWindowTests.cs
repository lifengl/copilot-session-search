#nullable enable

using CopilotSessionSearch;

namespace CopilotSessionSearch.Tests;

public sealed class MainWindowTests
{
    [Fact]
    public async Task ShutdownWaitStopsWaitingAfterTimeout()
    {
        var shutdownCompletion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool> waitTask = MainWindow.WaitForShutdownAsync(
            shutdownCompletion.Task,
            TimeSpan.FromMilliseconds(25));

        Task firstCompleted = await Task.WhenAny(
            waitTask,
            Task.Delay(TimeSpan.FromSeconds(2)));

        Assert.Same(waitTask, firstCompleted);
        Assert.False(await waitTask);
        shutdownCompletion.TrySetResult();
    }

    [Fact]
    public async Task ShutdownWaitPropagatesCleanupTimeoutException()
    {
        var expectedException = new TimeoutException(
            "Cleanup timed out internally.");

        TimeoutException actualException =
            await Assert.ThrowsAsync<TimeoutException>(
                () => MainWindow.WaitForShutdownAsync(
                    Task.FromException(expectedException),
                    TimeSpan.FromSeconds(1)));

        Assert.Same(expectedException, actualException);
    }
}
