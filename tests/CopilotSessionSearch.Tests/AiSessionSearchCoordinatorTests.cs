#nullable enable

using CopilotSessionSearch.Models;
using CopilotSessionSearch.Services;
using CopilotSessionSearch.ViewModels;

namespace CopilotSessionSearch.Tests;

public sealed class AiSessionSearchCoordinatorTests
{
    [Fact]
    public async Task SearchMapsRankedCandidatesToStandardResults()
    {
        SessionDescriptor relevant = CreateDescriptor(
            "relevant",
            "Relevant comparison",
            day: 1);
        SessionDescriptor partial = CreateDescriptor(
            "partial",
            "Partial investigation",
            day: 2);
        var historySource = new FakeHistorySource(
            [
                CreateDocument(
                    relevant,
                    "We benchmarked ClrMD 4.1 against 3.1.",
                    "ClrMD 4.1 took ten seconds while 3.1 took milliseconds."),
                CreateDocument(
                    partial,
                    "ClrMD 4.1 changed the API.",
                    "No comparison was measured."),
            ]);
        var aiSession = new FakeAiSearchSession();
        var hybridIndex = new FakeHybridSearchIndex(
            [
                CreateHybridResult(relevant, messageNumber: 2, score: 0.9),
                CreateHybridResult(partial, messageNumber: 1, score: 0.6),
            ]);
        var coordinator = new AiSessionSearchCoordinator(
            historySource,
            new SessionDocumentCache(),
            hybridIndex,
            new FakeAiSearchSessionFactory(aiSession));
        var options = new SessionSearchOptions(
            MatchWholeWord: true,
            IsCaseSensitive: true,
            UseRegularExpression: true,
            UseAiSearch: true);
        var updates = new List<SessionSearchUpdate>();

        await foreach (SessionSearchUpdate update in coordinator.SearchAsync(
            "Find the ClrMD comparison",
            options,
            CancellationToken.None))
        {
            updates.Add(update);
        }

        SessionSearchResult[] results = updates
            .Where(update => update.Result is not null)
            .Select(update => update.Result!)
            .ToArray();
        Assert.Equal(2, results.Length);
        Assert.Equal("relevant", results[0].Session.SessionId);
        Assert.Equal(96, results[0].AiRelevance?.Score);
        Assert.Equal("Direct measured comparison.", results[0].AiRelevance?.Reason);
        Assert.Contains(
            aiSession.ReceivedCandidates,
            candidate =>
                candidate.SessionId == "relevant"
                && candidate.LocalScore > 0);
        Assert.True(results[0].Options.UseAiSearch);
        Assert.NotEmpty(results[0].Sections);
        Assert.Equal(
            "AI relevance 96 (high)",
            new SessionSearchResultViewModel(results[0]).MatchCountText);
        Assert.Contains(
            updates,
            update => update.StatusText?.StartsWith(
                "AI search complete.",
                StringComparison.Ordinal) is true);
        Assert.True(aiSession.WasDisposed);
    }

    [Fact]
    public async Task SearchIncludesRecentlyActiveLongRunningSession()
    {
        SessionDescriptor descriptor = CreateDescriptor(
            "recent",
            "Recently active session",
            day: 1) with
        {
            ModifiedTime = DateTimeOffset.UtcNow,
        };
        var historySource = new FakeHistorySource(
            [CreateDocument(descriptor, "Useful recent evidence.")]);
        var aiSession = new FakeAiSearchSession
        {
            RankCandidatesInInputOrder = true,
        };
        var coordinator = new AiSessionSearchCoordinator(
            historySource,
            new SessionDocumentCache(),
            new FakeHybridSearchIndex(
                [CreateHybridResult(descriptor, messageNumber: 1, score: 0.8)]),
            new FakeAiSearchSessionFactory(aiSession));
        var updates = new List<SessionSearchUpdate>();

        await foreach (SessionSearchUpdate update in coordinator.SearchAsync(
            "Find useful recent evidence",
            new SessionSearchOptions(
                MatchWholeWord: false,
                IsCaseSensitive: false,
                UseRegularExpression: false,
                UseAiSearch: true),
            CancellationToken.None))
        {
            updates.Add(update);
        }

        SessionSearchResult result = Assert.Single(
            updates
                .Where(update => update.Result is not null)
                .Select(update => update.Result!));
        Assert.Equal(descriptor.SessionId, result.Session.SessionId);
    }

