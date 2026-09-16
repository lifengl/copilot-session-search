#nullable enable

using CopilotSessionSearch.Models;
using CopilotSessionSearch.Services;
using CopilotSessionSearch.ViewModels;

namespace CopilotSessionSearch.Tests;

public sealed class MainWindowViewModelTests
{
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
            SearchText = "needle",
        };

        Assert.Equal(AppThemePreference.System, viewModel.SelectedThemeOption.Preference);
        viewModel.SelectThemeCommand.Execute(viewModel.DarkThemeOption);
        Assert.Equal(AppThemePreference.Dark, themeService.CurrentPreference);
        Assert.Equal(AppThemePreference.Dark, themePreferenceStore.SavedPreference);
        Assert.True(viewModel.IsDarkTheme);
        Assert.False(viewModel.CancelCommand.CanExecute(null));

        Task searchTask = viewModel.SearchCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => viewModel.Results.Count > 0, TimeSpan.FromSeconds(2));

        Assert.True(viewModel.IsSearching);
        Assert.True(viewModel.CancelCommand.CanExecute(null));

        SessionSearchResult? openedResult = null;
        viewModel.OpenDetailsRequested += result => openedResult = result;
        viewModel.OpenDetailsCommand.Execute(viewModel.Results[0]);

        Assert.NotNull(openedResult);

        await searchTask;

        Assert.False(viewModel.IsSearching);
        Assert.False(viewModel.CancelCommand.CanExecute(null));
        Assert.NotNull(viewModel.SelectedResult);
        Assert.Equal(3, viewModel.CompletedSessionCount);
        Assert.Equal(3, viewModel.TotalSessionCount);
        Assert.Equal(3, viewModel.MatchingSessionCount);
        Assert.Equal("3 matched sessions", viewModel.MatchSummaryText);
        Assert.Equal(
            [newest.SessionId, middle.SessionId, oldest.SessionId],
            viewModel.Results.Select(result => result.Result.Session.SessionId));

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
}
