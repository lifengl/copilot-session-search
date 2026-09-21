#nullable enable

using System.IO;
using System.Runtime.CompilerServices;
using CopilotSessionSearch.Models;

namespace CopilotSessionSearch.Services;

public sealed class AiSessionSearchCoordinator : IAiSessionSearchCoordinator
{
    private const int MaximumRerankCandidates = 24;

    private readonly ISessionHistorySource _historySource;
    private readonly SessionDocumentCache _documentCache;
    private readonly IHybridSearchIndex _hybridIndex;
    private readonly IAiSearchSessionFactory _aiSessionFactory;

    public AiSessionSearchCoordinator(
        ISessionHistorySource historySource,
        SessionDocumentCache documentCache,
        IHybridSearchIndex hybridIndex,
        IAiSearchSessionFactory aiSessionFactory)
    {
        ArgumentNullException.ThrowIfNull(historySource);
        ArgumentNullException.ThrowIfNull(documentCache);
        ArgumentNullException.ThrowIfNull(hybridIndex);
        ArgumentNullException.ThrowIfNull(aiSessionFactory);

        _historySource = historySource;
        _documentCache = documentCache;
        _hybridIndex = hybridIndex;
        _aiSessionFactory = aiSessionFactory;
    }

    public bool IsReady => _hybridIndex.IsReady;

    public Task<HybridIndexMetrics> PrepareAsync(
        IProgress<HybridIndexProgress>? progress,
        CancellationToken cancellationToken)
    {
        return _hybridIndex.PrepareAsync(
            _historySource,
            _documentCache,
            progress,
            cancellationToken);
    }

