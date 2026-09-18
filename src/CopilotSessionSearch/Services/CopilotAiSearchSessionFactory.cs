#nullable enable

using System.IO;

namespace CopilotSessionSearch.Services;

public sealed class CopilotAiSearchSessionFactory : IAiSearchSessionFactory
{
    private readonly string _workingDirectory;

    public CopilotAiSearchSessionFactory(string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        _workingDirectory = Path.GetFullPath(workingDirectory);
    }

    public async Task<IAiSearchSession> CreateAsync(
        CancellationToken cancellationToken)
    {
        return await CopilotAiSearchSession.CreateAsync(
            _workingDirectory,
            cancellationToken).ConfigureAwait(false);
    }
}
