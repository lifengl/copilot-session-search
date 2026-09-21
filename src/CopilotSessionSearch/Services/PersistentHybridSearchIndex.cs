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
    private const int EmbeddingStorageLength = 388;
    private const string GenerationMetadataKey = "generation";
    private const string IncarnationMetadataKey = "incarnation";

    private readonly string _databasePath;
    private readonly string _indexLockPath;
    private readonly SemaphoreSlim _preparationGate = new(1, 1);
    private LocalEmbedder? _embedder;
    private IReadOnlyDictionary<long, HybridSearchBlock> _blocks =
        new Dictionary<long, HybridSearchBlock>();
    private long _loadedGeneration = -1;
    private string? _loadedIncarnation;
    private volatile bool _isReady;
    private bool _disposed;

    public PersistentHybridSearchIndex(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        _databasePath = Path.GetFullPath(databasePath);
        _indexLockPath = _databasePath + ".lock";
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
            IReadOnlyList<SessionDescriptor> sessions =
                await historySource
                    .GetSessionsAsync(cancellationToken)
                    .ConfigureAwait(false);
            var stopwatch = Stopwatch.StartNew();
            string? databaseDirectory = Path.GetDirectoryName(_databasePath);
            if (!string.IsNullOrWhiteSpace(databaseDirectory))
            {
                Directory.CreateDirectory(databaseDirectory);
            }

            using FileStream indexLock = await Task.Run(
                () => AcquireIndexLock(cancellationToken),
                cancellationToken).ConfigureAwait(false);
            using SqliteConnection connection =
                OpenValidatedDatabase(cancellationToken);
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
                DeleteSessionAndAdvanceGeneration(
                    connection,
                    sessionId);
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

            IndexIdentity identity =
                LoadIndexIdentity(connection);
            bool reloadBlocks = _blocks.Count == 0
                || totalOperations > 0
                || _loadedGeneration != identity.Generation
                || !string.Equals(
                    _loadedIncarnation,
                    identity.Incarnation,
                    StringComparison.Ordinal);
            if (reloadBlocks)
            {
                _blocks = await Task.Run(
                    () => LoadBlocks(connection),
                    cancellationToken).ConfigureAwait(false);
            }

            _loadedGeneration = identity.Generation;
            _loadedIncarnation = identity.Incarnation;
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
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
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
        int maximumResults = 30,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        if (!_isReady)
        {
            throw new InvalidOperationException(
                "The local AI search index is not ready.");
        }

        try
        {
            using FileStream indexLock =
                AcquireIndexLock(cancellationToken);
            using SqliteConnection connection =
                OpenReadOnlyDatabase();
            IndexIdentity identity =
                LoadIndexIdentity(connection);
            if (_loadedGeneration != identity.Generation
                || !string.Equals(
                    _loadedIncarnation,
                    identity.Incarnation,
                    StringComparison.Ordinal))
            {
                _blocks = LoadBlocks(connection);
                _loadedGeneration = identity.Generation;
                _loadedIncarnation = identity.Incarnation;
            }

            IReadOnlyList<string> terms =
                HybridSearchQuery.ExtractTerms(query);
            var stopwatch = Stopwatch.StartNew();
            IReadOnlyList<ChannelHit> wordHits = terms.Count == 0
                ? []
                : SearchFts(
                    connection,
                    "blocks_word",
                    HybridSearchQuery.CreateMatchExpression(terms),
                    maximumResults: 150,
                    cancellationToken);
            stopwatch.Stop();
            TimeSpan wordTime = stopwatch.Elapsed;

            stopwatch.Restart();
            IReadOnlyList<string> trigramTerms = terms
                .Where(term => term.Length >= 3)
                .ToArray();
            IReadOnlyList<ChannelHit> trigramHits =
                trigramTerms.Count == 0
                    ? []
                    : SearchFts(
                        connection,
                        "blocks_trigram",
                        HybridSearchQuery.CreateMatchExpression(
                            trigramTerms),
                        maximumResults: 150,
                        cancellationToken);
            stopwatch.Stop();
            TimeSpan trigramTime = stopwatch.Elapsed;

            stopwatch.Restart();
            EmbeddingI8[] queryEmbeddings = HybridSearchQuery
                .CreateSemanticQueries(query)
                .Select(
                    semanticQuery =>
                        GetEmbedder().Embed<EmbeddingI8>(
                            semanticQuery))
                .ToArray();
            IReadOnlyList<ChannelHit> embeddingHits = _blocks
                .Values
                .Select(
                    block =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        return new ChannelHit(
                            block.Id,
                            queryEmbeddings.Max(
                                queryEmbedding =>
                                    new EmbeddingI8(block.Embedding)
                                        .Similarity(queryEmbedding)));
                    })
                .OrderByDescending(hit => hit.Score)
                .Take(150)
                .ToArray();
            stopwatch.Stop();
            TimeSpan embeddingTime = stopwatch.Elapsed;

            stopwatch.Restart();
            IReadOnlyList<ChannelHit> exactHits =
                terms.Count == 0
                    ? []
                    : _blocks.Values
                        .Select(
                            block =>
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                return new ChannelHit(
                                    block.Id,
                                    HybridSearchQuery.ScoreExact(
                                        query,
                                        terms,
                                        block));
                            })
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
                CreateSingleChannelResults(
                    wordHits,
                    Channel.Word),
                CreateSingleChannelResults(
                    embeddingHits,
                    Channel.Embedding),
                hybrid);
        }
        catch (SqliteException ex) when (
            IsRecoverableDatabaseError(ex))
        {
            _isReady = false;
            throw new InvalidOperationException(
                "The local AI search index is corrupted and will be rebuilt.",
                ex);
        }
        catch (Exception ex) when (
            ex is FormatException
            or InvalidCastException
            or InvalidDataException
            or OverflowException)
        {
            _isReady = false;
            throw new InvalidOperationException(
                "The local AI search index is corrupted and will be rebuilt.",
                ex);
        }
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

    private SqliteConnection OpenValidatedDatabase(
        CancellationToken cancellationToken)
    {
        SqliteConnection? connection = null;
        try
        {
            connection = OpenWritableDatabase();
            if (!HasValidDatabaseIntegrity(connection))
            {
                return RebuildDatabaseAndRelease(
                    ref connection,
                    cancellationToken);
            }

            if (!TableExists(connection, "metadata"))
            {
                if (HasUserTables(connection))
                {
                    return RebuildDatabaseAndRelease(
                        ref connection,
                        cancellationToken);
                }

                CreateSchema(connection);
                WriteMetadata(connection);
                return TakeConnection(ref connection);
            }

            Dictionary<string, string> metadata =
                LoadMetadata(connection);
            if (metadata.Count == 0)
            {
                return RebuildDatabaseAndRelease(
                    ref connection,
                    cancellationToken);
            }

            if (!HasExpectedVersions(metadata))
            {
                return RebuildDatabaseAndRelease(
                    ref connection,
                    cancellationToken);
            }

            CreateSchema(connection);
            EnsureIncarnation(connection);
            ValidateStoredData(connection);
            return TakeConnection(ref connection);
        }
        catch (SqliteException ex) when (
            IsRecoverableDatabaseError(ex))
        {
            return RebuildDatabaseAndRelease(
                ref connection,
                cancellationToken);
        }
        catch (Exception ex) when (
            ex is FormatException
            or InvalidCastException
            or InvalidDataException
            or OverflowException)
        {
            return RebuildDatabaseAndRelease(
                ref connection,
                cancellationToken);
        }
        finally
        {
            connection?.Dispose();
        }
    }

    private SqliteConnection OpenWritableDatabase()
    {
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = _databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false,
            }.ToString());
        bool initialized = false;
        try
        {
            connection.Open();
            using SqliteCommand command =
                connection.CreateCommand();
            command.CommandText =
                """
                PRAGMA journal_mode = WAL;
                PRAGMA synchronous = NORMAL;
                PRAGMA temp_store = MEMORY;
                """;
            command.ExecuteNonQuery();
            initialized = true;
            return connection;
        }
        finally
        {
            if (!initialized)
            {
                connection.Dispose();
            }
        }
    }

    private SqliteConnection OpenReadOnlyDatabase()
    {
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = _databasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ToString());
        bool opened = false;
        try
        {
            connection.Open();
            opened = true;
            return connection;
        }
        finally
        {
            if (!opened)
            {
                connection.Dispose();
            }
        }
    }

    private IReadOnlyList<ChannelHit> SearchFts(
        SqliteConnection connection,
        string tableName,
        string matchExpression,
        int maximumResults,
        CancellationToken cancellationToken)
    {
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
            cancellationToken.ThrowIfCancellationRequested();
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
                    if (!_blocks.TryGetValue(
                        pair.Key,
                        out HybridSearchBlock? block))
                    {
                        return null;
                    }

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
            .OfType<HybridRankedBlock>()
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
                    if (!_blocks.TryGetValue(
                        hit.BlockId,
                        out HybridSearchBlock? block))
                    {
                        return null;
                    }

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
            .OfType<HybridRankedBlock>()
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
        AdvanceGeneration(connection, transaction);
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

    private static void DeleteSessionAndAdvanceGeneration(
        SqliteConnection connection,
        string sessionId)
    {
        using SqliteTransaction transaction =
            connection.BeginTransaction();
        DeleteSession(
            connection,
            sessionId,
            transaction);
        AdvanceGeneration(connection, transaction);
        transaction.Commit();
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
            if (reader.IsDBNull(0)
                || reader.IsDBNull(1)
                || reader.IsDBNull(2))
            {
                throw new InvalidDataException(
                    "The local AI index contains an invalid indexed session.");
            }

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
                    RetrievalText: string.Empty,
                    Embedding: (byte[])reader[8]));
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
            if (reader.IsDBNull(0)
                || reader.IsDBNull(1))
            {
                throw new InvalidDataException(
                    "The local AI index contains invalid metadata.");
            }

            metadata.Add(
                reader.GetString(0),
                reader.GetString(1));
        }

        return metadata;
    }

    private static IndexIdentity LoadIndexIdentity(
        SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT key, value
            FROM metadata
            WHERE key IN ($incarnation_key, $generation_key);
            """;
        command.Parameters.AddWithValue(
            "$incarnation_key",
            IncarnationMetadataKey);
        command.Parameters.AddWithValue(
            "$generation_key",
            GenerationMetadataKey);
        using SqliteDataReader reader = command.ExecuteReader();
        string incarnation = string.Empty;
        long generation = 0;
        while (reader.Read())
        {
            string key = reader.GetString(0);
            string value = reader.GetString(1);
            if (string.Equals(
                key,
                IncarnationMetadataKey,
                StringComparison.Ordinal))
            {
                incarnation = value;
            }
            else
            {
                if (!long.TryParse(
                    value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out generation))
                {
                    throw new InvalidDataException(
                        "The local AI index generation is invalid.");
                }
            }
        }

        return new IndexIdentity(
            incarnation,
            generation);
    }

    private static void EnsureIncarnation(
        SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO metadata (key, value)
            VALUES ($key, $value)
            ON CONFLICT(key) DO NOTHING;
            """;
        command.Parameters.AddWithValue(
            "$key",
            IncarnationMetadataKey);
        command.Parameters.AddWithValue(
            "$value",
            Guid.NewGuid().ToString("N"));
        command.ExecuteNonQuery();
    }

    private static void AdvanceGeneration(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO metadata (key, value)
            VALUES ($key, '1')
            ON CONFLICT(key) DO UPDATE SET
                value = CAST(value AS INTEGER) + 1;
            """;
        command.Parameters.AddWithValue(
            "$key",
            GenerationMetadataKey);
        command.ExecuteNonQuery();
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
            new(GenerationMetadataKey, "0"),
            new(
                IncarnationMetadataKey,
                Guid.NewGuid().ToString("N")),
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
                key TEXT PRIMARY KEY NOT NULL,
                value TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS indexed_sessions (
                session_id TEXT PRIMARY KEY NOT NULL,
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

    private SqliteConnection RebuildDatabaseAndRelease(
        ref SqliteConnection? connection,
        CancellationToken cancellationToken)
    {
        connection?.Dispose();
        connection = null;
        _isReady = false;
        _blocks = new Dictionary<long, HybridSearchBlock>();
        _loadedGeneration = -1;
        _loadedIncarnation = null;
        DeleteDatabaseFiles(cancellationToken);
        SqliteConnection? rebuilt = null;
        try
        {
            rebuilt = OpenWritableDatabase();
            CreateSchema(rebuilt);
            WriteMetadata(rebuilt);
            return TakeConnection(ref rebuilt);
        }
        finally
        {
            rebuilt?.Dispose();
        }
    }

    private static SqliteConnection TakeConnection(
        ref SqliteConnection? connection)
    {
        SqliteConnection result = connection
            ?? throw new InvalidOperationException(
                "The SQLite connection was unavailable.");
        connection = null;
        return result;
    }

    private static bool TableExists(
        SqliteConnection connection,
        string tableName)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT EXISTS (
                SELECT 1
                FROM sqlite_master
                WHERE type = 'table'
                  AND name = $name);
            """;
        command.Parameters.AddWithValue("$name", tableName);
        return Convert.ToInt32(
            command.ExecuteScalar(),
            CultureInfo.InvariantCulture) != 0;
    }

    private static bool HasUserTables(
        SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT EXISTS (
                SELECT 1
                FROM sqlite_master
                WHERE type = 'table'
                  AND name NOT LIKE 'sqlite_%');
            """;
        return Convert.ToInt32(
            command.ExecuteScalar(),
            CultureInfo.InvariantCulture) != 0;
    }

    private static bool HasValidDatabaseIntegrity(
        SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA quick_check;";
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read()
            && string.Equals(
                reader.GetString(0),
                "ok",
                StringComparison.OrdinalIgnoreCase)
            && !reader.Read();
    }

    private static void ValidateStoredData(
        SqliteConnection connection)
    {
        IndexIdentity identity = LoadIndexIdentity(connection);
        if (string.IsNullOrWhiteSpace(identity.Incarnation))
        {
            throw new InvalidDataException(
                "The local AI index has no incarnation identifier.");
        }

        _ = LoadIndexedSessions(connection);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                modified_time,
                message_number,
                chunk_number,
                typeof(session_id),
                typeof(session_name),
                typeof(modified_time),
                typeof(message_number),
                typeof(chunk_number),
                typeof(speaker),
                typeof(body),
                typeof(retrieval_text),
                typeof(embedding),
                length(embedding)
            FROM blocks
            """;
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            _ = DateTimeOffset.Parse(
                reader.GetString(0),
                CultureInfo.InvariantCulture);
            long messageNumber = reader.GetInt64(1);
            long chunkNumber = reader.GetInt64(2);
            if (messageNumber is < int.MinValue or > int.MaxValue
                || chunkNumber is < int.MinValue or > int.MaxValue)
            {
                throw new InvalidDataException(
                    "The local AI index contains an invalid message or chunk number.");
            }

            for (int column = 3; column <= 5; column++)
            {
                if (!string.Equals(
                    reader.GetString(column),
                    "text",
                    StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "The local AI index contains an invalid text value.");
                }
            }

            for (int column = 6; column <= 7; column++)
            {
                if (!string.Equals(
                    reader.GetString(column),
                    "integer",
                    StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "The local AI index contains an invalid numeric value.");
                }
            }

            for (int column = 8; column <= 10; column++)
            {
                if (!string.Equals(
                    reader.GetString(column),
                    "text",
                    StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "The local AI index contains an invalid text value.");
                }
            }

            if (!string.Equals(
                reader.GetString(11),
                "blob",
                StringComparison.Ordinal)
                || reader.GetInt64(12) != EmbeddingStorageLength)
            {
                throw new InvalidDataException(
                    "The local AI index contains an invalid embedding vector.");
            }
        }
    }

    private static bool IsCorrupt(SqliteException exception)
    {
        const int SqliteCorrupt = 11;
        const int SqliteNotADatabase = 26;
        return exception.SqliteErrorCode is
            SqliteCorrupt
            or SqliteNotADatabase;
    }

    private static bool IsRecoverableDatabaseError(
        SqliteException exception)
    {
        const int SqliteError = 1;
        return IsCorrupt(exception)
            || exception.SqliteErrorCode == SqliteError;
    }

    private FileStream AcquireIndexLock(
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    _indexLockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);
            }
            catch (IOException ex) when (
                IsSharingViolation(ex))
            {
                if (cancellationToken.WaitHandle.WaitOne(
                    TimeSpan.FromMilliseconds(100)))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
        }
    }

    private static bool IsSharingViolation(
        IOException exception)
    {
        const int SharingViolation = 32;
        const int LockViolation = 33;
        int errorCode = exception.HResult & 0xFFFF;
        return errorCode is
            SharingViolation
            or LockViolation;
    }

    private void DeleteDatabaseFiles(
        CancellationToken cancellationToken)
    {
        foreach (string path in new[]
        {
            _databasePath,
            _databasePath + "-shm",
            _databasePath + "-wal",
        })
        {
            for (int attempt = 0;
                File.Exists(path);
                attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    File.Delete(path);
                }
                catch (IOException) when (attempt < 50)
                {
                    if (cancellationToken.WaitHandle.WaitOne(
                        TimeSpan.FromMilliseconds(100)))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                }
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

    private sealed record IndexIdentity(
        string Incarnation,
        long Generation);

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
