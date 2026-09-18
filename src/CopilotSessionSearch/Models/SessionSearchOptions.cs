#nullable enable

namespace CopilotSessionSearch.Models;

public sealed record SessionSearchOptions(
    bool MatchWholeWord,
    bool IsCaseSensitive,
    bool UseRegularExpression,
    bool UseAiSearch = false)
{
    public static SessionSearchOptions Default { get; } = new(
        MatchWholeWord: false,
        IsCaseSensitive: false,
        UseRegularExpression: false,
        UseAiSearch: false);
}
