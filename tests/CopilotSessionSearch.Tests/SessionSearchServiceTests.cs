#nullable enable

using CopilotSessionSearch.Models;
using CopilotSessionSearch.Services;

namespace CopilotSessionSearch.Tests;

public sealed class SessionSearchServiceTests
{
    private readonly SessionSearchService _searchService = new();

    [Fact]
    public void SearchFindsUserAndCopilotMessagesCaseInsensitively()
    {
        SessionDocument document = CreateDocument(
            new ConversationEntry("user", ConversationSpeaker.User, DateTimeOffset.Parse("2026-09-01"), "Investigate PERFORMANCE regressions."),
            new ConversationEntry("assistant", ConversationSpeaker.Copilot, DateTimeOffset.Parse("2026-09-01"), "The performance regression came from project evaluation."));

        SessionSearchResult? result = _searchService.Search(document, "performance", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(2, result.MatchCount);
        Assert.Equal(2, result.Sections.Count);
        Assert.Contains(result.Sections, section => section.Speaker == ConversationSpeaker.User);
        Assert.Contains(result.Sections, section => section.Speaker == ConversationSpeaker.Copilot);
    }

    [Fact]
    public void SearchPropagatesOptionsIntoResults()
    {
        SessionDocument document = CreateDocument(
            new ConversationEntry(
                "user",
                ConversationSpeaker.User,
                DateTimeOffset.Parse("2026-09-01"),
                "Needle needle needles."));
        var options = new SessionSearchOptions(
            MatchWholeWord: true,
            IsCaseSensitive: true);

        SessionSearchResult? result = _searchService.Search(
            document,
            "needle",
            options,
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(options, result.Options);
        Assert.Equal(1, result.MatchCount);
    }

    [Fact]
    public void SearchLimitsMainSamplesButKeepsEveryDetailSection()
    {
        SessionDocument document = CreateDocument(
            Enumerable.Range(1, 5)
                .Select(index => new ConversationEntry(
                    $"event-{index}",
                    ConversationSpeaker.Copilot,
                    DateTimeOffset.Parse("2026-09-01").AddMinutes(index),
                    $"Matching text number {index}."))
                .ToArray());

        SessionSearchResult? result = _searchService.Search(document, "matching", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(5, result.Sections.Count);
        Assert.Equal(3, result.Samples.Count);
    }

    [Fact]
    public void SearchSeparatesDistantMatchesAndMergesNearbyMatches()
    {
        string content =
            "needle " +
            new string('a', 100) +
            " needle " +
            new string('b', 2000) +
            " needle";

        SessionDocument document = CreateDocument(
            new ConversationEntry("event", ConversationSpeaker.User, DateTimeOffset.Parse("2026-09-01"), content));

        SessionSearchResult? result = _searchService.Search(document, "needle", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(3, result.MatchCount);
        Assert.Equal(2, result.Sections.Count);
        Assert.Equal(2, result.Sections[0].OccurrenceCount);
        Assert.Equal(1, result.Sections[1].OccurrenceCount);
    }

    [Fact]
    public void DetailTextContainsMoreContextThanPreviewText()
    {
        string content = new string('a', 500) + "needle" + new string('b', 500);
        SessionDocument document = CreateDocument(
            new ConversationEntry("event", ConversationSpeaker.User, DateTimeOffset.Parse("2026-09-01"), content));

        SessionSearchResult? result = _searchService.Search(document, "needle", CancellationToken.None);

        MatchSection section = Assert.Single(Assert.IsType<SessionSearchResult>(result).Sections);
        Assert.True(section.DetailText.Length > section.PreviewText.Length);
        Assert.Equal(content, section.FullText);
    }

    [Fact]
    public void SearchReturnsNullWhenNothingMatches()
    {
        SessionDocument document = CreateDocument(
            new ConversationEntry("event", ConversationSpeaker.User, DateTimeOffset.Parse("2026-09-01"), "No relevant content."));

        SessionSearchResult? result = _searchService.Search(document, "missing", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public void SearchHonorsCancellation()
    {
        SessionDocument document = CreateDocument(
            new ConversationEntry("event", ConversationSpeaker.User, DateTimeOffset.Parse("2026-09-01"), "Some searchable content."));
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => _searchService.Search(document, "searchable", cancellationSource.Token));
    }

    private static SessionDocument CreateDocument(params ConversationEntry[] entries)
    {
        var session = new SessionDescriptor(
            "session-id",
            "Session name",
            DateTimeOffset.Parse("2026-09-01T10:00:00Z"),
            DateTimeOffset.Parse("2026-09-01T11:00:00Z"),
            @"Q:\ws\project",
            "owner/repository",
            "main");

        return new SessionDocument(session, entries);
    }
}
