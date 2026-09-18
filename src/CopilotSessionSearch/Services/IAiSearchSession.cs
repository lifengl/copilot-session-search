#nullable enable

using CopilotSessionSearch.Models;

namespace CopilotSessionSearch.Services;

public interface IAiSearchSession : IAsyncDisposable
{
    Task<AiSearchPlan> CreatePlanAsync(
        string query,
        CancellationToken cancellationToken);

    Task<List<AiRankingItem>> RankAsync(
        string query,
        IReadOnlyList<AiSearchCandidate> candidates,
        CancellationToken cancellationToken);

    AiUsageSummary GetUsage();
}

public interface IAiSearchSessionFactory
{
    Task<IAiSearchSession> CreateAsync(
        CancellationToken cancellationToken);
}
