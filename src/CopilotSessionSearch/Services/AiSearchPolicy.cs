#nullable enable

using CopilotSessionSearch.Models;

namespace CopilotSessionSearch.Services;

public static class AiSearchPolicy
{
    public static TimeSpan RecentSessionExclusion { get; } =
        TimeSpan.FromHours(1);

    public static IReadOnlyList<SessionDescriptor> FilterEligibleSessions(
        IEnumerable<SessionDescriptor> sessions,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        DateTimeOffset cutoff = now - RecentSessionExclusion;
        return sessions
            .Where(session => session.ModifiedTime < cutoff)
            .ToArray();
    }
}
