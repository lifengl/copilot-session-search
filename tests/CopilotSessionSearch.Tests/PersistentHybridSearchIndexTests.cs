#nullable enable

using CopilotSessionSearch.Models;
using CopilotSessionSearch.Services;
using Microsoft.Data.Sqlite;

namespace CopilotSessionSearch.Tests;

public sealed class PersistentHybridSearchIndexTests
{
    [Fact]
    public async Task VersionMismatchRebuildsDisposableIndex()
    {
        using var workspace = new TemporaryDirectory();
        string databasePath = Path.Combine(
            workspace.Path,
            "index.sqlite");
        await PrepareEmptyIndexAsync(databasePath);
        ExecuteNonQuery(
            databasePath,
            """
            UPDATE metadata
            SET value = 'obsolete'
            WHERE key = 'schema';
            """);

        using var index =
            new PersistentHybridSearchIndex(databasePath);
        HybridIndexMetrics metrics = await index.PrepareAsync(
            new EmptyHistorySource(),
            new SessionDocumentCache(),
            progress: null,
            CancellationToken.None);

        Assert.True(index.IsReady);
        Assert.Equal(0, metrics.Blocks);
        Assert.Equal(
            "1",
            ExecuteScalar<string>(
                databasePath,
                """
                SELECT value
                FROM metadata
                WHERE key = 'schema';
                """));
    }

    [Fact]
    public async Task CorruptDatabaseIsRebuiltAutomatically()
    {
        using var workspace = new TemporaryDirectory();
        string databasePath = Path.Combine(
            workspace.Path,
            "index.sqlite");
        await File.WriteAllBytesAsync(
            databasePath,
            [0x13, 0x37, 0x00, 0x42]);

        using var index =
            new PersistentHybridSearchIndex(databasePath);
        HybridIndexMetrics metrics = await index.PrepareAsync(
            new EmptyHistorySource(),
            new SessionDocumentCache(),
            progress: null,
            CancellationToken.None);

        Assert.True(index.IsReady);
        Assert.Equal(0, metrics.Blocks);
        Assert.Equal(
            "ok",
            ExecuteScalar<string>(
                databasePath,
                "PRAGMA quick_check;"));
    }

    [Fact]
    public async Task IncompatibleSchemaRebuildsBeforeCurrentDdlRuns()
    {
        using var workspace = new TemporaryDirectory();
        string databasePath = Path.Combine(
                workspace.Path,
                "index.sqlite");
        using (SqliteConnection connection =
                OpenConnection(databasePath))
        {
                using SqliteCommand command =
                    connection.CreateCommand();
                command.CommandText =
                    """
                    CREATE TABLE metadata (
                        key TEXT PRIMARY KEY,
                        value TEXT NOT NULL);
                    INSERT INTO metadata (key, value)
                    VALUES ('schema', 'obsolete');
                    CREATE TABLE blocks (
                        id INTEGER PRIMARY KEY);
                    """;
                command.ExecuteNonQuery();
        }

        using var index =
                new PersistentHybridSearchIndex(databasePath);
        HybridIndexMetrics metrics = await index.PrepareAsync(
                new EmptyHistorySource(),
                new SessionDocumentCache(),
                progress: null,
                CancellationToken.None);

        Assert.True(index.IsReady);
        Assert.Equal(0, metrics.Blocks);
        Assert.Equal(
                "1",
                ExecuteScalar<string>(
                    databasePath,
                    """
                    SELECT value
                    FROM metadata
                    WHERE key = 'schema';
                    """));
    }

