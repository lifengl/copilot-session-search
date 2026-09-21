#nullable enable

using System.IO;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using CopilotSessionSearch.Models;

namespace CopilotSessionSearch.Services;

public sealed class CopilotSdkSessionHistorySource : ISessionHistorySource
{
    private const long EventsPerPage = 1000;
    private const string AiSearchSessionPrefix =
        "copilot-session-search-ai-";

    private readonly CopilotClient _client;
    private readonly CancellationTokenSource
        _clientLifetimeCancellationSource = new();
    private readonly SharedClientStartup _clientStartup;
    private readonly SemaphoreSlim _sessionListGate = new(1, 1);
    private IReadOnlyList<SessionDescriptor>? _sessionSnapshot;
    private bool _disposed;

    public CopilotSdkSessionHistorySource()
    {
        string copilotHome = ResolveCopilotHome();
        if (!Directory.Exists(copilotHome))
        {
            throw new DirectoryNotFoundException(
                $"The Copilot data directory was not found: {copilotHome}");
        }

        _client = new CopilotClient(
            new CopilotClientOptions
            {
                BaseDirectory = copilotHome,
            });
        _clientStartup = new SharedClientStartup(
            cancellationToken =>
                _client.StartAsync(cancellationToken),
            _client.StopAsync,
            _clientLifetimeCancellationSource.Token);
    }

    public async Task<IReadOnlyList<SessionDescriptor>> GetSessionsAsync(
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_sessionSnapshot is not null)
        {
            return _sessionSnapshot;
        }

        await _sessionListGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_sessionSnapshot is not null)
            {
                return _sessionSnapshot;
            }

            await _clientStartup.WaitAsync(
                cancellationToken).ConfigureAwait(false);
            IList<SessionMetadata> sessions = await _client.ListSessionsAsync(
                cancellationToken: cancellationToken).ConfigureAwait(false);

            _sessionSnapshot = sessions
                .Where(
                    session =>
                        ShouldIncludeSession(
                            session.SessionId,
                            session.IsRemote))
                .Select(CreateDescriptor)
                .OrderByDescending(session => session.ModifiedTime)
                .ToArray();

            return _sessionSnapshot;
        }
        finally
        {
            _sessionListGate.Release();
        }
    }

    internal static bool ShouldIncludeSession(
        string sessionId,
        bool isRemote)
    {
        return !isRemote
            && !sessionId.StartsWith(
                AiSearchSessionPrefix,
                StringComparison.Ordinal);
    }

#pragma warning disable GHCP001
    public async Task<SessionDocument> GetSessionDocumentAsync(
        SessionDescriptor session,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(session);

        var entries = new List<ConversationEntry>();
        string? cursor = null;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();

            EventsReadResult page = await _client.Rpc.Sessions.ReadPersistedEventsAsync(
                session.SessionId,
                cursor,
                EventsPerPage,
                EventsReadDirection.Forward,
                cancellationToken).ConfigureAwait(false);

            if (page.CursorStatus != EventsCursorStatus.Ok)
            {
                throw new InvalidOperationException(
                    $"The persisted history snapshot expired for session {session.SessionId}.");
            }

            AddConversationEntries(page.Events, entries);
            cursor = page.Cursor;

            if (page.HasMore && string.IsNullOrEmpty(cursor))
            {
                throw new InvalidOperationException(
                    $"The persisted history cursor was missing for session {session.SessionId}.");
            }

            if (!page.HasMore)
            {
                break;
            }
        }
        while (!string.IsNullOrEmpty(cursor));

        return new SessionDocument(session, entries);
    }
#pragma warning restore GHCP001

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _clientLifetimeCancellationSource.Cancel();
        _sessionListGate.Dispose();
        try
        {
            await _client.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _clientLifetimeCancellationSource.Dispose();
        }
    }

    private static SessionDescriptor CreateDescriptor(SessionMetadata session)
    {
        string name = string.IsNullOrWhiteSpace(session.Summary)
            ? $"Session {CreateShortId(session.SessionId)}"
            : session.Summary.Trim();

        return new SessionDescriptor(
            session.SessionId,
            name,
            session.StartTime,
            session.ModifiedTime,
            session.Context?.WorkingDirectory,
            session.Context?.Repository,
            session.Context?.Branch);
    }

    private static void AddConversationEntries(
        IEnumerable<SessionEvent> events,
        ICollection<ConversationEntry> entries)
    {
        foreach (SessionEvent sessionEvent in events)
        {
            switch (sessionEvent)
            {
                case UserMessageEvent userMessage when IsVisibleUserMessage(userMessage):
                    entries.Add(
                        new ConversationEntry(
                            userMessage.Id.ToString("D"),
                            ConversationSpeaker.User,
                            userMessage.Timestamp,
                            userMessage.Data.Content));
                    break;

                case AssistantMessageEvent assistantMessage
                    when assistantMessage.AgentId is null
                    && !string.IsNullOrWhiteSpace(assistantMessage.Data.Content):
                    entries.Add(
                        new ConversationEntry(
                            assistantMessage.Id.ToString("D"),
                            ConversationSpeaker.Copilot,
                            assistantMessage.Timestamp,
                            assistantMessage.Data.Content));
                    break;
            }
        }
    }

    private static bool IsVisibleUserMessage(UserMessageEvent userMessage)
    {
        string? source = userMessage.Data.Source;

        return userMessage.AgentId is null
            && userMessage.Data.IsAutopilotContinuation is not true
            && !string.IsNullOrWhiteSpace(userMessage.Data.Content)
            && (string.IsNullOrWhiteSpace(source)
                || string.Equals(source, "user", StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveCopilotHome()
    {
        string? configuredHome = Environment.GetEnvironmentVariable("COPILOT_HOME");
        if (!string.IsNullOrWhiteSpace(configuredHome))
        {
            return Path.GetFullPath(configuredHome);
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".copilot");
    }

    private static string CreateShortId(string sessionId)
    {
        const int MaximumLength = 8;
        return sessionId.Length <= MaximumLength
            ? sessionId
            : sessionId[..MaximumLength];
    }
}
