#nullable enable

using System.Diagnostics;

namespace CopilotSessionSearch.Services;

public interface IProcessLauncher
{
    void Start(ProcessStartInfo startInfo);
}
