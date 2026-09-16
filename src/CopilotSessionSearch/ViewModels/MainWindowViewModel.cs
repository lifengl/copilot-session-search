#nullable enable

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CopilotSessionSearch.Models;
using CopilotSessionSearch.Services;

namespace CopilotSessionSearch.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject, IAsyncDisposable
{
    private readonly SessionSearchCoordinator _searchCoordinator;
    private readonly ISessionHistorySource _historySource;
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
    private bool _isSearching;

    [ObservableProperty]
    private string _statusText = "Enter text to search local Copilot sessions.";

    [ObservableProperty]
    private string? _errorMessage;

    public MainWindowViewModel(
        SessionSearchCoordinator searchCoordinator,
        ISessionHistorySource historySource)
    {
        ArgumentNullException.ThrowIfNull(searchCoordinator);
        ArgumentNullException.ThrowIfNull(historySource);

        _searchCoordinator = searchCoordinator;
        _historySource = historySource;
    }

    public event Action<SessionSearchResult>? OpenDetailsRequested;

    public ObservableCollection<SessionSearchResultViewModel> Results { get; } = [];

    private bool CanSearch()
    {
        return !_disposed
            && !IsSearching
            && !string.IsNullOrWhiteSpace(SearchText);
    }

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private Task SearchAsync()
    {
        _activeSearchTask = RunSearchAsync(SearchText.Trim());
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

    private async Task RunSearchAsync(string query)
    {
        using var cancellationSource = new CancellationTokenSource();
        _searchCancellationSource = cancellationSource;
        IsSearching = true;
        ErrorMessage = null;
        Results.Clear();
        _latestProgress = new SessionSearchProgress(0, 0, 0, 0);
        StatusText = "Loading the session list...";

        try
        {
            await foreach (SessionSearchUpdate update in _searchCoordinator.SearchAsync(
                query,
                cancellationSource.Token))
            {
                _latestProgress = update.Progress;

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
                : $"Search complete. Found {_latestProgress.MatchingSessions:N0} matching session(s) " +
                  $"out of {_latestProgress.TotalSessions:N0}.";
        }
        catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
        {
            StatusText =
                $"Search canceled after {_latestProgress.CompletedSessions:N0} of " +
                $"{_latestProgress.TotalSessions:N0} sessions. " +
                $"{Results.Count:N0} result(s) remain available.";
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
        int insertionIndex = 0;
        while (insertionIndex < Results.Count
            && Results[insertionIndex].ModifiedTime >= result.ModifiedTime)
        {
            insertionIndex++;
        }

        Results.Insert(insertionIndex, result);
    }

    private static string FormatProgress(SessionSearchProgress progress)
    {
        return
            $"Searched {progress.CompletedSessions:N0} of {progress.TotalSessions:N0} sessions - " +
            $"{progress.MatchingSessions:N0} matching, {progress.FailedSessions:N0} failed.";
    }
}
