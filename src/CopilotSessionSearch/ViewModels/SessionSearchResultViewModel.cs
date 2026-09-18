#nullable enable

using CopilotSessionSearch.Models;

namespace CopilotSessionSearch.ViewModels;

public sealed class SessionSearchResultViewModel
{
    public SessionSearchResultViewModel(SessionSearchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        Result = result;
        Samples = result.Samples
            .Select(
                section => new MatchSectionViewModel(
                    section,
                    result.Query,
                    result.Options))
            .ToArray();
    }

    public SessionSearchResult Result { get; }

    public string Name => Result.Session.Name;

    public string LastActiveText =>
        $"Last active {DisplayTextFormatter.FormatDateTime(Result.Session.ModifiedTime)}";

    public string WorkingDirectory => string.IsNullOrWhiteSpace(Result.Session.WorkingDirectory)
        ? "(working directory unavailable)"
        : Result.Session.WorkingDirectory;

    public string MatchCountText => Result.AiRelevance is AiRelevanceInfo relevance
        ? $"AI relevance {relevance.Score} ({relevance.Confidence})"
        : DisplayTextFormatter.FormatCount(
            Result.MatchCount,
            "match",
            "matches");

    public string? AiReason => Result.AiRelevance?.Reason;

    public bool HasAiReason => AiReason is not null;

    public DateTimeOffset ModifiedTime => Result.Session.ModifiedTime;

    public IReadOnlyList<MatchSectionViewModel> Samples { get; }
}
