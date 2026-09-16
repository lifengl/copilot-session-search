#nullable enable

using System.Diagnostics;

namespace CopilotSessionSearch.Services;

public sealed class ProcessLauncher : IProcessLauncher
{
    public void Start(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);

        if (Process.Start(startInfo) is null)
        {
            throw new InvalidOperationException(
                $"The process could not be started: {startInfo.FileName}");
        }
    }
}