    [Fact]
    public async Task SearchFallsBackToLocalRankingWhenRerankerIsCanceledInternally()
    {
        SessionDescriptor descriptor = CreateDescriptor(
            "session",
            "Session",
            day: 1);
        var historySource = new FakeHistorySource(
            [CreateDocument(descriptor, "Measured ClrMD comparison.")]);
        var aiSession = new FakeAiSearchSession
        {
            RankingException = new OperationCanceledException(
                "The reranker timed out."),
        };
        var coordinator = new AiSessionSearchCoordinator(
            historySource,
            new SessionDocumentCache(),
            new FakeHybridSearchIndex(
                [CreateHybridResult(descriptor, messageNumber: 1, score: 0.8)]),
            new FakeAiSearchSessionFactory(aiSession));
        var updates = new List<SessionSearchUpdate>();

        await foreach (SessionSearchUpdate update in coordinator.SearchAsync(
            "Find a comparison",
            new SessionSearchOptions(
                MatchWholeWord: false,
                IsCaseSensitive: false,
                UseRegularExpression: false,
                UseAiSearch: true),
            CancellationToken.None))
        {
            updates.Add(update);
        }

        SessionSearchResult result = Assert.Single(
            updates
                .Where(update => update.Result is not null)
                .Select(update => update.Result!));
        Assert.Equal("local", result.AiRelevance?.Confidence);
        Assert.Contains(
            updates,
            update => update.WarningMessage?.Contains(
                "reranker timed out",
                StringComparison.OrdinalIgnoreCase) is true);
        Assert.True(aiSession.WasDisposed);
    }

    [Fact]
    public async Task SearchSendsAtMostTwentyFourCandidatesToReranker()
    {
        SessionDescriptor[] descriptors = Enumerable
            .Range(1, 30)
            .Select(
                index => CreateDescriptor(
                    $"session-{index}",
                    $"Session {index}",
                    day: 1))
            .ToArray();
        var historySource = new FakeHistorySource(
            descriptors
                .Select(
                    descriptor => CreateDocument(
                        descriptor,
                        $"Evidence from {descriptor.Name}."))
                .ToArray());
        var aiSession = new FakeAiSearchSession
        {
            RankCandidatesInInputOrder = true,
        };
        var coordinator = new AiSessionSearchCoordinator(
            historySource,
            new SessionDocumentCache(),
            new FakeHybridSearchIndex(
                descriptors
                    .Select(
                        descriptor => CreateHybridResult(
                            descriptor,
                            messageNumber: 1,
                            score: 0.8))
                    .ToArray()),
            new FakeAiSearchSessionFactory(aiSession));
        var updates = new List<SessionSearchUpdate>();

        await foreach (SessionSearchUpdate update in coordinator.SearchAsync(
            "Find relevant evidence",
            new SessionSearchOptions(
                MatchWholeWord: false,
                IsCaseSensitive: false,
                UseRegularExpression: false,
                UseAiSearch: true),
            CancellationToken.None))
        {
            updates.Add(update);
        }

        Assert.Equal(24, aiSession.ReceivedCandidates.Count);
        Assert.Contains(
            updates,
            update => update.StatusText ==
                "Asking Copilot to rank 24 hybrid message blocks..."
                && update.IsProgressIndeterminate);
        Assert.False(updates[^1].IsProgressIndeterminate);
    }

