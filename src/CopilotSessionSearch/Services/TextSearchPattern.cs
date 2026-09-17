#nullable enable

using System.Text.RegularExpressions;
using CopilotSessionSearch.Models;

namespace CopilotSessionSearch.Services;

public sealed class TextSearchPattern
{
    private static readonly TimeSpan RegularExpressionTimeout =
        TimeSpan.FromMilliseconds(500);

    private readonly Regex? _regularExpression;

    private TextSearchPattern(
        string query,
        SessionSearchOptions options,
        Regex? regularExpression)
    {
        Query = query;
        Options = options;
        _regularExpression = regularExpression;
    }

    public string Query { get; }

    public SessionSearchOptions Options { get; }

    public static TextSearchPattern Create(
        string query,
        SessionSearchOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentNullException.ThrowIfNull(options);

        string normalizedQuery = query.Trim();
        Regex? regularExpression = options.UseRegularExpression
            ? CreateRegularExpression(normalizedQuery, options.IsCaseSensitive)
            : null;

        return new TextSearchPattern(
            normalizedQuery,
            options,
            regularExpression);
    }

    public IReadOnlyList<TextMatch> FindMatches(
        string content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        return _regularExpression is null
            ? FindLiteralMatches(content, cancellationToken)
            : FindRegularExpressionMatches(content, cancellationToken);
    }

    private static Regex CreateRegularExpression(
        string query,
        bool isCaseSensitive)
    {
        RegexOptions options = RegexOptions.Compiled | RegexOptions.CultureInvariant;
        if (!isCaseSensitive)
        {
            options |= RegexOptions.IgnoreCase;
        }

        try
        {
            return new Regex(
                query,
                options | RegexOptions.NonBacktracking,
                RegularExpressionTimeout);
        }
        catch (NotSupportedException)
        {
            return new Regex(
                query,
                options,
                RegularExpressionTimeout);
        }
    }

    private IReadOnlyList<TextMatch> FindLiteralMatches(
        string content,
        CancellationToken cancellationToken)
    {
        var matches = new List<TextMatch>();
        StringComparison comparison = Options.IsCaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        int searchStart = 0;

        while (searchStart <= content.Length - Query.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int matchStart = content.IndexOf(
                Query,
                searchStart,
                comparison);

            if (matchStart < 0)
            {
                break;
            }

            var match = new TextMatch(matchStart, Query.Length);
            if (!Options.MatchWholeWord
                || HasWholeWordBoundaries(content, match))
            {
                matches.Add(match);
            }

            searchStart = match.End;
        }

        return matches;
    }

    private IReadOnlyList<TextMatch> FindRegularExpressionMatches(
        string content,
        CancellationToken cancellationToken)
    {
        var matches = new List<TextMatch>();
        cancellationToken.ThrowIfCancellationRequested();

        Match match = _regularExpression!.Match(content);
        while (match.Success)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var textMatch = new TextMatch(match.Index, match.Length);
            if (textMatch.Length > 0
                && (!Options.MatchWholeWord
                    || HasWholeWordBoundaries(content, textMatch)))
            {
                matches.Add(textMatch);
            }

            match = match.NextMatch();
        }

        return matches;
    }

    private static bool HasWholeWordBoundaries(
        string content,
        TextMatch match)
    {
        bool requiresLeftBoundary = IsWordCharacter(content[match.Start]);
        bool requiresRightBoundary = IsWordCharacter(content[match.End - 1]);

        return (!requiresLeftBoundary
                || match.Start == 0
                || !IsWordCharacter(content[match.Start - 1]))
            && (!requiresRightBoundary
                || match.End == content.Length
                || !IsWordCharacter(content[match.End]));
    }

    private static bool IsWordCharacter(char value)
    {
        return char.IsLetterOrDigit(value) || value == '_';
    }
}
