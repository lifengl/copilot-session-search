#nullable enable

namespace CopilotSessionSearch.Models;

public sealed record SessionSearchUpdate(
    SessionSearchResult? Result,
    SessionSearchFailure? Failure,
    SessionSearchProgress Progress,
    string? StatusText = null,
    string? WarningMessage = null,
    bool IsProgressIndeterminate = false);