    public async IAsyncEnumerable<SessionSearchUpdate> SearchAsync(
        string query,
        SessionSearchOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentNullException.ThrowIfNull(options);

        IReadOnlyList<SessionDescriptor> availableSessions = await _historySource
            .GetSessionsAsync(cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<SessionDescriptor> sessions =
            AiSearchPolicy.FilterEligibleSessions(
                availableSessions,
                DateTimeOffset.UtcNow);
        var progress = new SessionSearchProgress(
            CompletedSessions: 0,
            TotalSessions: sessions.Count,
            MatchingSessions: 0,
            FailedSessions: 0);

        yield return new SessionSearchUpdate(
            null,
            null,
            progress,
            "Waiting for the local hybrid AI index...");
        HybridIndexMetrics indexMetrics = await PrepareAsync(
            progress: null,
            cancellationToken).ConfigureAwait(false);
        progress = progress with
        {
            CompletedSessions = sessions.Count,
            FailedSessions = indexMetrics.Failures.Count,
        };
        var failedSessionIds = indexMetrics.Failures
            .Select(failure => failure.Session.SessionId)
            .ToHashSet(StringComparer.Ordinal);

        foreach (SessionSearchFailure failure in indexMetrics.Failures)
        {
            yield return new SessionSearchUpdate(
                null,
                failure,
                progress,
                $"Local AI index completed with {indexMetrics.Failures.Count:N0} read failure(s).");
        }

        yield return new SessionSearchUpdate(
            null,
            null,
            progress,
            "Searching the local FTS and semantic indexes...",
            IsProgressIndeterminate: true);
        HybridQueryResult localResult = await Task.Run(
            () => _hybridIndex.Search(
                query,
                maximumResults: MaximumRerankCandidates,
                cancellationToken),
            cancellationToken).ConfigureAwait(false);
        if (localResult.HybridResults.Count == 0)
        {
            yield return new SessionSearchUpdate(
                null,
                null,
                progress,
                "AI search complete. No local candidates were found.");
            yield break;
        }

        Dictionary<string, SessionDescriptor> sessionsById = sessions
            .ToDictionary(
                session => session.SessionId,
                StringComparer.Ordinal);
        Dictionary<string, SessionDocument> documentsBySessionId = [];

        foreach (string sessionId in localResult.HybridResults
            .Select(result => result.Block.SessionId)
            .Distinct(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!sessionsById.TryGetValue(
                sessionId,
                out SessionDescriptor? session))
            {
                continue;
            }

            SessionSearchFailure? readFailure = null;
            try
            {
                documentsBySessionId[sessionId] =
                    await _documentCache
                        .GetOrLoadAsync(
                            session,
                            _historySource.GetSessionDocumentAsync,
                            cancellationToken)
                        .ConfigureAwait(false);
            }
            catch (Exception ex) when (
                ex is not OperationCanceledException
                || !cancellationToken.IsCancellationRequested)
            {
                readFailure = new SessionSearchFailure(
                    session,
                    ex.Message);
            }

            if (readFailure is not null)
            {
                if (failedSessionIds.Add(
                    readFailure.Session.SessionId))
                {
                    progress = progress with
                    {
                        FailedSessions =
                            progress.FailedSessions + 1,
                    };
                    yield return new SessionSearchUpdate(
                        null,
                        readFailure,
                        progress,
                        $"Skipping unreadable AI candidate session {session.Name}.");
                }
            }
        }

        AiSearchCandidate[] candidates = localResult.HybridResults
            .Where(
                result => documentsBySessionId.ContainsKey(
                    result.Block.SessionId))
            .Select(
                (result, index) => CreateCandidate(
                    result,
                    documentsBySessionId[result.Block.SessionId],
                    index))
            .ToArray();
        if (candidates.Length == 0)
        {
            yield return new SessionSearchUpdate(
                null,
                null,
                progress,
                "AI search complete. No readable local candidates were found.");
            yield break;
        }

        yield return new SessionSearchUpdate(
            null,
            null,
            progress,
            $"Asking Copilot to rank {candidates.Length:N0} hybrid message blocks...",
            IsProgressIndeterminate: true);

        List<AiRankingItem> rankings;
        string? rerankingWarning = null;
        var usage = new AiUsageSummary(
            ApiCalls: 0,
            InputTokens: 0,
            OutputTokens: 0,
            AiCredits: 0);
        IAiSearchSession? aiSession = null;
        try
        {
            aiSession = await _aiSessionFactory
                .CreateAsync(cancellationToken)
                .ConfigureAwait(false);
            rankings = await aiSession.RankAsync(
                query,
                candidates,
                cancellationToken).ConfigureAwait(false);
            usage = aiSession.GetUsage();
        }
        catch (Exception ex) when (
            ex is not OperationCanceledException
            || !cancellationToken.IsCancellationRequested)
        {
            rerankingWarning =
                "Copilot reranking failed; showing local hybrid ranking: " +
                ex.Message;
            rankings = CreateLocalFallbackRankings(candidates);
        }
        finally
        {
            if (aiSession is not null)
            {
                try
                {
                    await aiSession
                        .DisposeAsync()
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    rerankingWarning = AppendWarning(
                        rerankingWarning,
                        "Copilot session cleanup failed: " +
                        ex.Message);
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (rankings.Count == 0)
        {
            yield return new SessionSearchUpdate(
                null,
                null,
                progress,
                $"AI search complete. Copilot found no relevant sessions. " +
                    $"{usage.ApiCalls:N0} model call(s), " +
                    $"{usage.AiCredits:N2} AI credits.",
                rerankingWarning);
            yield break;
        }

        Dictionary<string, AiSearchCandidate> candidatesById = candidates
            .ToDictionary(
                candidate => candidate.CandidateId,
                StringComparer.Ordinal);
        IReadOnlyList<IGrouping<string, RankedCandidate>> rankedSessions = rankings
            .Where(ranking => candidatesById.ContainsKey(ranking.CandidateId))
            .Select(
                ranking => new RankedCandidate(
                    ranking,
                    candidatesById[ranking.CandidateId]))
            .GroupBy(
                item => item.Candidate.SessionId,
                StringComparer.Ordinal)
            .OrderByDescending(group => group.Max(item => item.Ranking.Score))
            .ThenByDescending(group => group.First().Candidate.ModifiedTime)
            .ToArray();
        int matchingSessions = 0;

        foreach (IGrouping<string, RankedCandidate> rankedSession in rankedSessions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            SessionSearchResult result = CreateResult(
                query,
                options,
                rankedSession);
            matchingSessions++;
            progress = progress with
            {
                MatchingSessions = matchingSessions,
            };
            yield return new SessionSearchUpdate(
                result,
                null,
                progress,
                $"Adding AI-ranked session {matchingSessions:N0} of {rankedSessions.Count:N0}...",
                WarningMessage: rerankingWarning,
                IsProgressIndeterminate: true);
        }

        yield return new SessionSearchUpdate(
            null,
            null,
            progress,
            rerankingWarning is null
                ? $"AI search complete. {matchingSessions:N0} ranked session(s), " +
                    $"{usage.ApiCalls:N0} model call(s), {usage.AiCredits:N2} AI credits."
                : $"Local hybrid search complete. {matchingSessions:N0} ranked session(s).",
            rerankingWarning);
    }

    private static AiSearchCandidate CreateCandidate(
        HybridRankedBlock result,
        SessionDocument document,
        int index)
    {
        HybridSearchBlock block = result.Block;
        int tableRows = block.Text
            .Split('\n')
            .Count(
                line => line.Count(
                    character => character == '|') >= 2);
        return new AiSearchCandidate(
            $"H{index + 1:D3}",
            block.SessionId,
            block.SessionName,
            block.ModifiedTime,
            result.HybridScore,
            new AiLocalMatchSignals(
                RequiredGroupsMatched: 0,
                RequiredGroupsTotal: 0,
                PreferredGroupsMatched: 0,
                PreferredGroupsTotal: 0,
                ExactPhrasesMatched: 0,
                DistinctTermsMatched: 0,
                QuantitativeSignals: 0,
                MarkdownTableRows: tableRows),
            [
                new CandidateEvidence(
                    block.MessageNumber,
                    block.Speaker,
                    block.Text),
            ])
        {
            Document = document,
        };
    }

    private static SessionSearchResult CreateResult(
        string query,
        SessionSearchOptions options,
        IGrouping<string, RankedCandidate> rankedSession)
    {
        IReadOnlyList<RankedCandidate> rankedBlocks = rankedSession
            .OrderByDescending(item => item.Ranking.Score)
            .ToArray();
        RankedCandidate bestBlock = rankedBlocks[0];
        SessionDocument document = bestBlock.Candidate.Document
            ?? throw new InvalidOperationException(
                $"Candidate {bestBlock.Candidate.CandidateId} has no session document.");
        var sections = new List<MatchSection>();
        var addedMessageNumbers = new HashSet<int>();

        foreach (RankedCandidate rankedBlock in rankedBlocks)
        {
            int[] messageNumbers = rankedBlock.Ranking.MessageNumbers.Count > 0
                ? rankedBlock.Ranking.MessageNumbers.ToArray()
                : rankedBlock.Candidate.Evidence
                    .Select(evidence => evidence.MessageNumber)
                    .ToArray();
            var relevance = new AiRelevanceInfo(
                rankedBlock.Ranking.Score,
                rankedBlock.Ranking.Confidence,
                rankedBlock.Ranking.Reason);

            foreach (int messageNumber in messageNumbers)
            {
                if (!addedMessageNumbers.Add(messageNumber))
                {
                    continue;
                }

                int entryIndex = messageNumber - 1;
                if (entryIndex < 0 || entryIndex >= document.Entries.Count)
                {
                    continue;
                }

                ConversationEntry entry = document.Entries[entryIndex];
                string preview = rankedBlock.Candidate.Evidence
                    .FirstOrDefault(
                        evidence =>
                            evidence.MessageNumber == messageNumber)
                    ?.Text
                    ?? CreatePreview(entry.Content);
                sections.Add(
                    new MatchSection(
                        entry.EventId,
                        messageNumber,
                        entry.Speaker,
                        entry.Timestamp,
                        preview,
                        entry.Content,
                        entry.Content,
                        OccurrenceCount: 1,
                        HasAdditionalText: !string.Equals(
                            preview,
                            entry.Content,
                            StringComparison.Ordinal),
                        AiRelevance: relevance));
            }
        }

        if (sections.Count == 0)
        {
            throw new InvalidDataException(
                $"AI session {rankedSession.Key} contained no usable evidence messages.");
        }

        return new SessionSearchResult(
            document,
            query,
            options,
            sections,
            sections.Count,
            new AiRelevanceInfo(
                bestBlock.Ranking.Score,
                bestBlock.Ranking.Confidence,
                bestBlock.Ranking.Reason));
    }

    private static string CreatePreview(string content)
    {
        const int MaximumLength = 1_800;
        return content.Length <= MaximumLength
            ? content
            : content[..MaximumLength] + "...";
    }

    private static List<AiRankingItem> CreateLocalFallbackRankings(
        IReadOnlyList<AiSearchCandidate> candidates)
    {
        return candidates
            .Select(
                (candidate, index) => new AiRankingItem
                {
                    CandidateId = candidate.CandidateId,
                    Confidence = "local",
                    MessageNumbers = candidate.Evidence
                        .Select(evidence => evidence.MessageNumber)
                        .ToList(),
                    Reason =
                        "Local hybrid ranking used because Copilot reranking was unavailable.",
                    Score = Math.Max(1, 80 - index * 2),
                })
            .Take(15)
            .ToList();
    }

    private static string AppendWarning(
        string? existingWarning,
        string additionalWarning)
    {
        return string.IsNullOrWhiteSpace(existingWarning)
            ? additionalWarning
            : existingWarning + " " + additionalWarning;
    }

    private sealed record RankedCandidate(
        AiRankingItem Ranking,
        AiSearchCandidate Candidate);
}
