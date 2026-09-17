#nullable enable

using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CopilotSessionSearch.Models;
using CopilotSessionSearch.Services;

namespace CopilotSessionSearch.ViewModels;

public sealed partial class SessionDetailsViewModel : ObservableObject
{
    private readonly IConsoleLauncher _consoleLauncher;
    private readonly IClipboardService _clipboardService;
    private readonly IReadOnlyList<SessionDetailMessageViewModel> _allMessages;
    private readonly IReadOnlyList<SessionDetailMessageViewModel> _matchingMessages;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusBarText))]
    private string? _statusMessage;

    [ObservableProperty]
    private SessionDetailMessageViewModel? _selectedMessage;

    [ObservableProperty]
    private IReadOnlyList<SessionDetailMessageViewModel> _messages = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShowingMatchingMessages))]
    [NotifyPropertyChangedFor(nameof(MessageViewMenuText))]
    private bool _isShowingWholeConversation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SessionInfoButtonText))]
    private bool _isSessionInfoExpanded = true;

    public SessionDetailsViewModel(
        SessionSearchResult result,
        IConsoleLauncher consoleLauncher,
        IClipboardService clipboardService)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(consoleLauncher);
        ArgumentNullException.ThrowIfNull(clipboardService);

        Result = result;
        _consoleLauncher = consoleLauncher;
        _clipboardService = clipboardService;
        Dictionary<string, int> occurrenceCounts = result.Sections
            .GroupBy(section => section.EntryId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(section => section.OccurrenceCount),
                StringComparer.Ordinal);
        _allMessages = result.Document.Entries
            .Select(
                (entry, index) => new SessionDetailMessageViewModel(
                    entry,
                    index + 1,
                    result.Query,
                    result.Options,
                    occurrenceCounts.TryGetValue(entry.EventId, out int count)
                        ? count
                        : 0))
            .ToArray();
        _matchingMessages = _allMessages
            .Where(message => message.IsMatch)
            .ToArray();
        Messages = _matchingMessages;
        SelectedMessage = Messages.FirstOrDefault();
    }

    public event Action? MessageViewChanged;

    public SessionSearchResult Result { get; }

    public string Name => Result.Session.Name;

    public string SessionId => Result.Session.SessionId;

    public string Query => Result.Query;

    public string WorkingDirectory => string.IsNullOrWhiteSpace(Result.Session.WorkingDirectory)
        ? "(working directory unavailable)"
        : Result.Session.WorkingDirectory;

    public string RepositoryAndBranch
    {
        get
        {
            string? repository = Result.Session.Repository;
            string? branch = Result.Session.Branch;

            if (string.IsNullOrWhiteSpace(repository))
            {
                return "(repository unavailable)";
            }

            return string.IsNullOrWhiteSpace(branch)
                ? repository
                : $"{repository} ({branch})";
        }
    }

    public string StartTimeText => DisplayTextFormatter.FormatDateTime(Result.Session.StartTime);

    public string LastActiveText => DisplayTextFormatter.FormatDateTime(Result.Session.ModifiedTime);

    public string SessionSpanText => DisplayTextFormatter.FormatSessionSpan(Result.SessionSpan);

    public string MessageCountText => DisplayTextFormatter.FormatCount(
        Result.MessageCount,
        "message",
        "messages");

    public string MatchCountText => DisplayTextFormatter.FormatCount(
        Result.MatchCount,
        "match",
        "matches");

    public string SessionSpanSummaryText => $"{SessionSpanText} - {MessageCountText}";

    public string SearchSummaryText => $"{Query} - {MatchCountText}";

    public string ResumeCommandText => $"copilot --resume={SessionId}";

    public string StatusBarText => StatusMessage
        ?? "Up/Down navigate | Page Up/Down scroll | Enter focus text | Esc close | Ctrl+C copy";

    public bool IsShowingMatchingMessages => !IsShowingWholeConversation;

    public string MessageViewMenuText => IsShowingWholeConversation
        ? "View: Whole conversation"
        : "View: Matching messages";

    public string SessionInfoButtonText => IsSessionInfoExpanded
        ? "Collapse session information"
        : "Expand session information";

    [RelayCommand]
    private void ToggleSessionInfo()
    {
        IsSessionInfoExpanded = !IsSessionInfoExpanded;
        MessageViewChanged?.Invoke();
    }

    [RelayCommand]
    private void ShowMatchingMessages()
    {
        SetMessageView(showWholeConversation: false);
    }

    [RelayCommand]
    private void ShowWholeConversation()
    {
        SetMessageView(showWholeConversation: true);
    }

    [RelayCommand]
    private void Resume()
    {
        try
        {
            _consoleLauncher.ResumeSession(Result.Session);
            ErrorMessage = null;
            StatusMessage = null;
        }
        catch (Exception ex) when (
            ex is Win32Exception
            or IOException
            or InvalidOperationException
            or UnauthorizedAccessException)
        {
            ErrorMessage = $"Unable to resume the session: {ex.Message}";
            StatusMessage = null;
        }
    }

    [RelayCommand]
    private void CopySessionInfo()
    {
        try
        {
            _clipboardService.SetText(CreateSessionInfoText());
            ErrorMessage = null;
            StatusMessage = "Session information was copied to the clipboard.";
        }
        catch (Exception ex) when (ex is ExternalException or InvalidOperationException)
        {
            ErrorMessage = $"Unable to copy session information: {ex.Message}";
            StatusMessage = null;
        }
    }

    private void SetMessageView(bool showWholeConversation)
    {
        if (IsShowingWholeConversation == showWholeConversation)
        {
            return;
        }

        SessionDetailMessageViewModel? previousSelection = SelectedMessage;
        int? previousMessageNumber = previousSelection?.MessageNumber;

        Messages = showWholeConversation
            ? _allMessages
            : _matchingMessages;
        IsShowingWholeConversation = showWholeConversation;
        SelectedMessage =
            previousSelection is not null && Messages.Contains(previousSelection)
                ? previousSelection
                : FindNearestMessage(previousMessageNumber);
        MessageViewChanged?.Invoke();
    }

    private SessionDetailMessageViewModel? FindNearestMessage(
        int? messageNumber)
    {
        if (Messages.Count == 0)
        {
            return null;
        }

        if (messageNumber is null)
        {
            return Messages[0];
        }

        return Messages
            .OrderBy(
                message => Math.Abs(
                    (long)message.MessageNumber - messageNumber.Value))
            .ThenBy(
                message => message.MessageNumber > messageNumber.Value
                    ? 1
                    : 0)
            .First();
    }

    private string CreateSessionInfoText()
    {
        return string.Join(
            Environment.NewLine,
            $"Name: {Name}",
            $"Session ID: {SessionId}",
            $"Working directory: {WorkingDirectory}",
            $"Repository: {RepositoryAndBranch}",
            $"Started: {StartTimeText}",
            $"Last active: {LastActiveText}",
            $"Session span: {SessionSpanSummaryText}",
            $"Search: {SearchSummaryText}",
            $"Resume command: {ResumeCommandText}");
    }
}
