#nullable enable

namespace CopilotSessionSearch.Models;

public sealed record SessionSearchProgress(
    int CompletedSessions,
    int TotalSessions,
    int MatchingSessions,
    int FailedSessions);
