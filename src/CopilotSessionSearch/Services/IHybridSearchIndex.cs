#nullable enable

using CopilotSessionSearch.Models;

namespace CopilotSessionSearch.Services;

public interface IHybridSearchIndex : IDisposable
{
    bool IsReady { get; }

    Task<HybridIndexMetrics> PrepareAsync(
        ISessionHistorySource historySource,
        SessionDocumentCache documentCache,
        IProgress<HybridIndexProgress>? progress,
        CancellationToken cancellationToken);

    HybridQueryResult Search(
        string query,
        int maximumResults = 30,
        CancellationToken cancellationToken = default);
}
