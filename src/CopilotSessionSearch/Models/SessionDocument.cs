#nullable enable

namespace CopilotSessionSearch.Models;

public sealed class SessionDocument
{
    public SessionDocument(SessionDescriptor session, IEnumerable<ConversationEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(entries);

        Session = session;
        Entries = entries.ToArray();
    }

    public SessionDescriptor Session { get; }

    public IReadOnlyList<ConversationEntry> Entries { get; }

    public int MessageCount => Entries.Count;

    public TimeSpan SessionSpan => Session.ModifiedTime >= Session.StartTime
        ? Session.ModifiedTime - Session.StartTime
        : TimeSpan.Zero;
}
