#nullable enable

using CopilotSessionSearch.Models;

namespace CopilotSessionSearch.Services;

public interface IConsoleLauncher
{
    void ResumeSession(SessionDescriptor session);
}
