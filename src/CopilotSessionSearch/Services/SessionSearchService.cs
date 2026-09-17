#nullable enable

using CopilotSessionSearch.Models;

namespace CopilotSessionSearch.Services;

public sealed class SessionSearchService
{
    private const int PreviewContextLength = 120;
    private const int DetailContextLength = 800;

    public SessionSearchResult? Search(
        SessionDocument document,
        string query,
        CancellationToken cancellationToken)
    {
        return Search(
            document,
            TextSearchPattern.Create(query, SessionSearchOptions.Default),
            cancellationToken);
    }

    public SessionSearchResult? Search(
        SessionDocument document,
        string query,
        SessionSearchOptions options,
        CancellationToken cancellationToken)
    {
        return Search(
            document,
            TextSearchPattern.Create(query, options),
            cancellationToken);
    }

    public SessionSearchResult? Search(
        SessionDocument document,
        TextSearchPattern pattern,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(pattern);

        var sections = new List<MatchSection>();
        int matchCount = 0;

        for (int entryIndex = 0; entryIndex < document.Entries.Count; entryIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ConversationEntry entry = document.Entries[entryIndex];
            IReadOnlyList<TextMatch> matches = pattern.FindMatches(
                entry.Content,
                cancellationToken);
            if (matches.Count == 0)
            {
                continue;
            }

            matchCount += matches.Count;
            foreach (MatchRange range in CreateMatchRanges(entry.Content.Length, matches))
            {
                cancellationToken.ThrowIfCancellationRequested();

                TextMatch firstMatch = range.Matches[0];
                int previewStart = Math.Max(0, firstMatch.Start - PreviewContextLength);
                int previewEnd = Math.Min(
                    entry.Content.Length,
                    firstMatch.End + PreviewContextLength);

                sections.Add(
                    new MatchSection(
                        entry.EventId,
                        entryIndex + 1,
                        entry.Speaker,
                        entry.Timestamp,
                        CreateExcerpt(entry.Content, previewStart, previewEnd),
                        CreateExcerpt(entry.Content, range.Start, range.End),
                        entry.Content,
                        range.Matches.Count,
                        range.Start > 0 || range.End < entry.Content.Length));
            }
        }

        return sections.Count == 0
            ? null
            : new SessionSearchResult(
                document,
                pattern.Query,
                pattern.Options,
                sections,
                matchCount);
    }

    private static IReadOnlyList<MatchRange> CreateMatchRanges(
        int contentLength,
        IReadOnlyList<TextMatch> matches)
    {
        var ranges = new List<MatchRange>();

        foreach (TextMatch match in matches)
        {
            int rangeStart = Math.Max(0, match.Start - DetailContextLength);
            int rangeEnd = Math.Min(contentLength, match.End + DetailContextLength);

            if (ranges.Count > 0 && rangeStart <= ranges[^1].End)
            {
                MatchRange previous = ranges[^1];
                previous.End = Math.Max(previous.End, rangeEnd);
                previous.Matches.Add(match);
            }
            else
            {
                ranges.Add(new MatchRange(rangeStart, rangeEnd, match));
            }
        }

        return ranges;
    }

    private static string CreateExcerpt(string content, int start, int end)
    {
        string excerpt = content[start..end].Trim();

        if (start > 0)
        {
            excerpt = "..." + excerpt;
        }

        if (end < content.Length)
        {
            excerpt += "...";
        }

        return excerpt;
    }

    private sealed class MatchRange
    {
        public MatchRange(int start, int end, TextMatch match)
        {
            Start = start;
            End = end;
            Matches = [match];
        }

        public int Start { get; }

        public int End { get; set; }

        public List<TextMatch> Matches { get; }
    }
}
