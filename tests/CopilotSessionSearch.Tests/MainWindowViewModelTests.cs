#nullable enable

using CopilotSessionSearch.Models;
using CopilotSessionSearch.Services;
using CopilotSessionSearch.ViewModels;

namespace CopilotSessionSearch.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public async Task SearchRemainsEnabledForAnEmptyQueryWithoutStartingWork()
    {
        var historySource = new DelayedHistorySource(
            [],
            new Dictionary<string, TimeSpan>());
        var viewModel = new MainWindowViewModel(
            new SessionSearchCoordinator(
                historySource,
                new SessionDocumentCache(),
                new SessionSearchService()),
            historySource,
            new RecordingThemeService(),
            new RecordingThemePreferenceStore());

        Assert.True(viewModel.SearchCommand.CanExecute(null));
        Assert.True(viewModel.AreSearchInputsEnabled);
        Assert.False(viewModel.CancelCommand.CanExecute(null));

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsSearching);
        Assert.Equal(0, historySource.GetSessionsCallCount);
        Assert.Equal(
            "Enter text to search local Copilot sessions.",
            viewModel.StatusText);

        await viewModel.DisposeAsync();
    }

    [Fact]
    public async Task ResultsStreamWhileSearchingAndRemainSortedByLastActivity()
    {
        SessionDescriptor oldest = CreateDescriptor("oldest", 1);
        SessionDescriptor newest = CreateDescriptor("newest", 3);
        SessionDescriptor middle = CreateDescriptor("middle", 2);
        var historySource = new DelayedHistorySource(
            [newest, middle, oldest],
            new Dictionary<string, TimeSpan>
            {
                [newest.SessionId] = TimeSpan.FromMilliseconds(350),
                [middle.SessionId] = TimeSpan.FromMilliseconds(30),
                [oldest.SessionId] = TimeSpan.FromMilliseconds(450),
            });
        var coordinator = new SessionSearchCoordinator(
            historySource,
            new SessionDocumentCache(),
            new SessionSearchService(),
            maximumConcurrency: 2);
        var themeService = new RecordingThemeService();
        var themePreferenceStore = new RecordingThemePreferenceStore();
        var viewModel = new MainWindowViewModel(
            coordinator,
            historySource,
            themeService,
            themePreferenceStore)
        {
            IsCaseSensitive = true,
            MatchWholeWord = true,
            SearchText = "needle",
            UseRegularExpression = true,
        };

        Assert.Equal(AppThemePreference.System, viewModel.SelectedThemeOption.Preference);
        viewModel.SelectThemeCommand.Execute(viewModel.DarkThemeOption);
        Assert.Equal(AppThemePreference.Dark, themeService.CurrentPreference);
        Assert.Equal(AppThemePreference.Dark, themePreferenceStore.SavedPreference);
        Assert.True(viewModel.IsDarkTheme);
        Assert.True(viewModel.AreSearchInputsEnabled);
        Assert.True(viewModel.AreLiteralSearchOptionsEnabled);
        Assert.False(viewModel.CancelCommand.CanExecute(null));

        Task searchTask = viewModel.SearchCommand.ExecuteAsync(null);
        viewModel.IsCaseSensitive = false;
        viewModel.MatchWholeWord = false;
        viewModel.UseRegularExpression = false;
        await WaitUntilAsync(() => viewModel.Results.Count > 0, TimeSpan.FromSeconds(2));

        Assert.True(viewModel.IsSearching);
        Assert.False(viewModel.AreSearchInputsEnabled);
        Assert.True(viewModel.CancelCommand.CanExecute(null));

        SessionSearchResult? openedResult = null;
        viewModel.OpenDetailsRequested += result => openedResult = result;
        viewModel.OpenDetailsCommand.Execute(viewModel.Results[0]);

        Assert.NotNull(openedResult);

        await searchTask;

        Assert.False(viewModel.IsSearching);
        Assert.True(viewModel.AreSearchInputsEnabled);
        Assert.False(viewModel.CancelCommand.CanExecute(null));
        Assert.NotNull(viewModel.SelectedResult);
        Assert.Equal(3, viewModel.CompletedSessionCount);
        Assert.Equal(3, viewModel.TotalSessionCount);
        Assert.Equal(3, viewModel.MatchingSessionCount);
        Assert.Equal("3 matched sessions", viewModel.MatchSummaryText);
        Assert.All(
            viewModel.Results,
            result =>
            {
                Assert.True(result.Result.Options.IsCaseSensitive);
                Assert.True(result.Result.Options.MatchWholeWord);
                Assert.True(result.Result.Options.UseRegularExpression);
            });
        Assert.Equal(
            [newest.SessionId, middle.SessionId, oldest.SessionId],
            viewModel.Results.Select(result => result.Result.Session.SessionId));

        await viewModel.DisposeAsync();
    }

    [Fact]
    public async Task StartupPreparationStaysQuietUntilAiSearchIsEnabled()
    {
        var historySource = new DelayedHistorySource(
            [],
            new Dictionary<string, TimeSpan>());
        var aiCoordinator = new ConcurrentAiSearchCoordinator();
        var coordinator = new SessionSearchCoordinator(
            historySource,
            new SessionDocumentCache(),
            new SessionSearchService(),
            aiCoordinator);
        var viewModel = new MainWindowViewModel(
            coordinator,
            historySource,
            new RecordingThemeService(),
            new RecordingThemePreferenceStore())
        {
            IsCaseSensitive = true,
            MatchWholeWord = true,
            UseRegularExpression = true,
        };

        Assert.Equal(
            "Search by PR, bug, or phrase, or turn on AI search for natural-language questions",
            viewModel.SearchWatermarkText);

        viewModel.StartBackgroundAiPreparation();
        await aiCoordinator.PreparationStarted;

        Assert.True(viewModel.IsPreparingAiSearch);
        Assert.False(viewModel.IsBackgroundWorkActive);
        Assert.True(viewModel.AreSearchInputsEnabled);
        Assert.True(viewModel.AreLiteralSearchOptionsEnabled);
        Assert.False(viewModel.CancelCommand.CanExecute(null));
        Assert.Equal(
            "Enter text to search local Copilot sessions.",
            viewModel.StatusText);

        viewModel.UseAiSearch = true;

        Assert.Equal(
            "Describe the earlier conversation or investigation you want to find",
            viewModel.SearchWatermarkText);
        Assert.True(viewModel.IsPreparingAiSearch);
        Assert.True(viewModel.IsBackgroundWorkActive);
        Assert.True(viewModel.AreSearchInputsEnabled);
        Assert.False(viewModel.AreLiteralSearchOptionsEnabled);
        Assert.False(viewModel.CancelCommand.CanExecute(null));
        Assert.True(viewModel.IsCaseSensitive);
        Assert.True(viewModel.MatchWholeWord);
        Assert.True(viewModel.UseRegularExpression);

        viewModel.UseAiSearch = false;

        Assert.True(viewModel.IsPreparingAiSearch);
        Assert.False(viewModel.IsBackgroundWorkActive);
        Assert.False(aiCoordinator.PreparationWasCanceled);
        Assert.Equal(
            "Enter text to search local Copilot sessions.",
            viewModel.StatusText);

        aiCoordinator.CompletePreparation();
        await WaitUntilAsync(
            () => !viewModel.IsPreparingAiSearch,
            TimeSpan.FromSeconds(2));

        Assert.True(aiCoordinator.IsReady);
        Assert.False(viewModel.IsBackgroundWorkActive);
        Assert.Equal(
            "Enter text to search local Copilot sessions.",
            viewModel.StatusText);

        await viewModel.DisposeAsync();
    }

    [Fact]
    public async Task CancelingAiSearchLeavesBackgroundIndexPreparationRunning()
    {
        var historySource = new DelayedHistorySource(
            [],
            new Dictionary<string, TimeSpan>());
        var aiCoordinator = new ConcurrentAiSearchCoordinator();
        var coordinator = new SessionSearchCoordinator(
            historySource,
            new SessionDocumentCache(),
            new SessionSearchService(),
            aiCoordinator);
        var viewModel = new MainWindowViewModel(
            coordinator,
            historySource,
            new RecordingThemeService(),
            new RecordingThemePreferenceStore())
        {
            SearchText = "Find a prior investigation",
        };
        viewModel.StartBackgroundAiPreparation();
        await aiCoordinator.PreparationStarted;
        viewModel.UseAiSearch = true;

        Task searchTask = viewModel.SearchCommand.ExecuteAsync(null);
        await aiCoordinator.SearchStarted;
        viewModel.CancelCommand.Execute(null);
        await searchTask;

        Assert.False(viewModel.IsSearching);
        Assert.True(viewModel.IsPreparingAiSearch);
        Assert.False(viewModel.CancelCommand.CanExecute(null));
        Assert.False(aiCoordinator.PreparationWasCanceled);

        aiCoordinator.CompletePreparation();
        await WaitUntilAsync(
            () => !viewModel.IsPreparingAiSearch,
            TimeSpan.FromSeconds(2));
        Assert.True(aiCoordinator.IsReady);

        await viewModel.DisposeAsync();
    }

    [Fact]
    public async Task DisposingViewModelCancelsAiIndexPreparation()
    {
        var historySource = new DelayedHistorySource(
            [],
            new Dictionary<string, TimeSpan>());
        var aiCoordinator = new BlockingAiSearchCoordinator();
        var coordinator = new SessionSearchCoordinator(
            historySource,
            new SessionDocumentCache(),
            new SessionSearchService(),
            aiCoordinator);
        var viewModel = new MainWindowViewModel(
            coordinator,
            historySource,
            new RecordingThemeService(),
            new RecordingThemePreferenceStore());

        viewModel.StartBackgroundAiPreparation();
        await aiCoordinator.PreparationStarted;

        await viewModel.DisposeAsync();

        Assert.True(aiCoordinator.PreparationWasCanceled);
    }

    [Fact]
    public async Task LiteralSearchPausesAndResumesBackgroundPreparation()
    {
        SessionDescriptor descriptor = CreateDescriptor(
            "session",
            day: 1);
        var historySource = new DelayedHistorySource(
            [descriptor],
            new Dictionary<string, TimeSpan>
            {
                [descriptor.SessionId] =
                    TimeSpan.FromMilliseconds(200),
            });
        var aiCoordinator =
            new ResumableAiSearchCoordinator();
        var coordinator = new SessionSearchCoordinator(
            historySource,
            new SessionDocumentCache(),
            new SessionSearchService(),
            aiCoordinator);
        var viewModel = new MainWindowViewModel(
            coordinator,
            historySource,
            new RecordingThemeService(),
            new RecordingThemePreferenceStore())
        {
            SearchText = "needle",
        };
        viewModel.StartBackgroundAiPreparation();
        await aiCoordinator.FirstPreparationStarted;

        Task searchTask = viewModel.SearchCommand.ExecuteAsync(null);
        await aiCoordinator.FirstPreparationCanceled;

        Assert.True(viewModel.IsSearching);
        Assert.Equal(1, aiCoordinator.PreparationCallCount);

        await searchTask;
        await aiCoordinator.SecondPreparationStarted;

        Assert.False(viewModel.IsSearching);
        Assert.True(viewModel.IsPreparingAiSearch);
        Assert.Equal(2, aiCoordinator.PreparationCallCount);

        aiCoordinator.CompleteSecondPreparation();
        await WaitUntilAsync(
            () => !viewModel.IsPreparingAiSearch,
            TimeSpan.FromSeconds(2));

        Assert.True(aiCoordinator.IsReady);

        await viewModel.DisposeAsync();
    }

    [Fact]
    public async Task AiRerankingUsesIndeterminateProgress()
    {
        var historySource = new DelayedHistorySource(
            [],
            new Dictionary<string, TimeSpan>());
        var aiCoordinator = new BlockingRerankingAiSearchCoordinator();
        var coordinator = new SessionSearchCoordinator(
            historySource,
            new SessionDocumentCache(),
            new SessionSearchService(),
            aiCoordinator);
        var viewModel = new MainWindowViewModel(
            coordinator,
            historySource,
            new RecordingThemeService(),
            new RecordingThemePreferenceStore())
        {
            SearchText = "Find a prior investigation",
            UseAiSearch = true,
        };

        Task searchTask = viewModel.SearchCommand.ExecuteAsync(null);
        await aiCoordinator.RerankingStarted;

        Assert.True(viewModel.IsSearching);
        Assert.True(viewModel.IsProgressIndeterminate);
        Assert.Equal(viewModel.TotalSessionCount, viewModel.CompletedSessionCount);
        Assert.Equal(
            "Asking Copilot to rank 24 conversation exchanges...",
            viewModel.StatusText);

        aiCoordinator.CompleteReranking();
        await searchTask;

        Assert.False(viewModel.IsSearching);
        Assert.False(viewModel.IsProgressIndeterminate);

        await viewModel.DisposeAsync();
    }

    [Fact]
    public async Task InvalidRegularExpressionIsRejectedBeforeSessionsAreLoaded()
    {
        var historySource = new DelayedHistorySource(
            [],
            new Dictionary<string, TimeSpan>());
        var viewModel = new MainWindowViewModel(
            new SessionSearchCoordinator(
                historySource,
                new SessionDocumentCache(),
                new SessionSearchService()),
            historySource,
            new RecordingThemeService(),
            new RecordingThemePreferenceStore())
        {
            SearchText = "[",
            UseRegularExpression = true,
        };

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsSearching);
        Assert.Equal(0, historySource.GetSessionsCallCount);
        Assert.Contains(
            "Invalid regular expression",
            viewModel.ErrorMessage,
            StringComparison.Ordinal);
        Assert.Equal("Enter a valid regular expression.", viewModel.StatusText);

        await viewModel.DisposeAsync();
    }

    [Fact]
    public async Task InternalCancellationIsReportedAsSearchFailure()
    {
        var historySource = new DelayedHistorySource(
            [],
            new Dictionary<string, TimeSpan>());
        var viewModel = new MainWindowViewModel(
            new SessionSearchCoordinator(
                historySource,
                new SessionDocumentCache(),
                new SessionSearchService(),
                new InternallyCanceledAiSearchCoordinator()),
            historySource,
            new RecordingThemeService(),
            new RecordingThemePreferenceStore())
        {
            SearchText = "Find an earlier investigation",
            UseAiSearch = true,
        };

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsSearching);
        Assert.Contains(
            "Couldn't complete the search",
            viewModel.ErrorMessage,
            StringComparison.Ordinal);
        Assert.Contains(
            "AI operation timed out",
            viewModel.ErrorMessage,
            StringComparison.Ordinal);
        Assert.Equal(
            "The search couldn't be completed.",
            viewModel.StatusText);

        await viewModel.DisposeAsync();
    }

    private static async Task WaitUntilAsync(
        Func<bool> condition,
        TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException("The expected condition was not reached.");
            }

            await Task.Delay(10);
        }
    }

    private static SessionDescriptor CreateDescriptor(string id, int day)
    {
        return new SessionDescriptor(
            id,
            $"Session {id}",
            DateTimeOffset.Parse("2026-09-01T10:00:00Z").AddDays(day),
            DateTimeOffset.Parse("2026-09-01T11:00:00Z").AddDays(day),
            $@"Q:\ws\{id}",
            "owner/repository",
            "main");
    }

    private sealed class DelayedHistorySource : ISessionHistorySource
    {
        private readonly IReadOnlyList<SessionDescriptor> _sessions;
        private readonly IReadOnlyDictionary<string, TimeSpan> _delays;

        public DelayedHistorySource(
            IReadOnlyList<SessionDescriptor> sessions,
            IReadOnlyDictionary<string, TimeSpan> delays)
        {
            _sessions = sessions;
            _delays = delays;
        }

        public int GetSessionsCallCount { get; private set; }

        public Task<IReadOnlyList<SessionDescriptor>> GetSessionsAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GetSessionsCallCount++;
            return Task.FromResult(_sessions);
        }

        public async Task<SessionDocument> GetSessionDocumentAsync(
            SessionDescriptor session,
            CancellationToken cancellationToken)
        {
            await Task.Delay(_delays[session.SessionId], cancellationToken);

            return new SessionDocument(
                session,
                [
                    new ConversationEntry(
                        session.SessionId + "-event",
                        ConversationSpeaker.Copilot,
                        session.ModifiedTime,
                        $"This session contains a needle for {session.Name}."),
                ]);
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingThemeService : IThemeService
    {
        public AppThemePreference CurrentPreference { get; private set; } =
            AppThemePreference.System;

        public void Apply(AppThemePreference preference)
        {
            CurrentPreference = preference;
        }
    }

    private sealed class RecordingThemePreferenceStore : IThemePreferenceStore
    {
        public AppThemePreference? SavedPreference { get; private set; }

        public AppThemePreference? Load()
        {
            return SavedPreference;
        }

        public void Save(AppThemePreference preference)
        {
            SavedPreference = preference;
        }
    }

    private sealed class BlockingAiSearchCoordinator :
        IAiSessionSearchCoordinator
    {
        private readonly TaskCompletionSource _preparationStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task PreparationStarted => _preparationStarted.Task;

        public bool PreparationWasCanceled { get; private set; }

        public bool IsReady => false;

        public async Task<HybridIndexMetrics> PrepareAsync(
            IProgress<HybridIndexProgress>? progress,
            CancellationToken cancellationToken)
        {
            _preparationStarted.TrySetResult();
            try
            {
                await Task.Delay(
                    Timeout.InfiniteTimeSpan,
                    cancellationToken);
                throw new InvalidOperationException(
                    "The cancellation delay unexpectedly completed.");
            }
            finally
            {
                PreparationWasCanceled =
                    cancellationToken.IsCancellationRequested;
            }
        }

        public async IAsyncEnumerable<SessionSearchUpdate> SearchAsync(
            string query,
            SessionSearchOptions options,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class ConcurrentAiSearchCoordinator :
        IAiSessionSearchCoordinator
    {
        private readonly TaskCompletionSource _preparationStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _preparationCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _searchStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsReady { get; private set; }

        public bool PreparationWasCanceled { get; private set; }

        public Task PreparationStarted => _preparationStarted.Task;

        public Task SearchStarted => _searchStarted.Task;

        public async Task<HybridIndexMetrics> PrepareAsync(
            IProgress<HybridIndexProgress>? progress,
            CancellationToken cancellationToken)
        {
            _preparationStarted.TrySetResult();
            try
            {
                await _preparationCompletion.Task.WaitAsync(
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                PreparationWasCanceled = true;
                throw;
            }

            IsReady = true;
            return new HybridIndexMetrics(
                Sessions: 0,
                Messages: 0,
                Blocks: 0,
                EmbeddingBytes: 0,
                DatabaseBytes: 0,
                UpdatedSessions: 0,
                RemovedSessions: 0,
                Failures: [],
                UpdateTime: TimeSpan.Zero);
        }

        public async IAsyncEnumerable<SessionSearchUpdate> SearchAsync(
            string query,
            SessionSearchOptions options,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken)
        {
            _searchStarted.TrySetResult();
            await Task.Delay(
                Timeout.InfiniteTimeSpan,
                cancellationToken);
            yield break;
        }

        public void CompletePreparation()
        {
            _preparationCompletion.TrySetResult();
        }
    }

    private sealed class ResumableAiSearchCoordinator :
        IAiSessionSearchCoordinator
    {
        private readonly TaskCompletionSource _firstPreparationStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _firstPreparationCanceled =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _secondPreparationStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _secondPreparationCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _preparationCallCount;

        public bool IsReady { get; private set; }

        public int PreparationCallCount =>
            Volatile.Read(ref _preparationCallCount);

        public Task FirstPreparationStarted =>
            _firstPreparationStarted.Task;

        public Task FirstPreparationCanceled =>
            _firstPreparationCanceled.Task;

        public Task SecondPreparationStarted =>
            _secondPreparationStarted.Task;

        public async Task<HybridIndexMetrics> PrepareAsync(
            IProgress<HybridIndexProgress>? progress,
            CancellationToken cancellationToken)
        {
            int callCount = Interlocked.Increment(
                ref _preparationCallCount);
            if (callCount == 1)
            {
                _firstPreparationStarted.TrySetResult();
                try
                {
                    await Task.Delay(
                        Timeout.InfiniteTimeSpan,
                        cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    _firstPreparationCanceled.TrySetResult();
                    throw;
                }
            }

            if (callCount != 2)
            {
                throw new InvalidOperationException(
                    $"Unexpected preparation call {callCount}.");
            }

            _secondPreparationStarted.TrySetResult();
            await _secondPreparationCompletion.Task.WaitAsync(
                cancellationToken);
            IsReady = true;
            return new HybridIndexMetrics(
                Sessions: 1,
                Messages: 1,
                Blocks: 1,
                EmbeddingBytes: 0,
                DatabaseBytes: 0,
                UpdatedSessions: 1,
                RemovedSessions: 0,
                Failures: [],
                UpdateTime: TimeSpan.Zero);
        }

        public async IAsyncEnumerable<SessionSearchUpdate> SearchAsync(
            string query,
            SessionSearchOptions options,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask;
            yield break;
        }

        public void CompleteSecondPreparation()
        {
            _secondPreparationCompletion.TrySetResult();
        }
    }

    private sealed class BlockingRerankingAiSearchCoordinator :
        IAiSessionSearchCoordinator
    {
        private readonly TaskCompletionSource _rerankingStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _rerankingCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsReady => true;

        public Task RerankingStarted => _rerankingStarted.Task;

        public Task<HybridIndexMetrics> PrepareAsync(
            IProgress<HybridIndexProgress>? progress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                new HybridIndexMetrics(
                    Sessions: 10,
                    Messages: 10,
                    Blocks: 24,
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
            yield return new SessionSearchUpdate(
                null,
                null,
                new SessionSearchProgress(10, 10, 0, 0),
                "Asking Copilot to rank 24 conversation exchanges...",
                IsProgressIndeterminate: true);
            _rerankingStarted.TrySetResult();
            await _rerankingCompletion.Task.WaitAsync(cancellationToken);
            yield return new SessionSearchUpdate(
                null,
                null,
                new SessionSearchProgress(10, 10, 0, 0),
                "AI search complete.");
        }

        public void CompleteReranking()
        {
            _rerankingCompletion.TrySetResult();
        }
    }

    private sealed class InternallyCanceledAiSearchCoordinator :
        IAiSessionSearchCoordinator
    {
        public bool IsReady => true;

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
            await Task.Yield();
            if (!cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(
                    "The AI operation timed out.");
            }

            yield break;
        }
    }
}