    [Fact]
    public async Task MalformedBlockTimestampTriggersRebuild()
    {
        using var workspace = new TemporaryDirectory();
        string databasePath = Path.Combine(
                workspace.Path,
                "index.sqlite");
        await PrepareEmptyIndexAsync(databasePath);
        using (SqliteConnection connection =
                OpenConnection(databasePath))
        {
                using SqliteCommand command =
                    connection.CreateCommand();
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
                        'corrupt-session',
                        'Corrupt session',
                        'not-a-timestamp',
                        1,
                        1,
                        'Copilot',
                        'content',
                        'content',
                        zeroblob(388));
                    """;
                command.ExecuteNonQuery();
        }

        using var index =
                new PersistentHybridSearchIndex(databasePath);
        HybridIndexMetrics metrics = await index.PrepareAsync(
                new EmptyHistorySource(),
                new SessionDocumentCache(),
                progress: null,
                CancellationToken.None);

        Assert.True(index.IsReady);
        Assert.Equal(0, metrics.Blocks);
    }

    [Theory]
    [InlineData(
        "UPDATE blocks SET embedding = printf('%0388d', 0);")]
    [InlineData(
        "UPDATE blocks SET message_number = 2147483648;")]
    public async Task MalformedBlockStorageTriggersRebuild(
        string corruptionSql)
    {
        using var workspace = new TemporaryDirectory();
        string databasePath = Path.Combine(
            workspace.Path,
            "index.sqlite");
        await PrepareEmptyIndexAsync(databasePath);
        InsertSyntheticBlock(databasePath);
        string firstIncarnation = ExecuteScalar<string>(
            databasePath,
            """
            SELECT value
            FROM metadata
            WHERE key = 'incarnation';
            """);
        ExecuteNonQuery(
            databasePath,
            corruptionSql);

        using var index =
            new PersistentHybridSearchIndex(databasePath);
        HybridIndexMetrics metrics = await index.PrepareAsync(
            new EmptyHistorySource(),
            new SessionDocumentCache(),
            progress: null,
            CancellationToken.None);
        string secondIncarnation = ExecuteScalar<string>(
            databasePath,
            """
            SELECT value
            FROM metadata
            WHERE key = 'incarnation';
            """);

        Assert.True(index.IsReady);
        Assert.Equal(0, metrics.Blocks);
        Assert.NotEqual(
            firstIncarnation,
            secondIncarnation);
    }

    [Theory]
    [InlineData("metadata")]
    [InlineData("indexed_sessions")]
    public async Task NullPrimaryKeyTriggersRebuild(
        string tableName)
    {
        using var workspace = new TemporaryDirectory();
        string databasePath = Path.Combine(
            workspace.Path,
            "index.sqlite");
        await PrepareEmptyIndexAsync(databasePath);
        string firstIncarnation = ExecuteScalar<string>(
            databasePath,
            """
            SELECT value
            FROM metadata
            WHERE key = 'incarnation';
            """);
        ReplaceTableWithNullablePrimaryKey(
            databasePath,
            tableName);

        using var index =
            new PersistentHybridSearchIndex(databasePath);
        await index.PrepareAsync(
            new EmptyHistorySource(),
            new SessionDocumentCache(),
            progress: null,
            CancellationToken.None);
        string secondIncarnation = ExecuteScalar<string>(
            databasePath,
            """
            SELECT value
            FROM metadata
            WHERE key = 'incarnation';
            """);

        Assert.True(index.IsReady);
        Assert.NotEqual(
            firstIncarnation,
            secondIncarnation);
    }

    [Fact]
    public async Task CanceledPreparationKeepsExistingIndexReady()
    {
        using var workspace = new TemporaryDirectory();
        string databasePath = Path.Combine(
            workspace.Path,
            "index.sqlite");
        using var index =
            new PersistentHybridSearchIndex(databasePath);
        await index.PrepareAsync(
            new EmptyHistorySource(),
            new SessionDocumentCache(),
            progress: null,
            CancellationToken.None);
        var historySource = new BlockingHistorySource();
        using var cancellationSource =
            new CancellationTokenSource();

        Task<HybridIndexMetrics> preparation = index.PrepareAsync(
            historySource,
            new SessionDocumentCache(),
            progress: null,
            cancellationSource.Token);
        await historySource.Started;
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => preparation);
        Assert.True(index.IsReady);
    }

    [Fact]
    public async Task CancelingDestructiveRebuildInvalidatesReadyState()
    {
        using var workspace = new TemporaryDirectory();
        string databasePath = Path.Combine(
            workspace.Path,
            "index.sqlite");
        using var index =
            new PersistentHybridSearchIndex(databasePath);
        await index.PrepareAsync(
            new EmptyHistorySource(),
            new SessionDocumentCache(),
            progress: null,
            CancellationToken.None);
        ExecuteNonQuery(
            databasePath,
            """
            UPDATE metadata
            SET value = 'obsolete'
            WHERE key = 'schema';
            """);
        string walPath = databasePath + "-wal";
        if (!File.Exists(walPath))
        {
            await File.WriteAllBytesAsync(walPath, []);
        }

        using var heldWal = new FileStream(
            walPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite);
        using var cancellationSource =
            new CancellationTokenSource();
        Task<HybridIndexMetrics> preparation = index.PrepareAsync(
            new EmptyHistorySource(),
            new SessionDocumentCache(),
            progress: null,
            cancellationSource.Token);
        await WaitUntilAsync(
            () => !File.Exists(databasePath),
            TimeSpan.FromSeconds(2));
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => preparation);
        Assert.False(index.IsReady);
        Assert.Throws<InvalidOperationException>(
            () => index.Search("needle"));
    }

    [Fact]
    public async Task InvalidGenerationTriggersRebuild()
    {
        using var workspace = new TemporaryDirectory();
        string databasePath = Path.Combine(
            workspace.Path,
            "index.sqlite");
        await PrepareEmptyIndexAsync(databasePath);
        string firstIncarnation = ExecuteScalar<string>(
            databasePath,
            """
            SELECT value
            FROM metadata
            WHERE key = 'incarnation';
            """);
        ExecuteNonQuery(
            databasePath,
            """
            UPDATE metadata
            SET value = '9223372036854775808'
            WHERE key = 'generation';
            """);

        using var index =
            new PersistentHybridSearchIndex(databasePath);
        await index.PrepareAsync(
            new EmptyHistorySource(),
            new SessionDocumentCache(),
            progress: null,
            CancellationToken.None);
        string secondIncarnation = ExecuteScalar<string>(
            databasePath,
            """
            SELECT value
            FROM metadata
            WHERE key = 'incarnation';
            """);

        Assert.True(index.IsReady);
        Assert.NotEqual(
            firstIncarnation,
            secondIncarnation);
    }

    [Fact]
    public async Task SearchRefreshesBlocksAfterExternalGenerationChange()
    {
        using var workspace = new TemporaryDirectory();
        string databasePath = Path.Combine(
            workspace.Path,
            "index.sqlite");
        SessionDescriptor descriptor = CreateDescriptor();
        var historySource = new SingleDocumentHistorySource(
            new SessionDocument(
                descriptor,
                [
                    new ConversationEntry(
                        "event-1",
                        ConversationSpeaker.Copilot,
                        descriptor.StartTime,
                        "Original needle evidence."),
                ]));
        using var index =
            new PersistentHybridSearchIndex(databasePath);
        await index.PrepareAsync(
            historySource,
            new SessionDocumentCache(),
            progress: null,
            CancellationToken.None);
        long originalBlockId = ExecuteScalar<long>(
            databasePath,
            "SELECT id FROM blocks;");

        long replacementBlockId =
            ReplaceBlockAndAdvanceGeneration(
                databasePath,
                descriptor);
        HybridQueryResult result = index.Search(
            "needle",
            maximumResults: 10);

        Assert.NotEqual(
            originalBlockId,
            replacementBlockId);
        Assert.Contains(
            result.HybridResults,
            candidate =>
                candidate.Block.Id == replacementBlockId
                && candidate.Block.Text.Contains(
                    "Replacement",
                    StringComparison.Ordinal));
    }

    [Fact]
    public async Task SearchRefreshesBlocksAfterDatabaseRebuild()
    {
        using var workspace = new TemporaryDirectory();
        string databasePath = Path.Combine(
            workspace.Path,
            "index.sqlite");
        SessionDescriptor descriptor = CreateDescriptor();
        using var firstIndex =
            new PersistentHybridSearchIndex(databasePath);
        await firstIndex.PrepareAsync(
            new SingleDocumentHistorySource(
                CreateDocument(
                    descriptor,
                    "Original needle evidence.")),
            new SessionDocumentCache(),
            progress: null,
            CancellationToken.None);
        string firstIncarnation = ExecuteScalar<string>(
            databasePath,
            """
            SELECT value
            FROM metadata
            WHERE key = 'incarnation';
            """);
        ExecuteNonQuery(
            databasePath,
            """
            UPDATE metadata
            SET value = 'obsolete'
            WHERE key = 'schema';
            """);
        using var rebuildingIndex =
            new PersistentHybridSearchIndex(databasePath);
        await rebuildingIndex.PrepareAsync(
            new SingleDocumentHistorySource(
                CreateDocument(
                    descriptor,
                    "Replacement needle evidence.")),
            new SessionDocumentCache(),
            progress: null,
            CancellationToken.None);
        string secondIncarnation = ExecuteScalar<string>(
            databasePath,
            """
            SELECT value
            FROM metadata
            WHERE key = 'incarnation';
            """);

        HybridQueryResult result = firstIndex.Search(
            "needle",
            maximumResults: 10);

        Assert.NotEqual(
            firstIncarnation,
            secondIncarnation);
        Assert.Contains(
            result.HybridResults,
            candidate => candidate.Block.Text.Contains(
                "Replacement",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            result.HybridResults,
            candidate => candidate.Block.Text.Contains(
                "Original",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task SearchCanBeCanceledWhileWaitingForIndexLease()
    {
        using var workspace = new TemporaryDirectory();
        string databasePath = Path.Combine(
            workspace.Path,
            "index.sqlite");
        using var index =
            new PersistentHybridSearchIndex(databasePath);
        await index.PrepareAsync(
            new EmptyHistorySource(),
            new SessionDocumentCache(),
            progress: null,
            CancellationToken.None);
        using var externalLease = new FileStream(
            databasePath + ".lock",
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);
        using var cancellationSource =
            new CancellationTokenSource();

        Task<HybridQueryResult> search = Task.Run(
            () => index.Search(
                "needle",
                maximumResults: 10,
                cancellationSource.Token));
        await Task.Delay(150);
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => search);
    }

    [Fact]
    public async Task SearchDoesNotRetryPermanentLeasePathFailure()
    {
        using var workspace = new TemporaryDirectory();
        string indexDirectory = Path.Combine(
            workspace.Path,
            "active");
        Directory.CreateDirectory(indexDirectory);
        string databasePath = Path.Combine(
            indexDirectory,
            "index.sqlite");
        using var index =
            new PersistentHybridSearchIndex(databasePath);
        await index.PrepareAsync(
            new EmptyHistorySource(),
            new SessionDocumentCache(),
            progress: null,
            CancellationToken.None);
        Directory.Move(
            indexDirectory,
            Path.Combine(
                workspace.Path,
                "moved"));

        Assert.Throws<DirectoryNotFoundException>(
            () => index.Search("needle"));
    }

    private static async Task PrepareEmptyIndexAsync(
        string databasePath)
    {
        using var index =
            new PersistentHybridSearchIndex(databasePath);
        await index.PrepareAsync(
            new EmptyHistorySource(),
            new SessionDocumentCache(),
            progress: null,
            CancellationToken.None);
    }

    private static long ReplaceBlockAndAdvanceGeneration(
        string databasePath,
        SessionDescriptor descriptor)
    {
        using SqliteConnection connection =
            OpenConnection(databasePath);
        byte[] embedding;
        using (SqliteCommand read = connection.CreateCommand())
        {
            read.CommandText =
                "SELECT embedding FROM blocks LIMIT 1;";
            embedding = (byte[])(read.ExecuteScalar()
                ?? throw new InvalidOperationException(
                    "The original embedding was unavailable."));
        }

        using SqliteTransaction transaction =
            connection.BeginTransaction();
        using (SqliteCommand delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText =
                """
                DELETE FROM blocks_word;
                DELETE FROM blocks_trigram;
                DELETE FROM blocks;
                """;
            delete.ExecuteNonQuery();
        }

        long blockId;
        using (SqliteCommand insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText =
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
                    1,
                    1,
                    'Copilot',
                    'Replacement needle evidence.',
                    'Replacement needle evidence.',
                    $embedding);
                SELECT last_insert_rowid();
                """;
            insert.Parameters.AddWithValue(
                "$session_id",
                descriptor.SessionId);
            insert.Parameters.AddWithValue(
                "$session_name",
                descriptor.Name);
            insert.Parameters.AddWithValue(
                "$modified_time",
                descriptor.ModifiedTime.ToString("O"));
            insert.Parameters.AddWithValue(
                "$embedding",
                embedding);
            blockId = (long)(insert.ExecuteScalar()
                ?? throw new InvalidOperationException(
                    "The replacement block ID was unavailable."));
        }

