#nullable enable

namespace CopilotSessionSearch.Models;

public sealed record ConversationEntry(
    string EventId,
    ConversationSpeaker Speaker,
    DateTimeOffset Timestamp,
    string Content);
