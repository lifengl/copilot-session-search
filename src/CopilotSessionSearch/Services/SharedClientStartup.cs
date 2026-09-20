#nullable enable

namespace CopilotSessionSearch.Services;

internal sealed class SharedClientStartup
{
    private readonly CancellationToken _lifetimeCancellationToken;
    private readonly Func<Task> _resetAsync;
    private readonly Func<CancellationToken, Task> _startAsync;
    private readonly object _gate = new();
    private Task? _startupTask;

    public SharedClientStartup(
        Func<CancellationToken, Task> startAsync,
        Func<Task> resetAsync,
        CancellationToken lifetimeCancellationToken)
    {
        ArgumentNullException.ThrowIfNull(startAsync);
        ArgumentNullException.ThrowIfNull(resetAsync);
        _startAsync = startAsync;
        _resetAsync = resetAsync;
        _lifetimeCancellationToken =
            lifetimeCancellationToken;
    }

    public Task WaitAsync(
        CancellationToken cancellationToken)
    {
        Task startupTask;
        lock (_gate)
        {
            if (_startupTask is
                {
                    IsCanceled: true,
                }
                or
                {
                    IsFaulted: true,
                })
            {
                _startupTask = ResetAndStartAsync();
            }

            startupTask = _startupTask
                ??= _startAsync(_lifetimeCancellationToken);
        }

        return startupTask.WaitAsync(cancellationToken);
    }

    private async Task ResetAndStartAsync()
    {
        await _resetAsync().ConfigureAwait(false);
        _lifetimeCancellationToken.ThrowIfCancellationRequested();
        await _startAsync(
            _lifetimeCancellationToken).ConfigureAwait(false);
    }
}
