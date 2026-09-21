#nullable enable

using CopilotSessionSearch.Models;
using CopilotSessionSearch.Services;
using GitHub.Copilot;

namespace CopilotSessionSearch.Tests;

public sealed class CopilotSdkSessionHistorySourceTests
{
    [Theory]
    [InlineData("ordinary-session", false, true)]
    [InlineData("ordinary-session", true, false)]
    [InlineData("copilot-session-search-ai-1234", false, false)]
    public void SessionFilterExcludesOnlyRemoteAndRankingSessions(
        string sessionId,
        bool isRemote,
        bool expected)
    {
        Assert.Equal(
            expected,
            CopilotSdkSessionHistorySource.ShouldIncludeSession(
                sessionId,
                isRemote));
    }

    [Fact]
    public void HistoryParserIncludesRootTaskCompletionSummary()
    {
        const string summary =
            "The investigation found one successful sample per week.";
        var entries = new List<ConversationEntry>();

        CopilotSdkSessionHistorySource.AddConversationEntries(
            [
                CreateTaskCompleteEvent(
                    summary,
                    agentId: null),
            ],
            entries);

        ConversationEntry entry = Assert.Single(entries);
        Assert.Equal(ConversationSpeaker.Copilot, entry.Speaker);
        Assert.Equal(summary, entry.Content);
    }

    [Fact]
    public void HistoryParserExcludesSubagentTaskCompletionSummary()
    {
        var entries = new List<ConversationEntry>();

        CopilotSdkSessionHistorySource.AddConversationEntries(
            [
                CreateTaskCompleteEvent(
                    "Internal subagent result.",
                    agentId: "subagent-id"),
            ],
            entries);

        Assert.Empty(entries);
    }

    [Fact]
    public void HistoryParserDoesNotDuplicateAdjacentCompletionSummary()
    {
        const string summary =
            "The investigation found one successful sample per week.";
        var entries = new List<ConversationEntry>
        {
            new(
                "assistant-message",
                ConversationSpeaker.Copilot,
                DateTimeOffset.Parse(
                    "2026-09-21T20:28:14Z"),
                summary),
        };

        CopilotSdkSessionHistorySource.AddConversationEntries(
            [
                CreateTaskCompleteEvent(
                    summary,
                    agentId: null),
            ],
            entries);

        ConversationEntry entry = Assert.Single(entries);
        Assert.Equal("assistant-message", entry.EventId);
    }

    private static SessionTaskCompleteEvent
        CreateTaskCompleteEvent(
            string summary,
            string? agentId)
    {
        return new SessionTaskCompleteEvent
        {
            AgentId = agentId,
            Data = new SessionTaskCompleteData
            {
                Success = true,
                Summary = summary,
            },
            Id = Guid.NewGuid(),
            ParentId = null,
            Timestamp = DateTimeOffset.Parse(
                "2026-09-21T20:28:15Z"),
        };
    }
}