        foreach (string tableName in new[]
        {
            "blocks_word",
            "blocks_trigram",
        })
        {
            using SqliteCommand insertFts =
                connection.CreateCommand();
            insertFts.Transaction = transaction;
            insertFts.CommandText =
                $"INSERT INTO {tableName} " +
                "(rowid, session_name, body) " +
                "VALUES ($id, $session_name, $body);";
            insertFts.Parameters.AddWithValue(
                "$id",
                blockId);
            insertFts.Parameters.AddWithValue(
                "$session_name",
                descriptor.Name);
            insertFts.Parameters.AddWithValue(
                "$body",
                "Replacement needle evidence.");
            insertFts.ExecuteNonQuery();
        }

        using (SqliteCommand generation =
            connection.CreateCommand())
        {
            generation.Transaction = transaction;
            generation.CommandText =
                """
                UPDATE metadata
                SET value = CAST(value AS INTEGER) + 1
                WHERE key = 'generation';
                """;
            generation.ExecuteNonQuery();
        }

        transaction.Commit();
        return blockId;
    }

    private static void InsertSyntheticBlock(
        string databasePath)
    {
        using SqliteConnection connection =
            OpenConnection(databasePath);
        using SqliteCommand command = connection.CreateCommand();
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
                'synthetic-session',
                'Synthetic session',
                '2026-01-01T10:00:00.0000000+00:00',
                1,
                1,
                'Copilot',
                'Synthetic content.',
                'Synthetic content.',
                zeroblob(388));
            """;
        command.ExecuteNonQuery();
    }

    private static void ReplaceTableWithNullablePrimaryKey(
        string databasePath,
        string tableName)
    {
        using SqliteConnection connection =
            OpenConnection(databasePath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = tableName switch
        {
            "metadata" =>
                """
                ALTER TABLE metadata RENAME TO metadata_old;
                CREATE TABLE metadata (
                    key TEXT PRIMARY KEY,
                    value TEXT NOT NULL);
                INSERT INTO metadata (key, value)
                SELECT key, value
                FROM metadata_old;
                DROP TABLE metadata_old;
                INSERT INTO metadata (key, value)
                VALUES (NULL, 'invalid');
                """,
            "indexed_sessions" =>
                """
                ALTER TABLE indexed_sessions
                RENAME TO indexed_sessions_old;
                CREATE TABLE indexed_sessions (
                    session_id TEXT PRIMARY KEY,
                    name TEXT NOT NULL,
                    modified_time TEXT NOT NULL);
                INSERT INTO indexed_sessions (
                    session_id,
                    name,
                    modified_time)
                SELECT
                    session_id,
                    name,
                    modified_time
                FROM indexed_sessions_old;
                DROP TABLE indexed_sessions_old;
                INSERT INTO indexed_sessions (
                    session_id,
                    name,
                    modified_time)
                VALUES (
                    NULL,
                    'Invalid session',
                    '2026-01-01T10:00:00.0000000+00:00');
                """,
            _ => throw new ArgumentOutOfRangeException(
                nameof(tableName)),
        };
        command.ExecuteNonQuery();
    }

    private static void ExecuteNonQuery(
        string databasePath,
        string commandText)
    {
        using SqliteConnection connection =
            OpenConnection(databasePath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = commandText;
        command.ExecuteNonQuery();
    }

    private static T ExecuteScalar<T>(
        string databasePath,
        string commandText)
    {
        using SqliteConnection connection =
            OpenConnection(databasePath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = commandText;
        return (T)(command.ExecuteScalar()
            ?? throw new InvalidOperationException(
                "The query returned no value."));
    }

    private static SqliteConnection OpenConnection(
        string databasePath)
    {
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false,
            }.ToString());
        connection.Open();
        return connection;
    }

    private static SessionDescriptor CreateDescriptor()
    {
        return new SessionDescriptor(
            "session-id",
            "Session name",
            DateTimeOffset.Parse(
                "2026-01-01T10:00:00Z"),
            DateTimeOffset.Parse(
                "2026-01-01T11:00:00Z"),
            @"Q:\ws\project",
            "owner/repository",
            "main");
    }

    private static SessionDocument CreateDocument(
        SessionDescriptor descriptor,
        string text)
    {
        return new SessionDocument(
            descriptor,
            [
                new ConversationEntry(
                    "event-1",
                    ConversationSpeaker.Copilot,
                    descriptor.StartTime,
                    text),
            ]);
    }

    private static async Task WaitUntilAsync(
        Func<bool> condition,
        TimeSpan timeout)
    {
        DateTimeOffset deadline =
            DateTimeOffset.UtcNow + timeout;
        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException(
                    "The expected index state was not reached.");
            }

            await Task.Delay(10);
        }
    }

    private sealed class EmptyHistorySource :
        ISessionHistorySource
    {
        public Task<IReadOnlyList<SessionDescriptor>> GetSessionsAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<
                IReadOnlyList<SessionDescriptor>>([]);
        }

        public Task<SessionDocument> GetSessionDocumentAsync(
            SessionDescriptor session,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException(
                "No session documents are available.");
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingHistorySource :
        ISessionHistorySource
    {
        private readonly TaskCompletionSource _started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;

        public async Task<IReadOnlyList<SessionDescriptor>>
            GetSessionsAsync(
                CancellationToken cancellationToken)
        {
            _started.TrySetResult();
            await Task.Delay(
                Timeout.InfiniteTimeSpan,
                cancellationToken);
            return [];
        }

        public Task<SessionDocument> GetSessionDocumentAsync(
            SessionDescriptor session,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException(
                "No session documents are available.");
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SingleDocumentHistorySource :
        ISessionHistorySource
    {
        private readonly SessionDocument _document;

        public SingleDocumentHistorySource(
            SessionDocument document)
        {
            _document = document;
        }

        public Task<IReadOnlyList<SessionDescriptor>> GetSessionsAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<
                IReadOnlyList<SessionDescriptor>>(
                    [_document.Session]);
        }

        public Task<SessionDocument> GetSessionDocumentAsync(
            SessionDescriptor session,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_document);
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "CopilotSessionSearch.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(
                    Path,
                    recursive: true);
            }
        }
    }
}
