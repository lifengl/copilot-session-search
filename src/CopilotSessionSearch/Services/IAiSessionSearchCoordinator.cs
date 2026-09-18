#nullable enable

using CopilotSessionSearch.Models;

namespace CopilotSessionSearch.Services;

public interface IAiSessionSearchCoordinator
{
    bool IsReady { get; }

    Task<HybridIndexMetrics> PrepareAsync(
        IProgress<HybridIndexProgress>? progress,
        CancellationToken cancellationToken);

    IAsyncEnumerable<SessionSearchUpdate> SearchAsync(
        string query,
        SessionSearchOptions options,
        CancellationToken cancellationToken);
}
