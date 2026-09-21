#nullable enable

using CopilotSessionSearch.Models;

namespace AiSearchSpike;

internal sealed record RetrievalResult(
    IReadOnlyList<AiSearchCandidate> Candidates,
    int ScannedSessions,
    IReadOnlyList<SessionSearchFailure> Failures,
    int StrictCandidateCount,
    bool UsedRelaxedRequirements,
    int SerializedCharacterCount)
{
    public int FailedSessions => Failures.Count;
}

internal sealed class AiSearchSpikeReport
{
    public string Query { get; set; } = string.Empty;

    public AiSearchPlan Plan { get; set; } = new();

    public RetrievalResult Retrieval { get; set; } =
        new([], 0, [], 0, false, 0);

    public List<AiRankingItem> Rankings { get; set; } = [];

    public AiUsageSummary Usage { get; set; } = new(0, 0, 0, 0);
}
