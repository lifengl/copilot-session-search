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

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusBarText))]
    private string? _statusMessage;

    [ObservableProperty]
    private SessionDetailMessageViewModel? _selectedMessage;

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
        Messages = result.Sections
            .GroupBy(section => section.EntryId, StringComparer.Ordinal)
            .Select(
                group => new SessionDetailMessageViewModel(
                    group.ToArray(),
                    result.Query,
                    result.Options))
            .OrderBy(message => message.MessageNumber)
            .ToArray();
        SelectedMessage = Messages.FirstOrDefault();
    }

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

    public IReadOnlyList<SessionDetailMessageViewModel> Messages { get; }

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
