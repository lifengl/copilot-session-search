#nullable enable

using System.Collections.Concurrent;
using CopilotSessionSearch.Models;

namespace CopilotSessionSearch.Services;

public sealed class SessionDocumentCache
{
    private readonly ConcurrentDictionary<string, SessionDocument> _documents =
        new(StringComparer.Ordinal);

    public async Task<SessionDocument> GetOrLoadAsync(
        SessionDescriptor session,
        Func<SessionDescriptor, CancellationToken, Task<SessionDocument>> loader,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(loader);

        if (_documents.TryGetValue(session.SessionId, out SessionDocument? cachedDocument))
        {
            return cachedDocument;
        }

        SessionDocument loadedDocument = await loader(session, cancellationToken).ConfigureAwait(false);
        return _documents.GetOrAdd(session.SessionId, loadedDocument);
    }
}
