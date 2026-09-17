#nullable enable

using CopilotSessionSearch.Models;

namespace CopilotSessionSearch.ViewModels;

public sealed class SessionDetailMessageViewModel
{
    public SessionDetailMessageViewModel(
        IReadOnlyList<MatchSection> sections,
        string query,
        SessionSearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentNullException.ThrowIfNull(options);

        if (sections.Count == 0)
        {
            throw new ArgumentException(
                "At least one matching section is required.",
                nameof(sections));
        }

        MatchSection firstSection = sections[0];
        EntryId = firstSection.EntryId;
        MessageNumber = firstSection.MessageNumber;
        Speaker = firstSection.Speaker;
        Timestamp = firstSection.Timestamp;
        MarkdownText = firstSection.FullText;
        Query = query;
        Options = options;
        OccurrenceCount = sections.Sum(section => section.OccurrenceCount);
    }

    public string EntryId { get; }

    public int MessageNumber { get; }

    public ConversationSpeaker Speaker { get; }

    public DateTimeOffset Timestamp { get; }

    public string MarkdownText { get; }

    public string Query { get; }

    public SessionSearchOptions Options { get; }

    public bool MatchWholeWord => Options.MatchWholeWord;

    public bool IsCaseSensitive => Options.IsCaseSensitive;

    public int OccurrenceCount { get; }

    public string SpeakerText => Speaker == ConversationSpeaker.User
        ? "You"
        : "Copilot";

    public string TimestampText => DisplayTextFormatter.FormatDateTime(Timestamp);

    public string MessageText => $"Message {MessageNumber:N0}";

    public string OccurrenceText => DisplayTextFormatter.FormatCount(
        OccurrenceCount,
        "match",
        "matches");

    public string AccessibleName =>
        $"{SpeakerText}, {MessageText}, {OccurrenceText}, {TimestampText}";
}
