#nullable enable

using System.Text.Json;
using CopilotSessionSearch.Models;
using CopilotSessionSearch.Services;
using HybridSearchSpike;

bool reuseIndex = args.Length > 0
    && string.Equals(
        args[0],
        "--reuse",
        StringComparison.OrdinalIgnoreCase);
int argumentOffset = reuseIndex ? 1 : 0;
string indexPath = args.Length > argumentOffset
    ? Path.GetFullPath(args[argumentOffset])
    : Path.Combine(
        Path.GetTempPath(),
        "CopilotSessionSearch-HybridSpike.sqlite");
string reportPath = args.Length > argumentOffset + 1
    ? Path.GetFullPath(args[argumentOffset + 1])
    : Path.Combine(
        Path.GetTempPath(),
        "CopilotSessionSearch-HybridSpike.json");
string[] queries = args.Length > argumentOffset + 2
    ? args[(argumentOffset + 2)..]
    :
    [
        "performance comparison between ClrMD 4.1 and 3.x",
        "earlier CPS NFE investigation for 18.8 updates",
    ];
using var cancellationSource = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellationSource.Cancel();
};

try
{
    Console.WriteLine(
        reuseIndex
            ? "Reconciling the existing persistent hybrid index..."
            : "Building the persistent hybrid index...");
    await using var historySource = new CopilotSdkSessionHistorySource();
    using var index = new PersistentHybridSearchIndex(indexPath);
    var progress = new Progress<HybridIndexProgress>(
        update =>
        {
            if (update.TotalSessions == 0
                || update.CompletedSessions % 25 == 0
                || update.CompletedSessions == update.TotalSessions)
            {
                Console.WriteLine(update.StatusText);
            }
        });
    HybridIndexMetrics metrics = await index.PrepareAsync(
        historySource,
        new SessionDocumentCache(),
        progress,
        cancellationSource.Token);
    Console.WriteLine(
        $"Index: {metrics.Blocks:N0} blocks, " +
        $"{metrics.Messages:N0} messages, " +
        $"{metrics.Sessions:N0} sessions, " +
        $"{metrics.DatabaseBytes / 1024d / 1024d:N1} MiB, " +
        $"{metrics.UpdatedSessions:N0} updated, " +
        $"{metrics.RemovedSessions:N0} removed, " +
        $"{metrics.UpdateTime.TotalSeconds:N1}s.");

    var queryResults = new List<QueryMetrics>();
    foreach (string query in queries)
    {
        cancellationSource.Token.ThrowIfCancellationRequested();
        Console.WriteLine();
        Console.WriteLine($"Query: {query}");
        HybridQueryResult localResult = index.Search(
            query,
            maximumResults: 30);
        QueryMetrics result = await AddCopilotRerankingAsync(
            query,
            localResult,
            cancellationSource.Token);
        queryResults.Add(result);
        Console.WriteLine(
            $"Latency: word={localResult.WordSearchTime.TotalMilliseconds:N1}ms, " +
            $"trigram={localResult.TrigramSearchTime.TotalMilliseconds:N1}ms, " +
            $"embedding={localResult.EmbeddingSearchTime.TotalMilliseconds:N1}ms, " +
            $"exact={localResult.ExactSearchTime.TotalMilliseconds:N1}ms, " +
            $"fusion={localResult.FusionTime.TotalMilliseconds:N1}ms.");
        PrintResults("BM25", localResult.WordResults);
        PrintResults("Embeddings", localResult.EmbeddingResults);
        PrintResults("Hybrid RRF", localResult.HybridResults);
        PrintCopilotResults(result.CopilotResults);
        if (result.CopilotUsage is AiUsageSummary usage)
        {
            Console.WriteLine(
                $"Copilot rerank usage: {usage.ApiCalls:N0} call(s), " +
                $"{usage.InputTokens:N0} input tokens, " +
                $"{usage.OutputTokens:N0} output tokens, " +
                $"{usage.AiCredits:N2} AI credits.");
        }
    }

    string? reportDirectory = Path.GetDirectoryName(reportPath);
    if (!string.IsNullOrWhiteSpace(reportDirectory))
    {
        Directory.CreateDirectory(reportDirectory);
    }

    await File.WriteAllTextAsync(
        reportPath,
        JsonSerializer.Serialize(
            new HybridSearchReport(metrics, queryResults),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true,
            }),
        cancellationSource.Token);
    Console.WriteLine();
    Console.WriteLine(
        $"Detailed local report written to {Path.GetFileName(reportPath)}.");
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Hybrid search spike canceled.");
    Environment.ExitCode = 2;
}
catch (Exception ex)
{
    Console.Error.WriteLine(
        $"Hybrid search spike failed: {ex.GetType().Name}: {ex.Message}");
    Environment.ExitCode = 1;
}

