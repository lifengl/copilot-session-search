#nullable enable

namespace CopilotSessionSearch.Models;

public sealed record SessionSearchFailure(
    SessionDescriptor Session,
    string Message);
