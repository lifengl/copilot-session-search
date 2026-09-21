#nullable enable

using CopilotSessionSearch.Models;
using CopilotSessionSearch.Services;

namespace CopilotSessionSearch.Tests;

public sealed class AiSearchJsonTests
{
    [Fact]
    public void EmptyRankingIsAValidNoResultsResponse()
    {
        var candidate = new AiSearchCandidate(
            "H001",
            "session-id",
            "Session",
            DateTimeOffset.Parse(
                "2026-01-01T10:00:00Z"),
            LocalScore: 0.8,
            new AiLocalMatchSignals(
                RequiredGroupsMatched: 0,
                RequiredGroupsTotal: 0,
                PreferredGroupsMatched: 0,
                PreferredGroupsTotal: 0,
                ExactPhrasesMatched: 0,
                DistinctTermsMatched: 0,
                QuantitativeSignals: 0,
                MarkdownTableRows: 0),
            [
                new CandidateEvidence(
                    MessageNumber: 1,
                    Speaker: "Copilot",
                    Text: "Candidate evidence."),
            ]);

        List<AiRankingItem> rankings =
            AiSearchJson.ParseRankings(
                """{"results":[]}""",
                [candidate]);

        Assert.Empty(rankings);
    }

    [Fact]
    public void MissingResultsArrayIsRejected()
    {
        var candidate = new AiSearchCandidate(
            "H001",
            "session-id",
            "Session",
            DateTimeOffset.Parse(
                "2026-01-01T10:00:00Z"),
            LocalScore: 0.8,
            new AiLocalMatchSignals(
                RequiredGroupsMatched: 0,
                RequiredGroupsTotal: 0,
                PreferredGroupsMatched: 0,
                PreferredGroupsTotal: 0,
                ExactPhrasesMatched: 0,
                DistinctTermsMatched: 0,
                QuantitativeSignals: 0,
                MarkdownTableRows: 0),
            [
                new CandidateEvidence(
                    MessageNumber: 1,
                    Speaker: "Copilot",
                    Text: "Candidate evidence."),
            ]);

        Assert.Throws<InvalidDataException>(
            () => AiSearchJson.ParseRankings(
                "{}",
                [candidate]));
    }
}
