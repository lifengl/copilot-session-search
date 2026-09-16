#nullable enable

namespace CopilotSessionSearch.Models;

public sealed record SessionDescriptor(
    string SessionId,
    string Name,
    DateTimeOffset StartTime,
    DateTimeOffset ModifiedTime,
    string? WorkingDirectory,
    string? Repository,
    string? Branch);
