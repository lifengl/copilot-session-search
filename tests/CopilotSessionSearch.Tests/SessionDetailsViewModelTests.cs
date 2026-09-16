#nullable enable

using CopilotSessionSearch.Models;
using CopilotSessionSearch.Services;
using CopilotSessionSearch.ViewModels;

namespace CopilotSessionSearch.Tests;

public sealed class SessionDetailsViewModelTests
{
    [Fact]
    public void DetailsGroupDistantSectionsByMessageAndCopyFullText()
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
        Assert.Equal(3, viewModel.Messages[0].OccurrenceCount);

        viewModel.CopyMessageCommand.Execute(viewModel.Messages[0]);

        Assert.Equal(fullMessage, clipboardService.Text);
        Assert.Equal(
            "The full message was copied to the clipboard.",
            viewModel.StatusMessage);
        Assert.Null(viewModel.ErrorMessage);
    }

    private static SessionSearchResult CreateResult(IReadOnlyList<MatchSection> sections)
    {
        var descriptor = new SessionDescriptor(
            "session-id",
            "Session name",
            DateTimeOffset.Parse("2026-09-01T10:00:00Z"),
            DateTimeOffset.Parse("2026-09-01T11:00:00Z"),
            @"Q:\ws\project",
            "owner/repository",
            "main");
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
