#nullable enable

namespace CopilotSessionSearch.Models;

public sealed record SessionSearchUpdate(
    SessionSearchResult? Result,
    SessionSearchFailure? Failure,
    SessionSearchProgress Progress);
