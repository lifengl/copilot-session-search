#nullable enable

using CopilotSessionSearch.Models;
using CopilotSessionSearch.Services;

namespace CopilotSessionSearch.Tests;

public sealed class AiSearchPolicyTests
{
    [Fact]
    public void FilterEligibleSessionsExcludesRecentlyActiveSessions()
    {
        DateTimeOffset now = DateTimeOffset.Parse(
            "2026-09-17T16:00:00Z");
        SessionDescriptor oldSession = CreateDescriptor(
            "old",
            now.AddHours(-2));
        SessionDescriptor recentSession = CreateDescriptor(
            "recent",
            now.AddMinutes(-30));

        IReadOnlyList<SessionDescriptor> eligible =
            AiSearchPolicy.FilterEligibleSessions(
                [oldSession, recentSession],
                now);

        SessionDescriptor result = Assert.Single(eligible);
        Assert.Same(oldSession, result);
    }

    private static SessionDescriptor CreateDescriptor(
        string id,
        DateTimeOffset modifiedTime)
    {
        return new SessionDescriptor(
            id,
            $"Session {id}",
            modifiedTime.AddHours(-1),
            modifiedTime,
            null,
            null,
            null);
    }
}
