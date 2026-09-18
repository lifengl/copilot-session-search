#nullable enable

using System.Text.Json;
using CopilotSessionSearch.Models;
using CopilotSessionSearch.Services;

namespace AiSearchSpike;

internal static class AiSearchSelfTest
{
    public static void Run()
    {
        AiSearchPlan plan = AiSearchJson.ParsePlan(
            """
            {
              "requiredGroups": [
                { "name": "subject", "anyOf": ["ClrMD"] }
              ],
              "preferredGroups": [
                { "name": "new version", "anyOf": ["4.1"] },
                { "name": "old version", "anyOf": ["3.0", "3.x"] },
                { "name": "performance", "anyOf": ["performance", "benchmark", "faster"] }
              ],
              "exactPhrases": []
            }
            """);
        IReadOnlyList<SessionDocument> documents =
        [
            CreateDocument(
                "relevant",
                "We benchmarked ClrMD 4.1 against 3.0.",
                "ClrMD 4.1 was faster in the measured heap walk."),
            CreateDocument(
                "api-only",
                "ClrMD 4.1 changed an API shape.",
                "No benchmark was collected."),
            CreateDocument(
                "unrelated",
                "CPS non-fatal error investigation.",
                "The project system fault rate improved."),
        ];

        RetrievalResult retrieval =
            LocalAiCandidateRetriever.RetrieveSynthetic(documents, plan);
        AiSearchCandidate best = retrieval.Candidates.FirstOrDefault()
            ?? throw new InvalidOperationException(
                "Synthetic retrieval returned no candidates.");
        if (!string.Equals(
            best.SessionId,
            "relevant",
            StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Synthetic retrieval ranked {best.SessionId} first.");
        }

        List<AiRankingItem> rankings = AiSearchJson.ParseRankings(
            JsonSerializer.Serialize(
                new AiRankingResponse
                {
                    Results =
                    [
                        new AiRankingItem
                        {
                            CandidateId = best.CandidateId,
                            Confidence = "high",
                            MessageNumbers = [1, 2, 99],
                            Reason = "Contains both versions and measured performance.",
                            Score = 98,
                        },
                    ],
                },
                AiSearchJson.SerializerOptions),
            retrieval.Candidates);
        AiRankingItem ranking = rankings.Single();
        if (!ranking.MessageNumbers.SequenceEqual([1, 2]))
        {
            throw new InvalidOperationException(
                "Ranking validation did not remove an unknown message number.");
        }
    }

    private static SessionDocument CreateDocument(
        string sessionId,
        params string[] messages)
    {
        var descriptor = new SessionDescriptor(
            sessionId,
            $"Synthetic {sessionId}",
            DateTimeOffset.Parse("2026-09-01T10:00:00Z"),
            DateTimeOffset.Parse("2026-09-01T11:00:00Z"),
            null,
            null,
            null);
        ConversationEntry[] entries = messages
            .Select(
                (message, index) => new ConversationEntry(
                    $"{sessionId}-{index}",
                    index % 2 == 0
                        ? ConversationSpeaker.User
                        : ConversationSpeaker.Copilot,
                    descriptor.StartTime.AddMinutes(index),
                    message))
            .ToArray();
        return new SessionDocument(descriptor, entries);
    }
}
