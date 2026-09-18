#nullable enable

using System.Text.Json;
using AiSearchSpike;
using CopilotSessionSearch.Models;
using CopilotSessionSearch.Services;

const string DefaultQuery =
    "Find the earlier performance comparison between ClrMD 4.1 and 3.x";

string query = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0])
    ? args[0].Trim()
    : DefaultQuery;
string reportPath = args.Length > 1 && !string.IsNullOrWhiteSpace(args[1])
    ? Path.GetFullPath(args[1])
    : Path.Combine(
        Path.GetTempPath(),
        "CopilotSessionSearch-AiSearchSpike.json");
using var cancellationSource = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellationSource.Cancel();
};

try
{
    Console.WriteLine("Running synthetic retrieval self-check...");
    AiSearchSelfTest.Run();
    Console.WriteLine("Synthetic retrieval self-check passed.");

    await using var historySource = new CopilotSdkSessionHistorySource();
    IReadOnlyList<SessionDescriptor> sessions =
        await historySource.GetSessionsAsync(cancellationSource.Token);
    IReadOnlyList<SessionDescriptor> eligibleSessions =
        AiSearchPolicy.FilterEligibleSessions(
            sessions,
            DateTimeOffset.UtcNow);
    Console.WriteLine(
        $"Captured {sessions.Count:N0} local sessions; " +
        $"{eligibleSessions.Count:N0} are older than the AI-search recency cutoff.");

    string repositoryRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    await using var aiSession = await CopilotAiSearchSession.CreateAsync(
        repositoryRoot,
        cancellationSource.Token);

    Console.WriteLine("Asking Copilot for a lexical retrieval plan...");
    AiSearchPlan plan = await aiSession.CreatePlanAsync(
        query,
        cancellationSource.Token);
    Console.WriteLine(
        $"Plan: {plan.RequiredGroups.Count} required groups, " +
        $"{plan.PreferredGroups.Count} preferred groups, " +
        $"{plan.ExactPhrases.Count} exact phrases.");
    foreach (AiTermGroup group in plan.RequiredGroups)
    {
        Console.WriteLine(
            $"  required {group.Name}: {string.Join(" | ", group.AnyOf)}");
    }

    foreach (AiTermGroup group in plan.PreferredGroups)
    {
        Console.WriteLine(
            $"  preferred {group.Name}: {string.Join(" | ", group.AnyOf)}");
    }

    RetrievalResult retrieval = await LocalAiCandidateRetriever.RetrieveAsync(
        historySource,
        new SessionDocumentCache(),
        eligibleSessions,
        plan,
        progress: (completed, total, _) =>
        {
            if (completed % 100 == 0 || completed == total)
            {
                Console.WriteLine(
                    $"Locally scanned {completed:N0} of {total:N0} sessions.");
            }
        },
        cancellationSource.Token);
    Console.WriteLine(
        $"Local retrieval produced {retrieval.Candidates.Count:N0} bounded candidates " +
        $"({retrieval.StrictCandidateCount:N0} strict, " +
        $"{retrieval.SerializedCharacterCount:N0} serialized characters, " +
        $"{retrieval.FailedSessions:N0} read failures).");
    if (retrieval.UsedRelaxedRequirements)
    {
        Console.WriteLine(
            "Fewer than three strict candidates were found; local retrieval used relaxed ranking.");
    }

    List<AiRankingItem> rankings = retrieval.Candidates.Count == 0
        ? []
        : await aiSession.RankAsync(
            query,
            retrieval.Candidates,
            cancellationSource.Token);
    AiUsageSummary usage = aiSession.GetUsage();

    var report = new AiSearchSpikeReport
    {
        Plan = plan,
        Query = query,
        Rankings = rankings,
        Retrieval = retrieval,
        Usage = usage,
    };
    string? reportDirectory = Path.GetDirectoryName(reportPath);
    if (!string.IsNullOrWhiteSpace(reportDirectory))
    {
        Directory.CreateDirectory(reportDirectory);
    }

    await File.WriteAllTextAsync(
        reportPath,
        JsonSerializer.Serialize(
            report,
            AiSearchJson.SerializerOptions),
        cancellationSource.Token);

    Console.WriteLine(
        $"Copilot usage: {usage.ApiCalls:N0} calls, " +
        $"{usage.InputTokens:N0} input tokens, " +
        $"{usage.OutputTokens:N0} output tokens, " +
        $"{usage.AiCredits:N2} AI credits.");
    Console.WriteLine("Top ranked session IDs:");
    Dictionary<string, AiSearchCandidate> candidatesById = retrieval.Candidates
        .ToDictionary(
            candidate => candidate.CandidateId,
            StringComparer.Ordinal);
    foreach (AiRankingItem ranking in rankings)
    {
        AiSearchCandidate candidate = candidatesById[ranking.CandidateId];
        Console.WriteLine(
            $"  {ranking.Score,3} {candidate.SessionId} " +
            $"messages=[{string.Join(",", ranking.MessageNumbers)}] " +
            $"confidence={ranking.Confidence}");
    }

    Console.WriteLine(
        $"Detailed local report written to {Path.GetFileName(reportPath)}.");
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("AI search spike canceled.");
    Environment.ExitCode = 2;
}
catch (Exception ex)
{
    Console.Error.WriteLine(
        $"AI search spike failed: {ex.GetType().Name}: {ex.Message}");
    Environment.ExitCode = 1;
}
