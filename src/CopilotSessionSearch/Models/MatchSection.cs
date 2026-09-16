#nullable enable

namespace CopilotSessionSearch.Models;

public sealed record MatchSection(
    string EntryId,
    int MessageNumber,
    ConversationSpeaker Speaker,
    DateTimeOffset Timestamp,
    string PreviewText,
    string DetailText,
    string FullText,
    int OccurrenceCount,
    bool HasAdditionalText);
