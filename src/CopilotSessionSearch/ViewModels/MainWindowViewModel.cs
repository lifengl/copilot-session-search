#nullable enable

using System.Collections.ObjectModel;
using System.IO;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CopilotSessionSearch.Models;
using CopilotSessionSearch.Services;

namespace CopilotSessionSearch.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject, IAsyncDisposable
{
    private readonly SessionSearchCoordinator _searchCoordinator;
    private readonly ISessionHistorySource _historySource;
    private readonly IThemePreferenceStore _themePreferenceStore;
    private readonly IThemeService _themeService;
    private CancellationTokenSource? _searchCancellationSource;
    private Task _activeSearchTask = Task.CompletedTask;
    private SessionSearchProgress _latestProgress = new(0, 0, 0, 0);
    private bool _disposed;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyPropertyChangedFor(nameof(AreSearchOptionsEnabled))]
    private bool _isSearching;

    [ObservableProperty]
    private string _statusText = "Enter text to search local Copilot sessions.";

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private SessionSearchResultViewModel? _selectedResult;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ThemeMenuText))]
    private ThemeOption _selectedThemeOption;

    [ObservableProperty]
    private int _completedSessionCount;

    [ObservableProperty]
    private int _totalSessionCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MatchSummaryText))]
    private int _matchingSessionCount;

    [ObservableProperty]
    private bool _matchWholeWord;

    [ObservableProperty]
    private bool _isCaseSensitive;

    [ObservableProperty]
    private bool _useRegularExpression;

    public MainWindowViewModel(
        SessionSearchCoordinator searchCoordinator,
        ISessionHistorySource historySource,
        IThemeService themeService,
        IThemePreferenceStore themePreferenceStore)
    {
        ArgumentNullException.ThrowIfNull(searchCoordinator);
        ArgumentNullException.ThrowIfNull(historySource);
        ArgumentNullException.ThrowIfNull(themeService);
        ArgumentNullException.ThrowIfNull(themePreferenceStore);

        _searchCoordinator = searchCoordinator;
        _historySource = historySource;
        _themeService = themeService;
        _themePreferenceStore = themePreferenceStore;
        ThemeOptions =
        [
            new ThemeOption(AppThemePreference.System, "System"),
            new ThemeOption(AppThemePreference.Light, "Light"),
            new ThemeOption(AppThemePreference.Dark, "Dark"),
        ];
        SystemThemeOption = ThemeOptions[0];
        LightThemeOption = ThemeOptions[1];
        DarkThemeOption = ThemeOptions[2];
        _selectedThemeOption = ThemeOptions.Single(
            option => option.Preference == themeService.CurrentPreference);
    }

    public event Action<SessionSearchResult>? OpenDetailsRequested;

    public ObservableCollection<SessionSearchResultViewModel> Results { get; } = [];

    public IReadOnlyList<ThemeOption> ThemeOptions { get; }

    public ThemeOption SystemThemeOption { get; }

    public ThemeOption LightThemeOption { get; }

    public ThemeOption DarkThemeOption { get; }

    public bool IsSystemTheme =>
        SelectedThemeOption.Preference == AppThemePreference.System;

    public bool IsLightTheme =>
        SelectedThemeOption.Preference == AppThemePreference.Light;

    public bool IsDarkTheme =>
        SelectedThemeOption.Preference == AppThemePreference.Dark;

    public string ThemeMenuText => $"Theme: {SelectedThemeOption.DisplayName}";

    public string MatchSummaryText => MatchingSessionCount == 1
        ? "1 matched session"
        : $"{MatchingSessionCount:N0} matched sessions";

    public bool AreSearchOptionsEnabled => !IsSearching;

    partial void OnSelectedThemeOptionChanged(ThemeOption value)
    {
        _themeService.Apply(value.Preference);
        try
        {
            _themePreferenceStore.Save(value.Preference);
            StatusText = $"{value.DisplayName} theme selected.";
        }
        catch (Exception ex) when (
            ex is IOException
            or UnauthorizedAccessException
            or NotSupportedException)
        {
            ErrorMessage =
                $"{value.DisplayName} theme is active for this run, " +
                $"but the preference could not be saved: {ex.Message}";
            StatusText = "The theme preference could not be saved.";
        }

        OnPropertyChanged(nameof(IsSystemTheme));
        OnPropertyChanged(nameof(IsLightTheme));
        OnPropertyChanged(nameof(IsDarkTheme));
    }

    private bool CanSearch()
    {
        return !_disposed
            && !IsSearching
            && !string.IsNullOrWhiteSpace(SearchText);
    }

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private Task SearchAsync()
    {
        var options = new SessionSearchOptions(
            MatchWholeWord: MatchWholeWord,
            IsCaseSensitive: IsCaseSensitive,
            UseRegularExpression: UseRegularExpression);

        TextSearchPattern pattern;
        try
        {
            pattern = TextSearchPattern.Create(SearchText, options);
        }
        catch (ArgumentException ex) when (options.UseRegularExpression)
        {
            ErrorMessage = $"Invalid regular expression: {ex.Message}";
            StatusText = "Enter a valid regular expression.";
            return Task.CompletedTask;
        }

        _activeSearchTask = RunSearchAsync(pattern);
        return _activeSearchTask;
    }

    private bool CanCancel()
    {
        return IsSearching;
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _searchCancellationSource?.Cancel();
    }

    [RelayCommand]
    private void OpenDetails(SessionSearchResultViewModel? result)
    {
        if (result is not null)
        {
            OpenDetailsRequested?.Invoke(result.Result);
        }
    }

    [RelayCommand]
    private void SelectTheme(ThemeOption? option)
    {
        if (option is not null)
        {
            SelectedThemeOption = option;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SearchCommand.NotifyCanExecuteChanged();
        _searchCancellationSource?.Cancel();

        try
        {
            await _activeSearchTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        _searchCancellationSource?.Dispose();
        _searchCancellationSource = null;
        await _historySource.DisposeAsync().ConfigureAwait(false);
    }

    private async Task RunSearchAsync(TextSearchPattern pattern)
    {
        using var cancellationSource = new CancellationTokenSource();
        _searchCancellationSource = cancellationSource;
        IsSearching = true;
        ErrorMessage = null;
        Results.Clear();
        SelectedResult = null;
        _latestProgress = new SessionSearchProgress(0, 0, 0, 0);
        UpdateProgress(_latestProgress);
        StatusText = "Loading the session list...";

        try
        {
            await foreach (SessionSearchUpdate update in _searchCoordinator.SearchAsync(
                pattern,
                cancellationSource.Token))
            {
                _latestProgress = update.Progress;
                UpdateProgress(update.Progress);

                if (update.Result is not null)
                {
                    InsertResult(new SessionSearchResultViewModel(update.Result));
                }

                if (update.Failure is not null)
                {
                    ErrorMessage =
                        $"{update.Progress.FailedSessions:N0} session(s) could not be read. " +
                        $"Latest: {update.Failure.Session.Name}: {update.Failure.Message}";
                }

                StatusText = FormatProgress(update.Progress);
            }

            StatusText = _latestProgress.TotalSessions == 0
                ? "No local Copilot sessions were found."
                : $"Search complete. Searched {_latestProgress.TotalSessions:N0} sessions.";
        }
        catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
        {
            StatusText =
                $"Search canceled after {_latestProgress.CompletedSessions:N0} of " +
                $"{_latestProgress.TotalSessions:N0} sessions.";
        }
        catch (RegexMatchTimeoutException)
        {
            ErrorMessage =
                "The regular expression took too long to evaluate. " +
                "Simplify it and try again.";
            StatusText = "The regular-expression search timed out.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ErrorMessage = $"Search failed: {ex.Message}";
            StatusText = "The search could not be completed.";
        }
        finally
        {
            if (ReferenceEquals(_searchCancellationSource, cancellationSource))
            {
                _searchCancellationSource = null;
            }

            IsSearching = false;
        }
    }

    private void InsertResult(SessionSearchResultViewModel result)
    {
        bool selectResult = SelectedResult is null;
        int insertionIndex = 0;
        while (insertionIndex < Results.Count
            && Results[insertionIndex].ModifiedTime >= result.ModifiedTime)
        {
            insertionIndex++;
        }

        Results.Insert(insertionIndex, result);

        if (selectResult)
        {
            SelectedResult = result;
        }
    }

    private void UpdateProgress(SessionSearchProgress progress)
    {
        CompletedSessionCount = progress.CompletedSessions;
        TotalSessionCount = progress.TotalSessions;
        MatchingSessionCount = progress.MatchingSessions;
    }

    private static string FormatProgress(SessionSearchProgress progress)
    {
        return
            $"Searched {progress.CompletedSessions:N0} of {progress.TotalSessions:N0} sessions - " +
            $"{progress.FailedSessions:N0} failed.";
    }
}
