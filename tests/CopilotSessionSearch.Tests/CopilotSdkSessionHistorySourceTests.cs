#nullable enable

using CopilotSessionSearch.Services;

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
}
