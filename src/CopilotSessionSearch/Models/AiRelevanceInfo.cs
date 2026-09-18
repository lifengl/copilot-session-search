#nullable enable

namespace CopilotSessionSearch.Models;

public sealed record AiRelevanceInfo(
    int Score,
    string Confidence,
    string Reason);
