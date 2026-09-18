#nullable enable

using System.Collections.Concurrent;
using CopilotSessionSearch.Models;
using CopilotSessionSearch.Services;

namespace CopilotSessionSearch.Tests;

public sealed class SessionSearchCoordinatorTests
{
    [Fact]
    public async Task SearchUsesBoundedParallelWorkersAndReportsEverySession()
    {
        SessionDescriptor[] sessions = Enumerable.Range(0, 8)
            .Select(CreateDescriptor)
            .ToArray();
        var historySource = new FakeHistorySource(sessions, TimeSpan.FromMilliseconds(40));
        var coordinator = new SessionSearchCoordinator(
            historySource,
            new SessionDocumentCache(),
            new SessionSearchService(),
            maximumConcurrency: 3);
        var updates = new List<SessionSearchUpdate>();

        await foreach (SessionSearchUpdate update in coordinator.SearchAsync("needle", CancellationToken.None))
        {
            updates.Add(update);
        }

        Assert.InRange(historySource.MaximumObservedConcurrency, 2, 3);
        Assert.Equal(8, updates.Count(update => update.Result is not null));
        Assert.Equal(8, updates[^1].Progress.CompletedSessions);
        Assert.Equal(8, updates[^1].Progress.MatchingSessions);
        Assert.Equal(0, updates[^1].Progress.FailedSessions);
    }

    [Fact]
    public async Task SearchReportsFailuresWithoutDiscardingOtherResults()
    {
        SessionDescriptor[] sessions = Enumerable.Range(0, 4)
            .Select(CreateDescriptor)
            .ToArray();
        var historySource = new FakeHistorySource(
            sessions,
            TimeSpan.Zero,
            failedSessionId: sessions[1].SessionId);
        var coordinator = new SessionSearchCoordinator(
            historySource,
            new SessionDocumentCache(),
            new SessionSearchService(),
            maximumConcurrency: 2);
        var updates = new List<SessionSearchUpdate>();

        await foreach (SessionSearchUpdate update in coordinator.SearchAsync("needle", CancellationToken.None))
        {
            updates.Add(update);
        }

        Assert.Equal(3, updates.Count(update => update.Result is not null));
        SessionSearchFailure failure = Assert.Single(
            updates.Where(update => update.Failure is not null).Select(update => update.Failure!));
        Assert.Equal(sessions[1].SessionId, failure.Session.SessionId);
        Assert.Equal(1, updates[^1].Progress.FailedSessions);
    }

