#nullable enable

using CopilotSessionSearch.Models;

namespace CopilotSessionSearch.Services;

public interface ISessionHistorySource : IAsyncDisposable
{
    Task<IReadOnlyList<SessionDescriptor>> GetSessionsAsync(CancellationToken cancellationToken);

    Task<SessionDocument> GetSessionDocumentAsync(
        SessionDescriptor session,
        CancellationToken cancellationToken);
}
