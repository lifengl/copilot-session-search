#nullable enable

using CopilotSessionSearch.Models;

namespace HybridSearchSpike;

internal sealed record QueryMetrics(
    HybridQueryResult Local,
    IReadOnlyList<CopilotRankedBlock> CopilotResults,
    AiUsageSummary? CopilotUsage);

internal sealed record CopilotRankedBlock(
    HybridRankedBlock LocalResult,
    int Score,
    string Confidence,
    string Reason);

internal sealed record HybridSearchReport(
    HybridIndexMetrics Index,
    IReadOnlyList<QueryMetrics> Queries);
