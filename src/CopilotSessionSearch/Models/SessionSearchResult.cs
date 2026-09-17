#nullable enable

namespace CopilotSessionSearch.Models;

public sealed class SessionSearchResult
{
    private const int MaximumSamples = 3;

    public SessionSearchResult(
        SessionDocument document,
        string query,
        IEnumerable<MatchSection> sections,
        int matchCount)
        : this(
            document,
            query,
            SessionSearchOptions.Default,
            sections,
            matchCount)
    {
    }

    public SessionSearchResult(
        SessionDocument document,
        string query,
        SessionSearchOptions options,
        IEnumerable<MatchSection> sections,
        int matchCount)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(sections);

        Document = document;
        Query = query;
        Options = options;
        Sections = sections.ToArray();
        Samples = Sections.Take(MaximumSamples).ToArray();
        MatchCount = matchCount;
    }

    public SessionDocument Document { get; }

    public SessionDescriptor Session => Document.Session;

    public string Query { get; }

    public SessionSearchOptions Options { get; }

    public IReadOnlyList<MatchSection> Sections { get; }

    public IReadOnlyList<MatchSection> Samples { get; }

    public int MatchCount { get; }

    public int MessageCount => Document.MessageCount;

    public TimeSpan SessionSpan => Document.SessionSpan;
}