    [Fact]
    public async Task SearchDisposesAiSessionWhenCanceled()
    {
        SessionDescriptor descriptor = CreateDescriptor(
            "session",
            "Session",
            day: 1);
        var historySource = new FakeHistorySource(
            [CreateDocument(descriptor, "ClrMD 4.1")]);
        var aiSession = new FakeAiSearchSession
        {
            WaitForCancellationWhileRanking = true,
        };
        var hybridIndex = new FakeHybridSearchIndex(
            [CreateHybridResult(descriptor, messageNumber: 1, score: 0.8)]);
        var coordinator = new AiSessionSearchCoordinator(
            historySource,
            new SessionDocumentCache(),
            hybridIndex,
            new FakeAiSearchSessionFactory(aiSession));
        using var cancellationSource = new CancellationTokenSource();
        await using IAsyncEnumerator<SessionSearchUpdate> enumerator =
            coordinator.SearchAsync(
                "Find a comparison",
                new SessionSearchOptions(
                    MatchWholeWord: false,
                    IsCaseSensitive: false,
                    UseRegularExpression: false,
                    UseAiSearch: true),
                cancellationSource.Token)
            .GetAsyncEnumerator();

        Assert.True(await enumerator.MoveNextAsync());
        Assert.True(await enumerator.MoveNextAsync());
        Assert.True(await enumerator.MoveNextAsync());
        Task<bool> rankingTask = enumerator.MoveNextAsync().AsTask();
        await aiSession.RankStarted;
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => rankingTask);
        Assert.True(aiSession.WasDisposed);
    }

    [Fact]
    public async Task SearchFallsBackToLocalRankingWhenRerankerFails()
    {
        SessionDescriptor descriptor = CreateDescriptor(
            "session",
            "Session",
            day: 1);
        var historySource = new FakeHistorySource(
            [CreateDocument(descriptor, "Measured ClrMD comparison.")]);
        var aiSession = new FakeAiSearchSession
        {
            FailWhileRanking = true,
        };
        var coordinator = new AiSessionSearchCoordinator(
            historySource,
            new SessionDocumentCache(),
            new FakeHybridSearchIndex(
                [CreateHybridResult(descriptor, messageNumber: 1, score: 0.8)]),
            new FakeAiSearchSessionFactory(aiSession));
        var updates = new List<SessionSearchUpdate>();

        await foreach (SessionSearchUpdate update in coordinator.SearchAsync(
            "Find a comparison",
            new SessionSearchOptions(
                MatchWholeWord: false,
                IsCaseSensitive: false,
                UseRegularExpression: false,
                UseAiSearch: true),
            CancellationToken.None))
        {
            updates.Add(update);
        }

        SessionSearchResult result = Assert.Single(
            updates
                .Where(update => update.Result is not null)
                .Select(update => update.Result!));
        Assert.Equal("local", result.AiRelevance?.Confidence);
        Assert.Contains(
            updates,
            update => update.WarningMessage?.Contains(
                "showing local hybrid ranking",
                StringComparison.OrdinalIgnoreCase) is true);
        Assert.True(aiSession.WasDisposed);
    }

    [Fact]
    public async Task SearchFallsBackToLocalRankingWhenSessionCreationFails()
    {
        SessionDescriptor descriptor = CreateDescriptor(
            "session",
            "Session",
            day: 1);
        var historySource = new FakeHistorySource(
            [CreateDocument(descriptor, "Measured comparison.")]);
        var coordinator = new AiSessionSearchCoordinator(
            historySource,
            new SessionDocumentCache(),
            new FakeHybridSearchIndex(
                [CreateHybridResult(descriptor, messageNumber: 1, score: 0.8)]),
            new ThrowingAiSearchSessionFactory(
                new InvalidOperationException(
                    "Copilot is unavailable.")));
        var updates = new List<SessionSearchUpdate>();

        await foreach (SessionSearchUpdate update in coordinator.SearchAsync(
            "Find a comparison",
            new SessionSearchOptions(
                MatchWholeWord: false,
                IsCaseSensitive: false,
                UseRegularExpression: false,
                UseAiSearch: true),
            CancellationToken.None))
        {
            updates.Add(update);
        }

        SessionSearchResult result = Assert.Single(
            updates
                .Where(update => update.Result is not null)
                .Select(update => update.Result!));
        Assert.Equal("local", result.AiRelevance?.Confidence);
        Assert.Contains(
            updates,
            update => update.WarningMessage?.Contains(
                "Copilot is unavailable",
                StringComparison.Ordinal) is true);
    }

    [Fact]
    public async Task SearchSkipsCandidateSessionCanceledByInternalTimeout()
    {
        SessionDescriptor readable = CreateDescriptor(
            "readable",
            "Readable",
            day: 1);
        SessionDescriptor canceled = CreateDescriptor(
            "canceled",
            "Canceled",
            day: 2);
        var historySource = new FakeHistorySource(
            [
                CreateDocument(readable, "Useful evidence."),
                CreateDocument(canceled, "Unavailable evidence."),
            ],
            internallyCanceledSessionIds: [canceled.SessionId]);
        var aiSession = new FakeAiSearchSession
        {
            RankCandidatesInInputOrder = true,
        };
        var coordinator = new AiSessionSearchCoordinator(
            historySource,
            new SessionDocumentCache(),
            new FakeHybridSearchIndex(
                [
                    CreateHybridResult(
                        canceled,
                        messageNumber: 1,
                        score: 0.9),
                    CreateHybridResult(
                        readable,
                        messageNumber: 1,
                        score: 0.8),
                ]),
            new FakeAiSearchSessionFactory(aiSession));
        var updates = new List<SessionSearchUpdate>();

        await foreach (SessionSearchUpdate update in coordinator.SearchAsync(
            "Find useful evidence",
            new SessionSearchOptions(
                MatchWholeWord: false,
                IsCaseSensitive: false,
                UseRegularExpression: false,
                UseAiSearch: true),
            CancellationToken.None))
        {
            updates.Add(update);
        }

        SessionSearchResult result = Assert.Single(
            updates
                .Where(update => update.Result is not null)
                .Select(update => update.Result!));
        Assert.Equal(readable.SessionId, result.Session.SessionId);
        Assert.Contains(
            updates,
            update =>
                update.Failure?.Session.SessionId == canceled.SessionId
                && update.Failure.Message.Contains(
                    "timed out",
                    StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SearchTreatsEmptyRankingAsNoRelevantResults()
    {
        SessionDescriptor descriptor = CreateDescriptor(
            "session",
            "Session",
            day: 1);
        var historySource = new FakeHistorySource(
            [CreateDocument(descriptor, "Related but irrelevant text.")]);
        var aiSession = new FakeAiSearchSession
        {
            ReturnEmptyRanking = true,
        };
        var coordinator = new AiSessionSearchCoordinator(
            historySource,
            new SessionDocumentCache(),
            new FakeHybridSearchIndex(
                [CreateHybridResult(descriptor, messageNumber: 1, score: 0.8)]),
            new FakeAiSearchSessionFactory(aiSession));
        var updates = new List<SessionSearchUpdate>();

        await foreach (SessionSearchUpdate update in coordinator.SearchAsync(
            "Find specific evidence",
            new SessionSearchOptions(
                MatchWholeWord: false,
                IsCaseSensitive: false,
                UseRegularExpression: false,
                UseAiSearch: true),
            CancellationToken.None))
        {
            updates.Add(update);
        }

        Assert.DoesNotContain(
            updates,
            update => update.Result is not null);
        Assert.DoesNotContain(
            updates,
            update => update.WarningMessage is not null);
        Assert.Contains(
            updates,
            update => update.StatusText?.Contains(
                "no relevant sessions",
                StringComparison.OrdinalIgnoreCase) is true);
        Assert.True(aiSession.WasDisposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SearchPreservesResultsWhenSessionCleanupFails(
        bool rankingFails)
    {
        SessionDescriptor descriptor = CreateDescriptor(
            "session",
            "Session",
            day: 1);
        var historySource = new FakeHistorySource(
            [CreateDocument(descriptor, "Useful evidence.")]);
        var aiSession = new FakeAiSearchSession
        {
            DisposalException = new IOException(
                "Cleanup failed."),
            FailWhileRanking = rankingFails,
            RankCandidatesInInputOrder = true,
        };
        var coordinator = new AiSessionSearchCoordinator(
            historySource,
            new SessionDocumentCache(),
            new FakeHybridSearchIndex(
                [CreateHybridResult(descriptor, messageNumber: 1, score: 0.8)]),
            new FakeAiSearchSessionFactory(aiSession));
        var updates = new List<SessionSearchUpdate>();

        await foreach (SessionSearchUpdate update in coordinator.SearchAsync(
            "Find useful evidence",
            new SessionSearchOptions(
                MatchWholeWord: false,
                IsCaseSensitive: false,
                UseRegularExpression: false,
                UseAiSearch: true),
            CancellationToken.None))
        {
            updates.Add(update);
        }

        Assert.Single(
            updates,
            update => update.Result is not null);
        Assert.Contains(
            updates,
            update => update.WarningMessage?.Contains(
                "Cleanup failed",
                StringComparison.Ordinal) is true);
        Assert.True(aiSession.WasDisposed);
    }

    [Fact]
    public async Task CancellationDuringCleanupDoesNotBecomeEmptySuccess()
    {
        SessionDescriptor descriptor = CreateDescriptor(
            "session",
            "Session",
            day: 1);
        var historySource = new FakeHistorySource(
            [CreateDocument(descriptor, "Irrelevant text.")]);
        using var cancellationSource =
            new CancellationTokenSource();
        var aiSession = new FakeAiSearchSession
        {
            DisposeAction = cancellationSource.Cancel,
            ReturnEmptyRanking = true,
        };
        var coordinator = new AiSessionSearchCoordinator(
            historySource,
            new SessionDocumentCache(),
            new FakeHybridSearchIndex(
                [CreateHybridResult(descriptor, messageNumber: 1, score: 0.8)]),
            new FakeAiSearchSessionFactory(aiSession));

        async Task EnumerateAsync()
        {
            await foreach (SessionSearchUpdate _ in coordinator.SearchAsync(
                "Find specific evidence",
                new SessionSearchOptions(
                    MatchWholeWord: false,
                    IsCaseSensitive: false,
                    UseRegularExpression: false,
                    UseAiSearch: true),
                cancellationSource.Token))
            {
            }
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            EnumerateAsync);
        Assert.True(aiSession.WasDisposed);
    }

    [Fact]
    public async Task SearchSkipsUnreadableCandidateSession()
    {
        SessionDescriptor readable = CreateDescriptor(
            "readable",
            "Readable",
            day: 1);
        SessionDescriptor unreadable = CreateDescriptor(
            "unreadable",
            "Unreadable",
            day: 2);
        var historySource = new FakeHistorySource(
            [
                CreateDocument(readable, "Useful evidence."),
                CreateDocument(unreadable, "Unavailable evidence."),
            ],
            unreadableSessionIds: [unreadable.SessionId]);
        var aiSession = new FakeAiSearchSession
        {
            RankCandidatesInInputOrder = true,
        };
        var coordinator = new AiSessionSearchCoordinator(
            historySource,
            new SessionDocumentCache(),
            new FakeHybridSearchIndex(
                [
                    CreateHybridResult(
                        unreadable,
                        messageNumber: 1,
                        score: 0.9),
                    CreateHybridResult(
                        readable,
                        messageNumber: 1,
                        score: 0.8),
                ],
                failures:
                [
                    new SessionSearchFailure(
                        unreadable,
                        "Indexing failed."),
                ]),
            new FakeAiSearchSessionFactory(aiSession));
        var updates = new List<SessionSearchUpdate>();

        await foreach (SessionSearchUpdate update in coordinator.SearchAsync(
            "Find useful evidence",
            new SessionSearchOptions(
                MatchWholeWord: false,
                IsCaseSensitive: false,
                UseRegularExpression: false,
                UseAiSearch: true),
            CancellationToken.None))
        {
            updates.Add(update);
        }

        SessionSearchResult result = Assert.Single(
            updates
                .Where(update => update.Result is not null)
                .Select(update => update.Result!));
        Assert.Equal(
            readable.SessionId,
            result.Session.SessionId);
        Assert.Single(aiSession.ReceivedCandidates);
        Assert.Contains(
            updates,
            update =>
                update.Failure?.Session.SessionId
                    == unreadable.SessionId);
        Assert.Equal(
            1,
            updates.Max(update => update.Progress.FailedSessions));
        Assert.True(aiSession.WasDisposed);
    }

    private static SessionDescriptor CreateDescriptor(
        string id,
        string name,
        int day)
    {
        return new SessionDescriptor(
            id,
            name,
            DateTimeOffset.Parse("2026-09-01T10:00:00Z").AddDays(day),
            DateTimeOffset.Parse("2026-09-01T11:00:00Z").AddDays(day),
            $@"Q:\ws\{id}",
            "owner/repository",
            "main");
    }

    private static SessionDocument CreateDocument(
        SessionDescriptor descriptor,
        params string[] messages)
    {
        ConversationEntry[] entries = messages
            .Select(
                (message, index) => new ConversationEntry(
                    $"{descriptor.SessionId}-{index}",
                    index % 2 == 0
                        ? ConversationSpeaker.User
                        : ConversationSpeaker.Copilot,
                    descriptor.StartTime.AddMinutes(index),
                    message))
            .ToArray();
        return new SessionDocument(descriptor, entries);
    }

    private static HybridRankedBlock CreateHybridResult(
        SessionDescriptor descriptor,
        int messageNumber,
        double score)
    {
        var block = new HybridSearchBlock(
            messageNumber,
            descriptor.SessionId,
            descriptor.Name,
            descriptor.ModifiedTime,
            messageNumber,
            ChunkNumber: 1,
            Speaker: "Copilot",
            Text: $"Evidence from message {messageNumber}.",
            RetrievalText: string.Empty,
            Embedding: []);
        return new HybridRankedBlock(
            block,
            score,
            WordRank: 1,
            TrigramRank: null,
            EmbeddingRank: 1,
            ExactRank: 1,
            EmbeddingSimilarity: 0.8,
            ExactScore: 50);
    }

    private sealed class FakeHistorySource : ISessionHistorySource
    {
        private readonly IReadOnlyList<SessionDocument> _documents;
        private readonly HashSet<string> _internallyCanceledSessionIds;
        private readonly HashSet<string> _unreadableSessionIds;

        public FakeHistorySource(
            IReadOnlyList<SessionDocument> documents,
            IEnumerable<string>? unreadableSessionIds = null,
            IEnumerable<string>? internallyCanceledSessionIds = null)
        {
            _documents = documents;
            _unreadableSessionIds = new HashSet<string>(
                unreadableSessionIds ?? [],
                StringComparer.Ordinal);
            _internallyCanceledSessionIds = new HashSet<string>(
                internallyCanceledSessionIds ?? [],
                StringComparer.Ordinal);
        }

        public Task<IReadOnlyList<SessionDescriptor>> GetSessionsAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<SessionDescriptor>>(
                _documents.Select(document => document.Session).ToArray());
        }

        public Task<SessionDocument> GetSessionDocumentAsync(
            SessionDescriptor session,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_internallyCanceledSessionIds.Contains(
                session.SessionId))
            {
                throw new OperationCanceledException(
                    "The session read timed out.");
            }

            if (_unreadableSessionIds.Contains(
                session.SessionId))
            {
                throw new InvalidDataException(
                    "The persisted session could not be read.");
            }

            return Task.FromResult(
                _documents.Single(
                    document => document.Session.SessionId == session.SessionId));
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingAiSearchSessionFactory :
        IAiSearchSessionFactory
    {
        private readonly Exception _exception;

        public ThrowingAiSearchSessionFactory(
            Exception exception)
        {
            _exception = exception;
        }

        public Task<IAiSearchSession> CreateAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromException<IAiSearchSession>(
                _exception);
        }
    }

    private sealed class FakeAiSearchSessionFactory : IAiSearchSessionFactory
    {
        private readonly IAiSearchSession _session;

        public FakeAiSearchSessionFactory(IAiSearchSession session)
        {
            _session = session;
        }

        public Task<IAiSearchSession> CreateAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_session);
        }
    }

    private sealed class FakeAiSearchSession : IAiSearchSession
    {
        private readonly TaskCompletionSource _rankStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool WaitForCancellationWhileRanking { get; init; }

        public bool FailWhileRanking { get; init; }

        public Exception? RankingException { get; init; }

        public bool RankCandidatesInInputOrder { get; init; }

        public bool ReturnEmptyRanking { get; init; }

        public Exception? DisposalException { get; init; }

        public Action? DisposeAction { get; init; }

        public bool WasDisposed { get; private set; }

        public Task RankStarted => _rankStarted.Task;

        public IReadOnlyList<AiSearchCandidate> ReceivedCandidates { get; private set; } = [];

        public async Task<AiSearchPlan> CreatePlanAsync(
            string query,
            CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            return new AiSearchPlan
            {
                RequiredGroups =
                [
                    new AiTermGroup
                    {
                        Name = "subject",
                        AnyOf = ["ClrMD"],
                    },
                    new AiTermGroup
                    {
                        Name = "version",
                        AnyOf = ["4.1"],
                    },
                ],
                PreferredGroups =
                [
                    new AiTermGroup
                    {
                        Name = "old version",
                        AnyOf = ["3.1"],
                    },
                    new AiTermGroup
                    {
                        Name = "comparison",
                        AnyOf = ["comparison", "against", "while"],
                    },
                ],
            };
        }

        public async Task<List<AiRankingItem>> RankAsync(
            string query,
            IReadOnlyList<AiSearchCandidate> candidates,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReceivedCandidates = candidates;
            _rankStarted.TrySetResult();
            if (WaitForCancellationWhileRanking)
            {
                await Task.Delay(
                    Timeout.InfiniteTimeSpan,
                    cancellationToken);
            }

            if (RankingException is not null)
            {
                throw RankingException;
            }

            if (FailWhileRanking)
            {
                throw new InvalidDataException(
                    "The reranker returned no valid candidate IDs.");
            }

            if (ReturnEmptyRanking)
            {
                return [];
            }

            if (RankCandidatesInInputOrder)
            {
                return candidates
                    .Take(15)
                    .Select(
                        (candidate, index) => new AiRankingItem
                        {
                            CandidateId = candidate.CandidateId,
                            Confidence = "medium",
                            MessageNumbers = candidate.Evidence
                                .Select(evidence => evidence.MessageNumber)
                                .ToList(),
                            Reason = "Ranked in supplied order.",
                            Score = 80 - index,
                        })
                    .ToList();
            }

            AiSearchCandidate relevant = candidates.First(
                candidate => candidate.SessionId == "relevant");
            AiSearchCandidate partial = candidates.First(
                candidate => candidate.SessionId == "partial");
            return
                new List<AiRankingItem>
                {
                    new()
                    {
                        CandidateId = relevant.CandidateId,
                        Confidence = "high",
                        MessageNumbers = relevant.Evidence
                            .Select(evidence => evidence.MessageNumber)
                            .ToList(),
                        Reason = "Direct measured comparison.",
                        Score = 96,
                    },
                    new()
                    {
                        CandidateId = partial.CandidateId,
                        Confidence = "low",
                        MessageNumbers =
                        [
                            partial.Evidence[0].MessageNumber,
                        ],
                        Reason = "Mentions only the newer version.",
                        Score = 25,
                    },
                };
        }

        public AiUsageSummary GetUsage()
        {
            return new AiUsageSummary(
                ApiCalls: 2,
                InputTokens: 1000,
                OutputTokens: 100,
                AiCredits: 2);
        }

        public ValueTask DisposeAsync()
        {
            WasDisposed = true;
            DisposeAction?.Invoke();
            return DisposalException is null
                ? ValueTask.CompletedTask
                : ValueTask.FromException(
                    DisposalException);
        }
    }

    private sealed class FakeHybridSearchIndex : IHybridSearchIndex
    {
        private readonly IReadOnlyList<HybridRankedBlock> _results;
        private readonly IReadOnlyList<SessionSearchFailure> _failures;

        public FakeHybridSearchIndex(
            IReadOnlyList<HybridRankedBlock> results,
            IReadOnlyList<SessionSearchFailure>? failures = null)
        {
            _results = results;
            _failures = failures ?? [];
        }

        public bool IsReady => true;

        public Task<HybridIndexMetrics> PrepareAsync(
            ISessionHistorySource historySource,
            SessionDocumentCache documentCache,
            IProgress<HybridIndexProgress>? progress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                new HybridIndexMetrics(
                    Sessions: _results
                        .Select(result => result.Block.SessionId)
                        .Distinct()
                        .Count(),
                    Messages: _results.Count,
                    Blocks: _results.Count,
                    EmbeddingBytes: 0,
                    DatabaseBytes: 0,
                    UpdatedSessions: 0,
                    RemovedSessions: 0,
                    Failures: _failures,
                    UpdateTime: TimeSpan.Zero));
        }

        public HybridQueryResult Search(
            string query,
            int maximumResults = 30,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new HybridQueryResult(
                query,
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.Zero,
                WordResults: _results,
                EmbeddingResults: _results,
                HybridResults: _results.Take(maximumResults).ToArray());
        }

        public void Dispose()
        {
        }
    }
}