static void PrintResults(
    string label,
    IReadOnlyList<HybridRankedBlock> results)
{
    Console.WriteLine($"{label} top blocks:");
    foreach (HybridRankedBlock result in results.Take(10))
    {
        Console.WriteLine(
            $"  {result.Block.SessionId} " +
            $"message={result.Block.MessageNumber} " +
            $"chunk={result.Block.ChunkNumber} " +
            $"hybrid={result.HybridScore:F4} " +
            $"dense={result.EmbeddingSimilarity?.ToString("F3") ?? "-"}");
    }
}

static async Task<QueryMetrics> AddCopilotRerankingAsync(
    string query,
    HybridQueryResult localResult,
    CancellationToken cancellationToken)
{
    IReadOnlyList<HybridRankedBlock> localCandidates =
        localResult.HybridResults.Take(24).ToArray();
    AiSearchCandidate[] candidates = localCandidates
        .Select(
            (result, index) =>
            {
                HybridSearchBlock block = result.Block;
                int tableRows = block.Text
                    .Split('\n')
                    .Count(
                        line => line.Count(
                            character => character == '|') >= 2);
                return new AiSearchCandidate(
                    $"H{index + 1:D3}",
                    block.SessionId,
                    block.SessionName,
                    block.ModifiedTime,
                    result.HybridScore,
                    new AiLocalMatchSignals(
                        RequiredGroupsMatched: 0,
                        RequiredGroupsTotal: 0,
                        PreferredGroupsMatched: 0,
                        PreferredGroupsTotal: 0,
                        ExactPhrasesMatched: 0,
                        DistinctTermsMatched: 0,
                        QuantitativeSignals: 0,
                        MarkdownTableRows: tableRows),
                    [
                        new CandidateEvidence(
                            block.MessageNumber,
                            block.Speaker,
                            block.Text),
                    ]);
            })
        .ToArray();
    string repositoryRoot = Path.GetFullPath(
        Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));
    await using var aiSession = await CopilotAiSearchSession.CreateAsync(
        repositoryRoot,
        cancellationToken);
    List<AiRankingItem> rankings = await aiSession.RankAsync(
        query,
        candidates,
        cancellationToken);
    Dictionary<string, HybridRankedBlock> localByCandidateId = candidates
        .Select(
            (candidate, index) => (
                candidate.CandidateId,
                Result: localCandidates[index]))
        .ToDictionary(
            pair => pair.CandidateId,
            pair => pair.Result,
            StringComparer.Ordinal);
    IReadOnlyList<CopilotRankedBlock> copilotResults = rankings
        .Where(ranking => localByCandidateId.ContainsKey(ranking.CandidateId))
        .Select(
            ranking => new CopilotRankedBlock(
                localByCandidateId[ranking.CandidateId],
                ranking.Score,
                ranking.Confidence,
                ranking.Reason))
        .ToArray();
    return new QueryMetrics(
        localResult,
        copilotResults,
        aiSession.GetUsage());
}

static void PrintCopilotResults(
    IReadOnlyList<CopilotRankedBlock> results)
{
    Console.WriteLine("Hybrid + Copilot top blocks:");
    foreach (CopilotRankedBlock result in results.Take(10))
    {
        HybridSearchBlock block = result.LocalResult.Block;
        Console.WriteLine(
            $"  {result.Score,3} {block.SessionId} " +
            $"message={block.MessageNumber} chunk={block.ChunkNumber} " +
            $"confidence={result.Confidence}");
    }
}
