#nullable enable

using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using CopilotSessionSearch.Models;

namespace CopilotSessionSearch.Services;

public static class LocalAiCandidateRetriever
{
    private static readonly Regex QuantitativeSignalPattern = new(
        @"\b\d+(?:\.\d+)?\s*(?:%|x|ms|msec|s|sec|secs|second|seconds|mb|gb|kb|bytes?)\b",
        RegexOptions.Compiled
            | RegexOptions.CultureInvariant
            | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(250));

    private const int MaximumCandidates = 24;
    private const int MaximumCandidatesPerSession = 6;
    private const int MaximumMatchedSnippetLength = 900;
    private const int MaximumAdjacentSnippetLength = 500;
    private const int MaximumSerializedCharacters = 50_000;

    public static async Task<RetrievalResult> RetrieveAsync(
        ISessionHistorySource historySource,
        SessionDocumentCache documentCache,
        IReadOnlyList<SessionDescriptor> sessions,
        AiSearchPlan plan,
        Action<int, int, int>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(historySource);
        ArgumentNullException.ThrowIfNull(documentCache);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(plan);

        var scoredBlocks = new ConcurrentBag<ScoredBlock>();
        var failures = new ConcurrentBag<SessionSearchFailure>();
        int scannedSessions = 0;
        var parallelOptions = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = 4,
        };

        await Parallel.ForEachAsync(
            sessions,
            parallelOptions,
            async (session, workerCancellationToken) =>
            {
                try
                {
                    SessionDocument document = await documentCache
                        .GetOrLoadAsync(
                            session,
                            historySource.GetSessionDocumentAsync,
                            workerCancellationToken)
                        .ConfigureAwait(false);
                    foreach (ScoredBlock block in ScoreDocument(document, plan))
                    {
                        scoredBlocks.Add(block);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failures.Add(
                        new SessionSearchFailure(
                            session,
                            ex.Message));
                }
                finally
                {
                    int completed = Interlocked.Increment(ref scannedSessions);
                    progress?.Invoke(
                        completed,
                        sessions.Count,
                        failures.Count);
                }
            }).ConfigureAwait(false);

        return CreateCandidates(
            scoredBlocks,
            scannedSessions,
            failures,
            plan);
    }

    public static RetrievalResult RetrieveSynthetic(
        IReadOnlyList<SessionDocument> documents,
        AiSearchPlan plan)
    {
        IReadOnlyList<ScoredBlock> scored = documents
            .SelectMany(document => ScoreDocument(document, plan))
            .ToArray();
        return CreateCandidates(
            scored,
            documents.Count,
            failures: [],
            plan);
    }

    private static RetrievalResult CreateCandidates(
        IEnumerable<ScoredBlock> scoredBlocks,
        int scannedSessions,
        IEnumerable<SessionSearchFailure> failures,
        AiSearchPlan plan)
    {
        IReadOnlyList<ScoredBlock> scored = scoredBlocks
            .GroupBy(
                block => (
                    block.Document.Session.SessionId,
                    block.StartIndex,
                    block.EndIndex))
            .Select(
                group => group
                    .OrderByDescending(block => block.Score)
                    .First())
            .GroupBy(
                block => block.Document.Session.SessionId,
                StringComparer.Ordinal)
            .SelectMany(
                group => group
                    .OrderByDescending(block => block.MeetsRequiredGroups)
                    .ThenByDescending(block => block.Score)
                    .Take(MaximumCandidatesPerSession))
            .OrderByDescending(block => block.MeetsRequiredGroups)
            .ThenByDescending(block => block.Score)
            .ThenByDescending(block => block.Document.Session.ModifiedTime)
            .ToArray();
        int strictCandidateCount = scored.Count(
            block => block.MeetsRequiredGroups);
        bool useRelaxedRequirements = strictCandidateCount < 3;
        IEnumerable<ScoredBlock> selectedBlocks = useRelaxedRequirements
            ? scored
            : scored.Where(block => block.MeetsRequiredGroups);

        var candidates = new List<AiSearchCandidate>();
        int serializedCharacterCount = 0;

        foreach (ScoredBlock scoredBlock in selectedBlocks)
        {
            string candidateId = $"C{candidates.Count + 1:D3}";
            var candidate = new AiSearchCandidate(
                candidateId,
                scoredBlock.Document.Session.SessionId,
                scoredBlock.Document.Session.Name,
                scoredBlock.Document.Session.ModifiedTime,
                scoredBlock.Score,
                scoredBlock.Signals,
                CreateEvidence(scoredBlock, plan))
            {
                Document = scoredBlock.Document,
            };
            int candidateLength = JsonSerializer.Serialize(
                candidate,
                AiSearchJson.SerializerOptions).Length;

            if (candidates.Count > 0
                && serializedCharacterCount + candidateLength
                    > MaximumSerializedCharacters)
            {
                break;
            }

            candidates.Add(candidate);
            serializedCharacterCount += candidateLength;
            if (candidates.Count == MaximumCandidates)
            {
                break;
            }
        }

        return new RetrievalResult(
            candidates,
            scannedSessions,
            failures.ToArray(),
            strictCandidateCount,
            useRelaxedRequirements,
            serializedCharacterCount);
    }