    [Fact]
    public async Task SearchCancelsAllWorkers()
    {
        SessionDescriptor[] sessions = Enumerable.Range(0, 20)
            .Select(CreateDescriptor)
            .ToArray();
        var historySource = new FakeHistorySource(sessions, TimeSpan.FromSeconds(1));
        var coordinator = new SessionSearchCoordinator(
            historySource,
            new SessionDocumentCache(),
            new SessionSearchService(),
            maximumConcurrency: 4);
        using var cancellationSource = new CancellationTokenSource();

        Task searchTask = Task.Run(
            async () =>
            {
                await foreach (SessionSearchUpdate _ in coordinator.SearchAsync(
                    "needle",
                    cancellationSource.Token))
                {
                }
            });

        await historySource.FirstLoadStarted;
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => searchTask);
        Assert.True(historySource.CancelledLoadCount > 0);
    }

    [Fact]
    public async Task AiSearchDelegatesWithoutParsingLiteralOptions()
    {
        var aiCoordinator = new RecordingAiSearchCoordinator();
        var historySource = new FakeHistorySource([], TimeSpan.Zero);
        var coordinator = new SessionSearchCoordinator(
            historySource,
            new SessionDocumentCache(),
            new SessionSearchService(),
            aiCoordinator);
        var options = new SessionSearchOptions(
            MatchWholeWord: true,
            IsCaseSensitive: true,
            UseRegularExpression: true,
            UseAiSearch: true);

        await foreach (SessionSearchUpdate _ in coordinator.SearchAsync(
            "[invalid regex",
            options,
            CancellationToken.None))
        {
        }

        Assert.Equal("[invalid regex", aiCoordinator.Query);
        Assert.Equal(options, aiCoordinator.Options);
    }

    private static SessionDescriptor CreateDescriptor(int index)
    {
        return new SessionDescriptor(
            $"session-{index}",
            $"Session {index}",
            DateTimeOffset.Parse("2026-09-01T10:00:00Z").AddDays(index),
            DateTimeOffset.Parse("2026-09-01T11:00:00Z").AddDays(index),
            $@"Q:\ws\project-{index}",
            "owner/repository",
            "main");
    }

    private sealed class FakeHistorySource : ISessionHistorySource
    {
        private readonly IReadOnlyList<SessionDescriptor> _sessions;
        private readonly TimeSpan _delay;
        private readonly string? _failedSessionId;
        private readonly TaskCompletionSource _firstLoadStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _activeLoadCount;
        private int _maximumObservedConcurrency;
        private int _cancelledLoadCount;

        public FakeHistorySource(
            IReadOnlyList<SessionDescriptor> sessions,
            TimeSpan delay,
            string? failedSessionId = null)
        {
            _sessions = sessions;
            _delay = delay;
            _failedSessionId = failedSessionId;
        }

        public int MaximumObservedConcurrency => Volatile.Read(ref _maximumObservedConcurrency);

        public int CancelledLoadCount => Volatile.Read(ref _cancelledLoadCount);

        public Task FirstLoadStarted => _firstLoadStarted.Task;

        public Task<IReadOnlyList<SessionDescriptor>> GetSessionsAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_sessions);
        }

        public async Task<SessionDocument> GetSessionDocumentAsync(
            SessionDescriptor session,
            CancellationToken cancellationToken)
        {
            int activeLoads = Interlocked.Increment(ref _activeLoadCount);
            SetMaximumConcurrency(activeLoads);
            _firstLoadStarted.TrySetResult();

            try
            {
                await Task.Delay(_delay, cancellationToken);

                if (string.Equals(
                    session.SessionId,
                    _failedSessionId,
                    StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Synthetic history failure.");
                }

                return new SessionDocument(
                    session,
                    [
                        new ConversationEntry(
                            $"{session.SessionId}-event",
                            ConversationSpeaker.User,
                            session.ModifiedTime,
                            $"A needle appears in {session.Name}."),
                    ]);
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref _cancelledLoadCount);
                throw;
            }
            finally
            {
                Interlocked.Decrement(ref _activeLoadCount);
            }
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        private void SetMaximumConcurrency(int candidate)
        {
            int observed;
            do
            {
                observed = Volatile.Read(ref _maximumObservedConcurrency);
                if (candidate <= observed)
                {
                    return;
                }
            }
            while (Interlocked.CompareExchange(
                ref _maximumObservedConcurrency,
                candidate,
                observed) != observed);
        }
    }

    private sealed class RecordingAiSearchCoordinator :
        IAiSessionSearchCoordinator
    {
        public bool IsReady => true;

        public string? Query { get; private set; }

        public SessionSearchOptions? Options { get; private set; }

        public Task<HybridIndexMetrics> PrepareAsync(
            IProgress<HybridIndexProgress>? progress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                new HybridIndexMetrics(
                    Sessions: 0,
                    Messages: 0,
                    Blocks: 0,
                    EmbeddingBytes: 0,
                    DatabaseBytes: 0,
                    UpdatedSessions: 0,
                    RemovedSessions: 0,
                    Failures: [],
                    UpdateTime: TimeSpan.Zero));
        }

        public async IAsyncEnumerable<SessionSearchUpdate> SearchAsync(
            string query,
            SessionSearchOptions options,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Query = query;
            Options = options;
            yield return new SessionSearchUpdate(
                null,
                null,
                new SessionSearchProgress(0, 0, 0, 0),
                "AI delegated.");
            await Task.CompletedTask;
        }
    }
}
