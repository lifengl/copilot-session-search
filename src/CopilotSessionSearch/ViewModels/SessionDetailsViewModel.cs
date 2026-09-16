#nullable enable

using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CopilotSessionSearch.Models;
using CopilotSessionSearch.Services;

namespace CopilotSessionSearch.ViewModels;

public sealed partial class SessionDetailsViewModel : ObservableObject
{
    private readonly IConsoleLauncher _consoleLauncher;

    [ObservableProperty]
    private string? _errorMessage;

    public SessionDetailsViewModel(
        SessionSearchResult result,
        IConsoleLauncher consoleLauncher)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(consoleLauncher);

        Result = result;
        _consoleLauncher = consoleLauncher;
        Sections = result.Sections
            .Select(section => new MatchSectionViewModel(section, result.Query))
            .ToArray();
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

    public IReadOnlyList<MatchSectionViewModel> Sections { get; }

    [RelayCommand]
    private void Resume()
    {
        try
        {
            _consoleLauncher.ResumeSession(Result.Session);
            ErrorMessage = null;
        }
        catch (Exception ex) when (
            ex is Win32Exception
            or IOException
            or InvalidOperationException
            or UnauthorizedAccessException)
        {
            ErrorMessage = $"Unable to resume the session: {ex.Message}";
        }
    }
}
