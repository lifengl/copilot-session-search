#nullable enable

using CopilotSessionSearch.Models;
using CopilotSessionSearch.Services;
using CopilotSessionSearch.ViewModels;

namespace CopilotSessionSearch.Tests;

public sealed class SessionDetailsViewModelTests
{
    [Fact]
    public void DetailsGroupDistantSectionsByMessageAndCopySessionInfo()
    {
        const string fullMessage = "## Heading\n\nA message containing needle in two places.";
        SessionSearchResult result = CreateResult(
            [
                new MatchSection(
                    "event-1",
                    1,
                    ConversationSpeaker.Copilot,
                    DateTimeOffset.Parse("2026-09-01T10:00:00Z"),
                    "needle",
                    "first needle",
                    fullMessage,
                    1,
                    true),
                new MatchSection(
                    "event-1",
                    1,
                    ConversationSpeaker.Copilot,
                    DateTimeOffset.Parse("2026-09-01T10:00:00Z"),
                    "needle",
                    "second needle",
                    fullMessage,
                    2,
                    true),
                new MatchSection(
                    "event-2",
                    2,
                    ConversationSpeaker.User,
                    DateTimeOffset.Parse("2026-09-01T10:01:00Z"),
                    "needle",
                    "another needle",
                    "Another needle.",
                    1,
                    false),
            ]);
        var clipboardService = new RecordingClipboardService();
        var viewModel = new SessionDetailsViewModel(
            result,
            new NoOpConsoleLauncher(),
            clipboardService);

        Assert.Equal(2, viewModel.Messages.Count);
        Assert.Same(viewModel.Messages[0], viewModel.SelectedMessage);
        Assert.Equal(3, viewModel.Messages[0].OccurrenceCount);
        Assert.Equal("copilot --resume=session-id", viewModel.ResumeCommandText);
        Assert.Contains("Esc close", viewModel.StatusBarText, StringComparison.Ordinal);

        viewModel.CopySessionInfoCommand.Execute(null);

        Assert.Contains("Session ID: session-id", clipboardService.Text, StringComparison.Ordinal);
        Assert.Contains(
            @"Working directory: Q:\ws\project",
            clipboardService.Text,
            StringComparison.Ordinal);
        Assert.Contains(
            "Resume command: copilot --resume=session-id",
            clipboardService.Text,
            StringComparison.Ordinal);
        Assert.Equal(
            "Session information was copied to the clipboard.",
            viewModel.StatusBarText);

        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public void ConversationViewsPreserveSelectionOrChooseNearestMatch()
    {
        SessionDescriptor descriptor = CreateDescriptor();
        ConversationEntry[] entries = Enumerable.Range(1, 5)
            .Select(
                messageNumber => new ConversationEntry(
                    $"event-{messageNumber}",
                    messageNumber % 2 == 0
                        ? ConversationSpeaker.User
                        : ConversationSpeaker.Copilot,
                    descriptor.StartTime.AddMinutes(messageNumber),
                    $"Message {messageNumber} content."))
            .ToArray();
        MatchSection[] sections =
        [
            CreateSection(entries[1], messageNumber: 2),
            CreateSection(entries[4], messageNumber: 5),
        ];
        var result = new SessionSearchResult(
            new SessionDocument(descriptor, entries),
            "content",
            sections,
            matchCount: 2);
        var viewModel = new SessionDetailsViewModel(
            result,
            new NoOpConsoleLauncher(),
            new RecordingClipboardService());
        int viewChangeCount = 0;
        viewModel.MessageViewChanged += () => viewChangeCount++;

        Assert.True(viewModel.IsShowingMatchingMessages);
        Assert.False(viewModel.IsShowingWholeConversation);
        Assert.Equal("Matching messages", viewModel.MessageViewButtonText);
        Assert.Equal([2, 5], viewModel.Messages.Select(message => message.MessageNumber));

        SessionDetailMessageViewModel matchingMessage = viewModel.Messages[1];
        viewModel.SelectedMessage = matchingMessage;
        viewModel.ToggleMessageViewCommand.Execute(null);

        Assert.True(viewModel.IsShowingWholeConversation);
        Assert.Equal("Whole conversation", viewModel.MessageViewButtonText);
        Assert.Equal(5, viewModel.Messages.Count);
        Assert.Equal(0, viewModel.Messages[0].OccurrenceCount);
        Assert.Same(matchingMessage, viewModel.SelectedMessage);

        viewModel.SelectedMessage = viewModel.Messages[3];
        viewModel.ToggleMessageViewCommand.Execute(null);

        Assert.True(viewModel.IsShowingMatchingMessages);
        Assert.Equal([2, 5], viewModel.Messages.Select(message => message.MessageNumber));
        Assert.Equal(5, viewModel.SelectedMessage?.MessageNumber);

        viewModel.ToggleMessageViewCommand.Execute(null);
        viewModel.SelectedMessage = viewModel.Messages[2];
        viewModel.ToggleMessageViewCommand.Execute(null);

        Assert.Equal(2, viewModel.SelectedMessage?.MessageNumber);
        Assert.Equal(4, viewChangeCount);
    }

    [Fact]
    public void SessionInformationExpansionIsLocalToTheDetailView()
    {
        SessionSearchResult result = CreateResult(
            [
                new MatchSection(
                    "event-1",
                    1,
                    ConversationSpeaker.Copilot,
                    DateTimeOffset.Parse("2026-09-01T10:00:00Z"),
                    "needle",
                    "needle",
                    "A message containing needle.",
                    1,
                    false),
            ]);
        var viewModel = new SessionDetailsViewModel(
            result,
            new NoOpConsoleLauncher(),
            new RecordingClipboardService());
        int viewChangeCount = 0;
        viewModel.MessageViewChanged += () => viewChangeCount++;

        Assert.True(viewModel.IsSessionInfoExpanded);
        Assert.Equal("Collapse session information", viewModel.SessionInfoButtonText);

        viewModel.ToggleSessionInfoCommand.Execute(null);

        Assert.False(viewModel.IsSessionInfoExpanded);
        Assert.Equal("Expand session information", viewModel.SessionInfoButtonText);

        viewModel.ToggleSessionInfoCommand.Execute(null);

        Assert.True(viewModel.IsSessionInfoExpanded);
        Assert.Equal(2, viewChangeCount);
    }

    private static SessionSearchResult CreateResult(IReadOnlyList<MatchSection> sections)
    {
        SessionDescriptor descriptor = CreateDescriptor();
        var document = new SessionDocument(
            descriptor,
            [
                new ConversationEntry(
                    "event-1",
                    ConversationSpeaker.Copilot,
                    descriptor.StartTime,
                    sections[0].FullText),
                new ConversationEntry(
                    "event-2",
                    ConversationSpeaker.User,
                    descriptor.StartTime.AddMinutes(1),
                    sections[^1].FullText),
            ]);

        return new SessionSearchResult(
            document,
            "needle",
            sections,
            sections.Sum(section => section.OccurrenceCount));
    }

    private static SessionDescriptor CreateDescriptor()
    {
        return new SessionDescriptor(
            "session-id",
            "Session name",
            DateTimeOffset.Parse("2026-09-01T10:00:00Z"),
            DateTimeOffset.Parse("2026-09-01T11:00:00Z"),
            @"Q:\ws\project",
            "owner/repository",
            "main");
    }

    private static MatchSection CreateSection(
        ConversationEntry entry,
        int messageNumber)
    {
        return new MatchSection(
            entry.EventId,
            messageNumber,
            entry.Speaker,
            entry.Timestamp,
            entry.Content,
            entry.Content,
            entry.Content,
            OccurrenceCount: 1,
            HasAdditionalText: false);
    }

    private sealed class RecordingClipboardService : IClipboardService
    {
        public string? Text { get; private set; }

        public void SetText(string text)
        {
            Text = text;
        }
    }

    private sealed class NoOpConsoleLauncher : IConsoleLauncher
    {
        public void ResumeSession(SessionDescriptor session)
        {
        }
    }
}
