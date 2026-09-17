#nullable enable

using CopilotSessionSearch.Models;

namespace CopilotSessionSearch.ViewModels;

public sealed class MatchSectionViewModel
{
    public MatchSectionViewModel(
        MatchSection section,
        string query,
        SessionSearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentNullException.ThrowIfNull(options);

        Section = section;
        Query = query;
        Options = options;
    }

    public MatchSection Section { get; }

    public string Query { get; }

    public SessionSearchOptions Options { get; }

    public bool MatchWholeWord => Options.MatchWholeWord;

    public bool IsCaseSensitive => Options.IsCaseSensitive;

    public string SpeakerText => Section.Speaker == ConversationSpeaker.User
        ? "You"
        : "Copilot";

    public string TimestampText => DisplayTextFormatter.FormatDateTime(Section.Timestamp);

    public string MessageText => $"Message {Section.MessageNumber:N0}";

    public string OccurrenceText => DisplayTextFormatter.FormatCount(
        Section.OccurrenceCount,
        "match",
        "matches");

    public string PreviewText => Section.PreviewText;

    public string DetailText => Section.DetailText;

    public string FullText => Section.FullText;

    public bool HasAdditionalText => Section.HasAdditionalText;
}
