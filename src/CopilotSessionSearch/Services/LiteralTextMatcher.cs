#nullable enable

using CopilotSessionSearch.Models;

namespace CopilotSessionSearch.Services;

public static class LiteralTextMatcher
{
    public static IReadOnlyList<int> FindMatches(
        string content,
        string query,
        SessionSearchOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrEmpty(query);
        ArgumentNullException.ThrowIfNull(options);

        var matches = new List<int>();
        StringComparison comparison = options.IsCaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        int searchStart = 0;

        while (searchStart <= content.Length - query.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int matchStart = content.IndexOf(
                query,
                searchStart,
                comparison);

            if (matchStart < 0)
            {
                break;
            }

            if (!options.MatchWholeWord
                || HasWholeWordBoundaries(content, query, matchStart))
            {
                matches.Add(matchStart);
            }

            searchStart = matchStart + query.Length;
        }

        return matches;
    }

    private static bool HasWholeWordBoundaries(
        string content,
        string query,
        int matchStart)
    {
        bool requiresLeftBoundary = IsWordCharacter(query[0]);
        bool requiresRightBoundary = IsWordCharacter(query[^1]);
        int matchEnd = matchStart + query.Length;

        return (!requiresLeftBoundary
                || matchStart == 0
                || !IsWordCharacter(content[matchStart - 1]))
            && (!requiresRightBoundary
                || matchEnd == content.Length
                || !IsWordCharacter(content[matchEnd]));
    }

    private static bool IsWordCharacter(char value)
    {
        return char.IsLetterOrDigit(value) || value == '_';
    }
}
