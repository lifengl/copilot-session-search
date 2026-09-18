#nullable enable

using CopilotSessionSearch.Models;

namespace CopilotSessionSearch.ViewModels;

public sealed class SessionDetailMessageViewModel
{
    public SessionDetailMessageViewModel(
        ConversationEntry entry,
        int messageNumber,
        string query,
        SessionSearchOptions options,
        int occurrenceCount,
        AiRelevanceInfo? aiRelevance)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentNullException.ThrowIfNull(options);

        if (messageNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(messageNumber),
                "Message number must be greater than zero.");
        }

        if (occurrenceCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(occurrenceCount),
                "Occurrence count cannot be negative.");
        }

        EntryId = entry.EventId;
        MessageNumber = messageNumber;
        Speaker = entry.Speaker;
        Timestamp = entry.Timestamp;
        MarkdownText = entry.Content;
        Query = query;
        Options = options;
        OccurrenceCount = occurrenceCount;
        AiRelevance = aiRelevance;
    }

    public string EntryId { get; }

    public int MessageNumber { get; }

    public ConversationSpeaker Speaker { get; }

    public DateTimeOffset Timestamp { get; }

    public string MarkdownText { get; }

    public string Query { get; }

    public string HighlightText => Options.UseAiSearch
        ? string.Empty
        : Query;

    public SessionSearchOptions Options { get; }

    public bool MatchWholeWord => Options.MatchWholeWord;

    public bool IsCaseSensitive => Options.IsCaseSensitive;

    public bool UseRegularExpression => Options.UseRegularExpression;

    public bool UseAiSearch => Options.UseAiSearch;

    public int OccurrenceCount { get; }

    public AiRelevanceInfo? AiRelevance { get; }

    public string? AiReason => AiRelevance?.Reason;

    public bool HasAiReason => AiReason is not null;

    public bool IsMatch => OccurrenceCount > 0;

    public string SpeakerText => Speaker == ConversationSpeaker.User
        ? "You"
        : "Copilot";

    public string TimestampText => DisplayTextFormatter.FormatDateTime(Timestamp);

    public string MessageText => $"Message {MessageNumber:N0}";

    public string OccurrenceText => UseAiSearch
        ? AiRelevance is null
            ? "AI evidence"
            : $"AI relevance {AiRelevance.Score} ({AiRelevance.Confidence})"
        : DisplayTextFormatter.FormatCount(
            OccurrenceCount,
            "match",
            "matches");

    public string AccessibleName =>
        $"{SpeakerText}, {MessageText}, {OccurrenceText}, {TimestampText}";
}
