#nullable enable

using System.Diagnostics;
using System.Globalization;
using System.IO;
using CopilotSessionSearch.Models;
using Microsoft.Data.Sqlite;
using SmartComponents.LocalEmbeddings;

namespace CopilotSessionSearch.Services;

public sealed class PersistentHybridSearchIndex : IHybridSearchIndex
{
    private const string SchemaVersion = "1";
    private const string ParserVersion = "visible-messages-v1";
    private const string ChunkerVersion = "message-markdown-v1";
    private const string RetrievalTextVersion = "neighbors-v1";
    private const string FtsVersion = "unicode61-trigram-v1";
    private const string EmbeddingModelVersion =
        "bge-micro-v2@72908b7";
    private const string EmbeddingFormatVersion = "i8-384-v1";

    private readonly string _databasePath;
    private readonly SemaphoreSlim _preparationGate = new(1, 1);
    private LocalEmbedder? _embedder;
    private IReadOnlyDictionary<long, HybridSearchBlock> _blocks =
        new Dictionary<long, HybridSearchBlock>();
    private bool _isReady;
    private bool _disposed;

    public PersistentHybridSearchIndex(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        _databasePath = Path.GetFullPath(databasePath);
    }

    public bool IsReady => _isReady;

    public static string GetDefaultDatabasePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "CopilotSessionSearch",
            "ai-search-index-v1.sqlite");
    }

    public async Task<HybridIndexMetrics> PrepareAsync(
        ISessionHistorySource historySource,
        SessionDocumentCache documentCache,
        IProgress<HybridIndexProgress>? progress,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(historySource);
        ArgumentNullException.ThrowIfNull(documentCache);

        await _preparationGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            IReadOnlyList<SessionDescriptor> availableSessions =
                await historySource
                    .GetSessionsAsync(cancellationToken)
                    .ConfigureAwait(false);
            IReadOnlyList<SessionDescriptor> sessions =
                AiSearchPolicy.FilterEligibleSessions(
                    availableSessions,
                    DateTimeOffset.UtcNow);
            var stopwatch = Stopwatch.StartNew();
            string? databaseDirectory = Path.GetDirectoryName(_databasePath);
            if (!string.IsNullOrWhiteSpace(databaseDirectory))
            {
                Directory.CreateDirectory(databaseDirectory);
            }

            using SqliteConnection connection =
                OpenValidatedDatabase();
            Dictionary<string, IndexedSession> indexedSessions =
                LoadIndexedSessions(connection);
            Dictionary<string, SessionDescriptor> sessionsById =
                sessions.ToDictionary(
                    session => session.SessionId,
                    StringComparer.Ordinal);
            string[] removedSessionIds = indexedSessions.Keys
                .Where(sessionId => !sessionsById.ContainsKey(sessionId))
                .ToArray();
            SessionDescriptor[] sessionsToUpdate = sessions
                .Where(
                    session =>
                        !indexedSessions.TryGetValue(
                            session.SessionId,
                            out IndexedSession? indexed)
                        || indexed.ModifiedTime != session.ModifiedTime
                        || !string.Equals(
                            indexed.Name,
                            session.Name,
                            StringComparison.Ordinal))
                .ToArray();
            int totalOperations =
                removedSessionIds.Length + sessionsToUpdate.Length;
            int completedOperations = 0;
            var failures = new List<SessionSearchFailure>();

            progress?.Report(
                new HybridIndexProgress(
                    completedOperations,
                    totalOperations,
                    totalOperations == 0
                        ? "Local AI index is up to date."
                        : $"Updating local AI index for {totalOperations:N0} session(s)..."));

            foreach (string sessionId in removedSessionIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DeleteSession(connection, sessionId);
                completedOperations++;
                ReportProgress(
                    progress,
                    completedOperations,
                    totalOperations);
            }

            foreach (SessionDescriptor session in sessionsToUpdate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    SessionDocument document = await documentCache
                        .GetOrLoadAsync(
                            session,
                            historySource.GetSessionDocumentAsync,
                            cancellationToken)
                        .ConfigureAwait(false);
                    await Task.Run(
                        () => IndexSession(
                            connection,
                            document,
                            cancellationToken),
                        cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failures.Add(
                        new SessionSearchFailure(
                            session,
                            ex.Message));
                }

                completedOperations++;
                ReportProgress(
                    progress,
                    completedOperations,
                    totalOperations);
            }

            _blocks = await Task.Run(
                () => LoadBlocks(connection),
                cancellationToken).ConfigureAwait(false);
            _isReady = true;
            stopwatch.Stop();
            connection.Close();

            long embeddingBytes = _blocks.Values.Sum(
                block => (long)block.Embedding.Length);
            long databaseBytes = File.Exists(_databasePath)
                ? new FileInfo(_databasePath).Length
                : 0;
            HybridIndexMetrics metrics = new(
                sessions.Count,
                _blocks.Values
                    .Select(
                        block => (
                            block.SessionId,
                            block.MessageNumber))
                    .Distinct()
                    .Count(),
                _blocks.Count,
                embeddingBytes,
                databaseBytes,
                sessionsToUpdate.Length,
                removedSessionIds.Length,
                failures,
                stopwatch.Elapsed);
            progress?.Report(
                new HybridIndexProgress(
                    totalOperations,
                    totalOperations,
                    $"Local AI index ready: {metrics.Blocks:N0} blocks."));
            return metrics;
        }
        catch
        {
            _isReady = false;
            throw;
        }
        finally
        {
            _preparationGate.Release();
        }
    }

    public HybridQueryResult Search(
        string query,
        int maximumResults = 30)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        if (!_isReady)
        {
            throw new InvalidOperationException(
                "The local AI search index is not ready.");
        }

        IReadOnlyList<string> terms = HybridSearchQuery.ExtractTerms(query);
        var stopwatch = Stopwatch.StartNew();
        IReadOnlyList<ChannelHit> wordHits = SearchFts(
            "blocks_word",
            HybridSearchQuery.CreateMatchExpression(terms),
            maximumResults: 150);
        stopwatch.Stop();
        TimeSpan wordTime = stopwatch.Elapsed;

        stopwatch.Restart();
        IReadOnlyList<string> trigramTerms = terms
            .Where(term => term.Length >= 3)
            .ToArray();
        IReadOnlyList<ChannelHit> trigramHits = trigramTerms.Count == 0
            ? []
            : SearchFts(
                "blocks_trigram",
                HybridSearchQuery.CreateMatchExpression(trigramTerms),
                maximumResults: 150);
        stopwatch.Stop();
        TimeSpan trigramTime = stopwatch.Elapsed;

        stopwatch.Restart();
        EmbeddingI8[] queryEmbeddings = HybridSearchQuery
            .CreateSemanticQueries(query)
            .Select(
                semanticQuery =>
                    GetEmbedder().Embed<EmbeddingI8>(semanticQuery))
            .ToArray();
        IReadOnlyList<ChannelHit> embeddingHits = _blocks.Values
            .Select(
                block => new ChannelHit(
                    block.Id,
                    queryEmbeddings.Max(
                        queryEmbedding =>
                            new EmbeddingI8(block.Embedding)
                                .Similarity(queryEmbedding))))
            .OrderByDescending(hit => hit.Score)
            .Take(150)
            .ToArray();
        stopwatch.Stop();
        TimeSpan embeddingTime = stopwatch.Elapsed;

        stopwatch.Restart();
        IReadOnlyList<ChannelHit> exactHits = _blocks.Values
            .Select(
                block => new ChannelHit(
                    block.Id,
                    HybridSearchQuery.ScoreExact(
                        query,
                        terms,
                        block)))
            .Where(hit => hit.Score > 0)
            .OrderByDescending(hit => hit.Score)
            .Take(150)
            .ToArray();
        stopwatch.Stop();
        TimeSpan exactTime = stopwatch.Elapsed;

        stopwatch.Restart();
        IReadOnlyList<HybridRankedBlock> hybrid = Fuse(
            wordHits,
            trigramHits,
            embeddingHits,
            exactHits,
            maximumResults);
        stopwatch.Stop();

        return new HybridQueryResult(
            query,
            wordTime,
            trigramTime,
            embeddingTime,
            exactTime,
            stopwatch.Elapsed,
            CreateSingleChannelResults(wordHits, Channel.Word),
            CreateSingleChannelResults(
                embeddingHits,
                Channel.Embedding),
            hybrid);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _embedder?.Dispose();
        _embedder = null;
        _preparationGate.Dispose();
    }

    private SqliteConnection OpenValidatedDatabase()
    {
        SqliteConnection connection = OpenWritableDatabase();
        CreateSchema(connection);
        Dictionary<string, string> metadata = LoadMetadata(connection);
        if (metadata.Count == 0)
        {
            WriteMetadata(connection);
            return connection;
        }

        if (HasExpectedVersions(metadata))
        {
            return connection;
        }

        connection.Dispose();
        DeleteDatabaseFiles();
        connection = OpenWritableDatabase();
        CreateSchema(connection);
        WriteMetadata(connection);
        return connection;
    }

    private SqliteConnection OpenWritableDatabase()
    {
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = _databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
            }.ToString());
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            PRAGMA temp_store = MEMORY;
            """;
        command.ExecuteNonQuery();
        return connection;
    }

    private IReadOnlyList<ChannelHit> SearchFts(
        string tableName,
        string matchExpression,
        int maximumResults)
    {
        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = _databasePath,
                Mode = SqliteOpenMode.ReadOnly,
            }.ToString());
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            $"SELECT rowid, bm25({tableName}, 0.5, 1.0) " +
            $"FROM {tableName} " +
            $"WHERE {tableName} MATCH $query " +
            "ORDER BY 2 " +
            "LIMIT $limit;";
        command.Parameters.AddWithValue("$query", matchExpression);
        command.Parameters.AddWithValue("$limit", maximumResults);
        using SqliteDataReader reader = command.ExecuteReader();
        var hits = new List<ChannelHit>();
        while (reader.Read())
        {
            hits.Add(
                new ChannelHit(
                    reader.GetInt64(0),
                    -reader.GetDouble(1)));
        }

        return hits;
    }

    private IReadOnlyList<HybridRankedBlock> Fuse(
        IReadOnlyList<ChannelHit> wordHits,
        IReadOnlyList<ChannelHit> trigramHits,
        IReadOnlyList<ChannelHit> embeddingHits,
        IReadOnlyList<ChannelHit> exactHits,
        int maximumResults)
    {
        const double RrfK = 60;
        var scores = new Dictionary<long, MutableRank>();

        AddChannel(wordHits, Channel.Word, weight: 1.1);
        AddChannel(trigramHits, Channel.Trigram, weight: 0.8);
        AddChannel(embeddingHits, Channel.Embedding, weight: 1.2);
        AddChannel(exactHits, Channel.Exact, weight: 1.0);

        IReadOnlyList<HybridRankedBlock> ordered = scores
            .Select(
                pair =>
                {
                    HybridSearchBlock block = _blocks[pair.Key];
                    MutableRank rank = pair.Value;
                    return new HybridRankedBlock(
                        block,
                        rank.Score,
                        rank.WordRank,
                        rank.TrigramRank,
                        rank.EmbeddingRank,
                        rank.ExactRank,
                        rank.EmbeddingSimilarity,
                        rank.ExactScore);
                })
            .OrderByDescending(result => result.HybridScore)
            .ThenByDescending(result => result.Block.ModifiedTime)
            .ToArray();
        var sessionCounts = new Dictionary<string, int>(
            StringComparer.Ordinal);
        var results = new List<HybridRankedBlock>();

        foreach (HybridRankedBlock result in ordered)
        {
            int sessionCount = sessionCounts.GetValueOrDefault(
                result.Block.SessionId);
            if (sessionCount == 5)
            {
                continue;
            }

            sessionCounts[result.Block.SessionId] = sessionCount + 1;
            results.Add(result);
            if (results.Count == maximumResults)
            {
                break;
            }
        }

        return results;

        void AddChannel(
            IReadOnlyList<ChannelHit> hits,
            Channel channel,
            double weight)
        {
            for (int index = 0; index < hits.Count; index++)
            {
                ChannelHit hit = hits[index];
                if (!scores.TryGetValue(
                    hit.BlockId,
                    out MutableRank? rank))
                {
                    rank = new MutableRank();
                    scores.Add(hit.BlockId, rank);
                }

                int oneBasedRank = index + 1;
                rank.Score += weight / (RrfK + oneBasedRank);
                rank.SetChannel(
                    channel,
                    oneBasedRank,
                    hit.Score);
            }
        }
    }

    private IReadOnlyList<HybridRankedBlock> CreateSingleChannelResults(
        IReadOnlyList<ChannelHit> hits,
        Channel channel)
    {
        return hits
            .Take(15)
            .Select(
                (hit, index) =>
                {
                    HybridSearchBlock block = _blocks[hit.BlockId];
                    int rank = index + 1;
                    return new HybridRankedBlock(
                        block,
                        hit.Score,
                        channel == Channel.Word ? rank : null,
                        channel == Channel.Trigram ? rank : null,
                        channel == Channel.Embedding ? rank : null,
                        channel == Channel.Exact ? rank : null,
                        channel == Channel.Embedding
                            ? hit.Score
                            : null,
                        channel == Channel.Exact ? hit.Score : 0);
                })
            .ToArray();
    }

    private void IndexSession(
        SqliteConnection connection,
        SessionDocument document,
        CancellationToken cancellationToken)
    {
        using SqliteTransaction transaction = connection.BeginTransaction();
        DeleteSession(
            connection,
            document.Session.SessionId,
            transaction);
        using SqliteCommand insertBlock = CreateInsertBlockCommand(
            connection,
            transaction);
        using SqliteCommand insertWord = CreateInsertFtsCommand(
            connection,
            transaction,
            "blocks_word");
        using SqliteCommand insertTrigram = CreateInsertFtsCommand(
            connection,
            transaction,
            "blocks_trigram");

        foreach (HybridConversationChunker.PendingBlock pending
            in HybridConversationChunker.CreateBlocks(document))
        {
            cancellationToken.ThrowIfCancellationRequested();

            EmbeddingI8 embedding = GetEmbedder().Embed<EmbeddingI8>(
                pending.RetrievalText);
            long blockId = InsertBlock(
                insertBlock,
                pending,
                embedding.Buffer.ToArray());
            InsertFts(insertWord, blockId, pending);
            InsertFts(insertTrigram, blockId, pending);
        }

        using SqliteCommand upsert = connection.CreateCommand();
        upsert.Transaction = transaction;
        upsert.CommandText =
            """
            INSERT INTO indexed_sessions (
                session_id,
                name,
                modified_time)
            VALUES (
                $session_id,
                $name,
                $modified_time)
            ON CONFLICT(session_id) DO UPDATE SET
                name = excluded.name,
                modified_time = excluded.modified_time;
            """;
        upsert.Parameters.AddWithValue(
            "$session_id",
            document.Session.SessionId);
        upsert.Parameters.AddWithValue(
            "$name",
            document.Session.Name);
        upsert.Parameters.AddWithValue(
            "$modified_time",
            document.Session.ModifiedTime.ToString("O"));
        upsert.ExecuteNonQuery();
        transaction.Commit();
    }

    private static long InsertBlock(
        SqliteCommand command,
        HybridConversationChunker.PendingBlock pending,
        byte[] embedding)
    {
        SetParameter(command, "$session_id", pending.Session.SessionId);
        SetParameter(command, "$session_name", pending.Session.Name);
        SetParameter(
            command,
            "$modified_time",
            pending.Session.ModifiedTime.ToString("O"));
        SetParameter(command, "$message_number", pending.MessageNumber);
        SetParameter(command, "$chunk_number", pending.ChunkNumber);
        SetParameter(command, "$speaker", pending.Speaker);
        SetParameter(command, "$body", pending.Text);
        SetParameter(
            command,
            "$retrieval_text",
            pending.RetrievalText);
        SetParameter(command, "$embedding", embedding);
        return (long)(command.ExecuteScalar()
            ?? throw new InvalidOperationException(
                "SQLite did not return the inserted block ID."));
    }

    private static void InsertFts(
        SqliteCommand command,
        long blockId,
        HybridConversationChunker.PendingBlock pending)
    {
        SetParameter(command, "$id", blockId);
        SetParameter(command, "$session_name", pending.Session.Name);
        SetParameter(command, "$body", pending.Text);
        command.ExecuteNonQuery();
    }

    private static void DeleteSession(
        SqliteConnection connection,
        string sessionId,
        SqliteTransaction? transaction = null)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            DELETE FROM blocks_word
            WHERE rowid IN (
                SELECT id
                FROM blocks
                WHERE session_id = $session_id);
            DELETE FROM blocks_trigram
            WHERE rowid IN (
                SELECT id
                FROM blocks
                WHERE session_id = $session_id);
            DELETE FROM blocks
            WHERE session_id = $session_id;
            DELETE FROM indexed_sessions
            WHERE session_id = $session_id;
            """;
        command.Parameters.AddWithValue("$session_id", sessionId);
        command.ExecuteNonQuery();
    }

    private static Dictionary<string, IndexedSession> LoadIndexedSessions(
        SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                session_id,
                name,
                modified_time
            FROM indexed_sessions;
            """;
        using SqliteDataReader reader = command.ExecuteReader();
        var sessions = new Dictionary<string, IndexedSession>(
            StringComparer.Ordinal);
        while (reader.Read())
        {
            string sessionId = reader.GetString(0);
            sessions.Add(
                sessionId,
                new IndexedSession(
                    sessionId,
                    reader.GetString(1),
                    DateTimeOffset.Parse(
                        reader.GetString(2),
                        CultureInfo.InvariantCulture)));
        }

        return sessions;
    }

    private static IReadOnlyDictionary<long, HybridSearchBlock> LoadBlocks(
        SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                id,
                session_id,
                session_name,
                modified_time,
                message_number,
                chunk_number,
                speaker,
                body,
                retrieval_text,
                embedding
            FROM blocks;
            """;
        using SqliteDataReader reader = command.ExecuteReader();
        var blocks = new Dictionary<long, HybridSearchBlock>();
        while (reader.Read())
        {
            long id = reader.GetInt64(0);
            blocks.Add(
                id,
                new HybridSearchBlock(
                    id,
                    reader.GetString(1),
                    reader.GetString(2),
                    DateTimeOffset.Parse(
                        reader.GetString(3),
                        CultureInfo.InvariantCulture),
                    reader.GetInt32(4),
                    reader.GetInt32(5),
                    reader.GetString(6),
                    reader.GetString(7),
                    reader.GetString(8),
                    (byte[])reader[9]));
        }

        return blocks;
    }

    private static Dictionary<string, string> LoadMetadata(
        SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT key, value
            FROM metadata;
            """;
        using SqliteDataReader reader = command.ExecuteReader();
        var metadata = new Dictionary<string, string>(
            StringComparer.Ordinal);
        while (reader.Read())
        {
            metadata.Add(
                reader.GetString(0),
                reader.GetString(1));
        }

        return metadata;
    }

    private static bool HasExpectedVersions(
        IReadOnlyDictionary<string, string> metadata)
    {
        return HasValue("schema", SchemaVersion)
            && HasValue("parser", ParserVersion)
            && HasValue("chunker", ChunkerVersion)
            && HasValue("retrieval_text", RetrievalTextVersion)
            && HasValue("fts", FtsVersion)
            && HasValue("embedding_model", EmbeddingModelVersion)
            && HasValue("embedding_format", EmbeddingFormatVersion);

        bool HasValue(string key, string expected)
        {
            return metadata.TryGetValue(key, out string? value)
                && string.Equals(
                    value,
                    expected,
                    StringComparison.Ordinal);
        }
    }

    private static void WriteMetadata(SqliteConnection connection)
    {
        KeyValuePair<string, string>[] values =
        [
            new("schema", SchemaVersion),
            new("parser", ParserVersion),
            new("chunker", ChunkerVersion),
            new("retrieval_text", RetrievalTextVersion),
            new("fts", FtsVersion),
            new("embedding_model", EmbeddingModelVersion),
            new("embedding_format", EmbeddingFormatVersion),
        ];
        using SqliteTransaction transaction = connection.BeginTransaction();
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO metadata (key, value)
            VALUES ($key, $value);
            """;
        command.Parameters.Add("$key", SqliteType.Text);
        command.Parameters.Add("$value", SqliteType.Text);

        foreach (KeyValuePair<string, string> value in values)
        {
            SetParameter(command, "$key", value.Key);
            SetParameter(command, "$value", value.Value);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static void CreateSchema(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS metadata (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS indexed_sessions (
                session_id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                modified_time TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS blocks (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id TEXT NOT NULL,
                session_name TEXT NOT NULL,
                modified_time TEXT NOT NULL,
                message_number INTEGER NOT NULL,
                chunk_number INTEGER NOT NULL,
                speaker TEXT NOT NULL,
                body TEXT NOT NULL,
                retrieval_text TEXT NOT NULL,
                embedding BLOB NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_blocks_session_id
            ON blocks(session_id);
            CREATE VIRTUAL TABLE IF NOT EXISTS blocks_word USING fts5(
                session_name,
                body,
                tokenize = 'unicode61 remove_diacritics 2'
            );
            CREATE VIRTUAL TABLE IF NOT EXISTS blocks_trigram USING fts5(
                session_name,
                body,
                tokenize = 'trigram'
            );
            """;
        command.ExecuteNonQuery();
    }

    private static SqliteCommand CreateInsertBlockCommand(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO blocks (
                session_id,
                session_name,
                modified_time,
                message_number,
                chunk_number,
                speaker,
                body,
                retrieval_text,
                embedding)
            VALUES (
                $session_id,
                $session_name,
                $modified_time,
                $message_number,
                $chunk_number,
                $speaker,
                $body,
                $retrieval_text,
                $embedding);
            SELECT last_insert_rowid();
            """;
        command.Parameters.Add("$session_id", SqliteType.Text);
        command.Parameters.Add("$session_name", SqliteType.Text);
        command.Parameters.Add("$modified_time", SqliteType.Text);
        command.Parameters.Add("$message_number", SqliteType.Integer);
        command.Parameters.Add("$chunk_number", SqliteType.Integer);
        command.Parameters.Add("$speaker", SqliteType.Text);
        command.Parameters.Add("$body", SqliteType.Text);
        command.Parameters.Add("$retrieval_text", SqliteType.Text);
        command.Parameters.Add("$embedding", SqliteType.Blob);
        return command;
    }

    private static SqliteCommand CreateInsertFtsCommand(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName)
    {
        SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"INSERT INTO {tableName} (rowid, session_name, body) " +
            "VALUES ($id, $session_name, $body);";
        command.Parameters.Add("$id", SqliteType.Integer);
        command.Parameters.Add("$session_name", SqliteType.Text);
        command.Parameters.Add("$body", SqliteType.Text);
        return command;
    }

    private static void SetParameter(
        SqliteCommand command,
        string name,
        object value)
    {
        command.Parameters[name].Value = value;
    }

    private LocalEmbedder GetEmbedder()
    {
        return _embedder ??= new LocalEmbedder();
    }

    private void DeleteDatabaseFiles()
    {
        foreach (string path in new[]
        {
            _databasePath,
            _databasePath + "-shm",
            _databasePath + "-wal",
        })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static void ReportProgress(
        IProgress<HybridIndexProgress>? progress,
        int completed,
        int total)
    {
        progress?.Report(
            new HybridIndexProgress(
                completed,
                total,
                $"Updating local AI index: {completed:N0} of {total:N0} session(s)..."));
    }

    private sealed record ChannelHit(long BlockId, double Score);

    private sealed record IndexedSession(
        string SessionId,
        string Name,
        DateTimeOffset ModifiedTime);

    private sealed class MutableRank
    {
        public double Score { get; set; }

        public int? WordRank { get; private set; }

        public int? TrigramRank { get; private set; }

        public int? EmbeddingRank { get; private set; }

        public int? ExactRank { get; private set; }

        public double? EmbeddingSimilarity { get; private set; }

        public double ExactScore { get; private set; }

        public void SetChannel(
            Channel channel,
            int rank,
            double channelScore)
        {
            switch (channel)
            {
                case Channel.Word:
                    WordRank = rank;
                    break;

                case Channel.Trigram:
                    TrigramRank = rank;
                    break;

                case Channel.Embedding:
                    EmbeddingRank = rank;
                    EmbeddingSimilarity = channelScore;
                    break;

                case Channel.Exact:
                    ExactRank = rank;
                    ExactScore = channelScore;
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(channel));
            }
        }
    }

    private enum Channel
    {
        Word,
        Trigram,
        Embedding,
        Exact,
    }
}