    private static IReadOnlyList<ScoredBlock> ScoreDocument(
        SessionDocument document,
        AiSearchPlan plan)
    {
        var anchorIndexes = new List<int>();
        for (int index = 0; index < document.Entries.Count; index++)
        {
            if (plan.AllTerms.Any(
                term => Contains(
                    document.Entries[index].Content,
                    term)))
            {
                anchorIndexes.Add(index);
            }
        }

        var blocks = new List<ScoredBlock>();
        foreach (int anchorIndex in anchorIndexes)
        {
            int startIndex = Math.Max(0, anchorIndex - 1);
            int endIndex = Math.Min(
                document.Entries.Count - 1,
                anchorIndex + 1);
            ScoredBlock? block = ScoreBlock(
                document,
                startIndex,
                endIndex,
                anchorIndex,
                plan);
            if (block is not null)
            {
                blocks.Add(block);
            }
        }

        return blocks;
    }

    private static ScoredBlock? ScoreBlock(
        SessionDocument document,
        int startIndex,
        int endIndex,
        int anchorIndex,
        AiSearchPlan plan)
    {
        string combinedText = string.Join(
            "\n",
            document.Entries
                .Skip(startIndex)
                .Take(endIndex - startIndex + 1)
                .Select(entry => entry.Content));
        bool hasAnyMatch = false;
        int requiredGroupsMatched = 0;
        int preferredGroupsMatched = 0;
        int exactPhrasesMatched = 0;
        double score = 0;

        foreach (AiTermGroup group in plan.RequiredGroups)
        {
            bool matched = group.AnyOf.Any(
                term => Contains(combinedText, term));
            hasAnyMatch |= matched;
            if (matched)
            {
                requiredGroupsMatched++;
                score += 30;
            }
        }

        foreach (AiTermGroup group in plan.PreferredGroups)
        {
            bool matched = group.AnyOf.Any(
                term => Contains(combinedText, term));
            hasAnyMatch |= matched;
            if (matched)
            {
                preferredGroupsMatched++;
                score += 12;
            }
        }

        foreach (string phrase in plan.ExactPhrases)
        {
            if (Contains(combinedText, phrase))
            {
                hasAnyMatch = true;
                exactPhrasesMatched++;
                score += 18;
            }
        }

        if (!hasAnyMatch)
        {
            return null;
        }

        int distinctTermsMatched = plan.AllTerms.Count(
            term => Contains(combinedText, term));
        score += Math.Min(distinctTermsMatched, 12) * 3;
        int quantitativeSignals = QuantitativeSignalPattern
            .Matches(combinedText)
            .Count;
        int markdownTableRows = combinedText
            .Split('\n')
            .Count(line => line.Count(character => character == '|') >= 2);
        if (PrefersMeasuredEvidence(plan))
        {
            score += Math.Min(quantitativeSignals, 10) * 3;
            score += Math.Min(markdownTableRows, 8) * 2;
        }

        var signals = new AiLocalMatchSignals(
            requiredGroupsMatched,
            plan.RequiredGroups.Count,
            preferredGroupsMatched,
            plan.PreferredGroups.Count,
            exactPhrasesMatched,
            distinctTermsMatched,
            quantitativeSignals,
            markdownTableRows);

        return new ScoredBlock(
            document,
            startIndex,
            endIndex,
            anchorIndex,
            score,
            requiredGroupsMatched == plan.RequiredGroups.Count,
            signals);
    }

    private static IReadOnlyList<CandidateEvidence> CreateEvidence(
        ScoredBlock block,
        AiSearchPlan plan)
    {
        var evidence = new List<CandidateEvidence>();
        for (int index = block.StartIndex; index <= block.EndIndex; index++)
        {
            ConversationEntry entry = block.Document.Entries[index];
            bool isAnchor = index == block.AnchorIndex;
            evidence.Add(
                new CandidateEvidence(
                    index + 1,
                    entry.Speaker == ConversationSpeaker.User
                        ? "You"
                        : "Copilot",
                    CreateSnippet(
                        entry.Content,
                        plan.AllTerms,
                        isAnchor
                            ? MaximumMatchedSnippetLength
                            : MaximumAdjacentSnippetLength)));
        }

        return evidence;
    }

    private static string CreateSnippet(
        string content,
        IReadOnlyList<string> terms,
        int maximumLength)
    {
        if (content.Length <= maximumLength)
        {
            return content;
        }

        int firstMatch = terms
            .Select(
                term => content.IndexOf(
                    term,
                    StringComparison.OrdinalIgnoreCase))
            .Where(index => index >= 0)
            .DefaultIfEmpty(0)
            .Min();
        int start = Math.Max(0, firstMatch - maximumLength / 3);
        int end = Math.Min(content.Length, start + maximumLength);
        start = Math.Max(0, end - maximumLength);

        return (start > 0 ? "..." : string.Empty)
            + content[start..end]
            + (end < content.Length ? "..." : string.Empty);
    }

    private static bool Contains(string content, string term)
    {
        return content.Contains(
            term,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool PrefersMeasuredEvidence(AiSearchPlan plan)
    {
        string[] evidenceTerms =
        [
            "benchmark",
            "compare",
            "comparison",
            "duration",
            "elapsed",
            "faster",
            "latency",
            "measurement",
            "performance",
            "regression",
            "slower",
            "speed",
            "throughput",
            "timing",
        ];
        return plan.PreferredGroups
            .Concat(plan.RequiredGroups)
            .Any(
                group =>
                    evidenceTerms.Any(
                        term => group.Name.Contains(
                            term,
                            StringComparison.OrdinalIgnoreCase))
                    || group.AnyOf.Any(
                        value => evidenceTerms.Any(
                            term => value.Contains(
                                term,
                                StringComparison.OrdinalIgnoreCase))));
    }

    private sealed record ScoredBlock(
        SessionDocument Document,
        int StartIndex,
        int EndIndex,
        int AnchorIndex,
        double Score,
        bool MeetsRequiredGroups,
        AiLocalMatchSignals Signals);
}
