#nullable enable

using System.Runtime.CompilerServices;
using System.Threading.Channels;
using CopilotSessionSearch.Models;

namespace CopilotSessionSearch.Services;

public sealed class SessionSearchCoordinator
{
    private const int DefaultMaximumConcurrency = 4;

    private readonly ISessionHistorySource _historySource;
    private readonly SessionDocumentCache _documentCache;
    private readonly SessionSearchService _searchService;
    private readonly int _maximumConcurrency;

    public SessionSearchCoordinator(
        ISessionHistorySource historySource,
        SessionDocumentCache documentCache,
        SessionSearchService searchService,
        int? maximumConcurrency = null)
    {
        ArgumentNullException.ThrowIfNull(historySource);
        ArgumentNullException.ThrowIfNull(documentCache);
        ArgumentNullException.ThrowIfNull(searchService);

        int resolvedMaximumConcurrency = maximumConcurrency
            ?? Math.Min(Environment.ProcessorCount, DefaultMaximumConcurrency);

        if (resolvedMaximumConcurrency <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumConcurrency),
                "Maximum concurrency must be greater than zero.");
        }

        _historySource = historySource;
        _documentCache = documentCache;
        _searchService = searchService;
        _maximumConcurrency = resolvedMaximumConcurrency;
    }

    public IAsyncEnumerable<SessionSearchUpdate> SearchAsync(
        string query,
        CancellationToken cancellationToken)
    {
        return SearchAsync(
            query,
            SessionSearchOptions.Default,
            cancellationToken);
    }

    public async IAsyncEnumerable<SessionSearchUpdate> SearchAsync(
        string query,
        SessionSearchOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentNullException.ThrowIfNull(options);

        IReadOnlyList<SessionDescriptor> sessions = await _historySource
            .GetSessionsAsync(cancellationToken)
            .ConfigureAwait(false);

        var updates = Channel.CreateUnbounded<SessionSearchUpdate>(
            new UnboundedChannelOptions
            {
                AllowSynchronousContinuations = false,
                SingleReader = true,
                SingleWriter = false,
            });

        Task processingTask = Task.Run(
            () => ProcessSessionsAsync(
                sessions,
                query.Trim(),
                options,
                updates.Writer,
                cancellationToken),
            CancellationToken.None);

        try
        {
            await foreach (SessionSearchUpdate update in updates.Reader.ReadAllAsync(cancellationToken))
            {
                yield return update;
            }

            await processingTask.ConfigureAwait(false);
        }
        finally
        {
            if (!processingTask.IsCompleted)
            {
                try
                {
                    await processingTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                }
            }
        }
    }

    private async Task ProcessSessionsAsync(
        IReadOnlyList<SessionDescriptor> sessions,
        string query,
        SessionSearchOptions options,
        ChannelWriter<SessionSearchUpdate> updateWriter,
        CancellationToken cancellationToken)
    {
        int completedSessions = 0;
        int matchingSessions = 0;
        int failedSessions = 0;
        var progressGate = new object();

        updateWriter.TryWrite(
            new SessionSearchUpdate(
                null,
                null,
                new SessionSearchProgress(0, sessions.Count, 0, 0)));

        try
        {
            var parallelOptions = new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = _maximumConcurrency,
            };

            await Parallel.ForEachAsync(
                sessions,
                parallelOptions,
                async (session, workerCancellationToken) =>
                {
                    SessionSearchResult? result = null;
                    SessionSearchFailure? failure = null;

                    try
                    {
                        SessionDocument document = await _documentCache.GetOrLoadAsync(
                            session,
                            _historySource.GetSessionDocumentAsync,
                            workerCancellationToken).ConfigureAwait(false);

                        result = _searchService.Search(
                            document,
                            query,
                            options,
                            workerCancellationToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        failure = new SessionSearchFailure(session, ex.Message);
                    }

                    workerCancellationToken.ThrowIfCancellationRequested();

                    lock (progressGate)
                    {
                        completedSessions++;
                        if (result is not null)
                        {
                            matchingSessions++;
                        }

                        if (failure is not null)
                        {
                            failedSessions++;
                        }

                        updateWriter.TryWrite(
                            new SessionSearchUpdate(
                                result,
                                failure,
                                new SessionSearchProgress(
                                    completedSessions,
                                    sessions.Count,
                                    matchingSessions,
                                    failedSessions)));
                    }
                }).ConfigureAwait(false);

            updateWriter.TryComplete();
        }
        catch (Exception ex)
        {
            updateWriter.TryComplete(ex);
        }
    }
}
