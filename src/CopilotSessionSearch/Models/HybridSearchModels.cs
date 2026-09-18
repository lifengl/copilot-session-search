#nullable enable

using System.Text.Json.Serialization;

namespace CopilotSessionSearch.Models;

public sealed record HybridSearchBlock(
    long Id,
    string SessionId,
    string SessionName,
    DateTimeOffset ModifiedTime,
    int MessageNumber,
    int ChunkNumber,
    string Speaker,
    string Text,
    [property: JsonIgnore] string RetrievalText,
    [property: JsonIgnore] byte[] Embedding);

public sealed record HybridRankedBlock(
    HybridSearchBlock Block,
    double HybridScore,
    int? WordRank,
    int? TrigramRank,
    int? EmbeddingRank,
    int? ExactRank,
    double? EmbeddingSimilarity,
    double ExactScore);

public sealed record HybridIndexProgress(
    int CompletedSessions,
    int TotalSessions,
    string StatusText);

public sealed record HybridIndexMetrics(
    int Sessions,
    int Messages,
    int Blocks,
    long EmbeddingBytes,
    long DatabaseBytes,
    int UpdatedSessions,
    int RemovedSessions,
    IReadOnlyList<SessionSearchFailure> Failures,
    TimeSpan UpdateTime);

public sealed record HybridQueryResult(
    string Query,
    TimeSpan WordSearchTime,
    TimeSpan TrigramSearchTime,
    TimeSpan EmbeddingSearchTime,
    TimeSpan ExactSearchTime,
    TimeSpan FusionTime,
    IReadOnlyList<HybridRankedBlock> WordResults,
    IReadOnlyList<HybridRankedBlock> EmbeddingResults,
    IReadOnlyList<HybridRankedBlock> HybridResults);
