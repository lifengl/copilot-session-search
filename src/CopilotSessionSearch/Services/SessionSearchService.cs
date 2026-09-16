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
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        string normalizedQuery = query.Trim();
        var sections = new List<MatchSection>();
        int matchCount = 0;

        for (int entryIndex = 0; entryIndex < document.Entries.Count; entryIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ConversationEntry entry = document.Entries[entryIndex];
            IReadOnlyList<int> matches = FindMatches(entry.Content, normalizedQuery, cancellationToken);
            if (matches.Count == 0)
            {
                continue;
            }

            matchCount += matches.Count;
            foreach (MatchRange range in CreateMatchRanges(entry.Content.Length, normalizedQuery.Length, matches))
            {
                cancellationToken.ThrowIfCancellationRequested();

                int previewStart = Math.Max(0, range.MatchStarts[0] - PreviewContextLength);
                int previewEnd = Math.Min(
                    entry.Content.Length,
                    range.MatchStarts[0] + normalizedQuery.Length + PreviewContextLength);

                sections.Add(
                    new MatchSection(
                        entry.EventId,
                        entryIndex + 1,
                        entry.Speaker,
                        entry.Timestamp,
                        CreateExcerpt(entry.Content, previewStart, previewEnd),
                        CreateExcerpt(entry.Content, range.Start, range.End),
                        entry.Content,
                        range.MatchStarts.Count,
                        range.Start > 0 || range.End < entry.Content.Length));
            }
        }

        return sections.Count == 0
            ? null
            : new SessionSearchResult(document, normalizedQuery, sections, matchCount);
    }

    private static IReadOnlyList<int> FindMatches(
        string content,
        string query,
        CancellationToken cancellationToken)
    {
        var matches = new List<int>();
        int searchStart = 0;

        while (searchStart <= content.Length - query.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int matchStart = content.IndexOf(
                query,
                searchStart,
                StringComparison.OrdinalIgnoreCase);

            if (matchStart < 0)
            {
                break;
            }

            matches.Add(matchStart);
            searchStart = matchStart + query.Length;
        }

        return matches;
    }

    private static IReadOnlyList<MatchRange> CreateMatchRanges(
        int contentLength,
        int queryLength,
        IReadOnlyList<int> matchStarts)
    {
        var ranges = new List<MatchRange>();

        foreach (int matchStart in matchStarts)
        {
            int rangeStart = Math.Max(0, matchStart - DetailContextLength);
            int rangeEnd = Math.Min(contentLength, matchStart + queryLength + DetailContextLength);

            if (ranges.Count > 0 && rangeStart <= ranges[^1].End)
            {
                MatchRange previous = ranges[^1];
                previous.End = Math.Max(previous.End, rangeEnd);
                previous.MatchStarts.Add(matchStart);
            }
            else
            {
                ranges.Add(new MatchRange(rangeStart, rangeEnd, matchStart));
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
        public MatchRange(int start, int end, int matchStart)
        {
            Start = start;
            End = end;
            MatchStarts = [matchStart];
        }

        public int Start { get; }

        public int End { get; set; }

        public List<int> MatchStarts { get; }
    }
}
