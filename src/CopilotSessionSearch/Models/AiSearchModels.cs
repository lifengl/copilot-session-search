#nullable enable

using System.Text.Json.Serialization;

namespace CopilotSessionSearch.Models;

public sealed class AiTermGroup
{
    public string Name { get; set; } = string.Empty;

    public List<string> AnyOf { get; set; } = [];
}

public sealed class AiSearchPlan
{
    public List<AiTermGroup> RequiredGroups { get; set; } = [];

    public List<AiTermGroup> PreferredGroups { get; set; } = [];

    public List<string> ExactPhrases { get; set; } = [];

    public IReadOnlyList<string> AllTerms =>
        RequiredGroups
            .Concat(PreferredGroups)
            .SelectMany(group => group.AnyOf)
            .Concat(ExactPhrases)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}

public sealed class AiRankingResponse
{
    public List<AiRankingItem> Results { get; set; } = [];
}

public sealed class AiRankingItem
{
    public string CandidateId { get; set; } = string.Empty;

    public int Score { get; set; }

    public string Confidence { get; set; } = string.Empty;

    public List<int> MessageNumbers { get; set; } = [];

    public string Reason { get; set; } = string.Empty;
}

public sealed record CandidateEvidence(
    int MessageNumber,
    string Speaker,
    string Text);

public sealed record AiLocalMatchSignals(
    int RequiredGroupsMatched,
    int RequiredGroupsTotal,
    int PreferredGroupsMatched,
    int PreferredGroupsTotal,
    int ExactPhrasesMatched,
    int DistinctTermsMatched,
    int QuantitativeSignals,
    int MarkdownTableRows);

public sealed record AiSearchCandidate(
    string CandidateId,
    string SessionId,
    string SessionName,
    DateTimeOffset ModifiedTime,
    double LocalScore,
    AiLocalMatchSignals Signals,
    IReadOnlyList<CandidateEvidence> Evidence)
{
    [JsonIgnore]
    public SessionDocument? Document { get; init; }
}

public sealed record RetrievalResult(
    IReadOnlyList<AiSearchCandidate> Candidates,
    int ScannedSessions,
    IReadOnlyList<SessionSearchFailure> Failures,
    int StrictCandidateCount,
    bool UsedRelaxedRequirements,
    int SerializedCharacterCount)
{
    public int FailedSessions => Failures.Count;
}

public sealed record AiUsageSummary(
    int ApiCalls,
    long InputTokens,
    long OutputTokens,
    double AiCredits);

public sealed class AiSearchSpikeReport
{
    public string Query { get; set; } = string.Empty;

    public AiSearchPlan Plan { get; set; } = new();

    public RetrievalResult Retrieval { get; set; } =
        new([], 0, [], 0, false, 0);

    public List<AiRankingItem> Rankings { get; set; } = [];

    public AiUsageSummary Usage { get; set; } = new(0, 0, 0, 0);
}
