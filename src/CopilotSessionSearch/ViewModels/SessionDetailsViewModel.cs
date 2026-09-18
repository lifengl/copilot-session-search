#nullable enable

using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CopilotSessionSearch.Models;
using CopilotSessionSearch.Services;

namespace CopilotSessionSearch.ViewModels;

public sealed partial class SessionDetailsViewModel : ObservableObject, IDisposable
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
    [NotifyPropertyChangedFor(nameof(MessageViewButtonText))]
    private bool _isShowingWholeConversation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SessionInfoButtonText))]
    private bool _isSessionInfoExpanded = true;

    public SessionDetailsViewModel(
        SessionSearchResult result,
        IConsoleLauncher consoleLauncher,
        IClipboardService clipboardService,
        int initialZoomPercentage =
            WindowZoomViewModel.DefaultPercentage)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(consoleLauncher);
        ArgumentNullException.ThrowIfNull(clipboardService);

        Result = result;
        _consoleLauncher = consoleLauncher;
        _clipboardService = clipboardService;
        Zoom = new WindowZoomViewModel(initialZoomPercentage);
        ILookup<string, MatchSection> sectionsByEntryId = result.Sections
            .ToLookup(
                section => section.EntryId,
                StringComparer.Ordinal);
        Dictionary<string, int> occurrenceCounts =
            sectionsByEntryId.ToDictionary(
                group => group.Key,
                group => group.Sum(section => section.OccurrenceCount),
                StringComparer.Ordinal);
        Dictionary<string, AiRelevanceInfo?> relevanceByEntryId =
            sectionsByEntryId.ToDictionary(
                group => group.Key,
                group => group
                    .Select(section => section.AiRelevance)
                    .OfType<AiRelevanceInfo>()
                    .OrderByDescending(relevance => relevance.Score)
                    .FirstOrDefault(),
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
                        : 0,
                    relevanceByEntryId.GetValueOrDefault(entry.EventId)))
            .ToArray();
        Dictionary<string, SessionDetailMessageViewModel> messagesByEntryId =
            _allMessages.ToDictionary(
                message => message.EntryId,
                StringComparer.Ordinal);
        _matchingMessages = result.Sections
            .Select(section => section.EntryId)
            .Distinct(StringComparer.Ordinal)
            .Where(messagesByEntryId.ContainsKey)
            .Select(entryId => messagesByEntryId[entryId])
            .ToArray();
        Messages = _matchingMessages;
        SelectedMessage = Messages.FirstOrDefault();
    }

    public event Action? MessageViewChanged;

    public SessionSearchResult Result { get; }

    public WindowZoomViewModel Zoom { get; }

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

    public string SearchSummaryText => Result.AiRelevance is AiRelevanceInfo relevance
        ? $"{Query} - AI relevance {relevance.Score} ({relevance.Confidence})"
        : $"{Query} - {MatchCountText}";

    public string ResumeCommandText => $"copilot --resume={SessionId}";

    public string StatusBarText => StatusMessage
        ?? "Up/Down navigate | Page Up/Down scroll | Enter focus text | Esc close | Ctrl+C copy";

    public bool IsShowingMatchingMessages => !IsShowingWholeConversation;

    public string MessageViewButtonText => IsShowingWholeConversation
        ? "Whole conversation"
        : "Matching messages";

    public string SessionInfoButtonText => IsSessionInfoExpanded
        ? "Collapse session information"
        : "Expand session information";

    public void Dispose()
    {
        Zoom.Dispose();
    }

    [RelayCommand]
    private void ToggleSessionInfo()
    {
        IsSessionInfoExpanded = !IsSessionInfoExpanded;
        MessageViewChanged?.Invoke();
    }

    [RelayCommand]
    private void ToggleMessageView()
    {
        SetMessageView(!IsShowingWholeConversation);
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
        CopyText(
            CreateSessionInfoText(),
            "Session information was copied to the clipboard.",
            "Unable to copy session information");
    }

    [RelayCommand]
    private void CopySessionId()
    {
        CopyText(
            SessionId,
            "Session ID was copied to the clipboard.",
            "Unable to copy the session ID");
    }

    private bool CanCopyWorkingDirectory()
    {
        return !string.IsNullOrWhiteSpace(
            Result.Session.WorkingDirectory);
    }

    [RelayCommand(CanExecute = nameof(CanCopyWorkingDirectory))]
    private void CopyWorkingDirectory()
    {
        CopyText(
            Result.Session.WorkingDirectory!,
            "Working directory was copied to the clipboard.",
            "Unable to copy the working directory");
    }

    [RelayCommand]
    private void CopyResume()
    {
        CopyText(
            ResumeCommandText,
            "Resume command was copied to the clipboard.",
            "Unable to copy the resume command");
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

    private void CopyText(
        string text,
        string successMessage,
        string errorPrefix)
    {
        try
        {
            _clipboardService.SetText(text);
            ErrorMessage = null;
            StatusMessage = successMessage;
        }
        catch (Exception ex) when (
            ex is ExternalException
            or InvalidOperationException)
        {
            ErrorMessage = $"{errorPrefix}: {ex.Message}";
            StatusMessage = null;
        }
    }
}
