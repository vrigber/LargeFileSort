using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using LargeFileSort.Sorter.Abstractions;
using LargeFileSort.Sorter.Models;
using LargeFileSort.Sorter.Storage;
using Microsoft.Extensions.Logging;

namespace LargeFileSort.Sorter.Chunks;

internal sealed class ChunkPipeline(
    ChunkBuilder builder,
    ChunkSorter sorter,
    RunStore store,
    SortPlan plan,
    Func<IOutputWriter> createOutput,
    SortStatistics statistics,
    ILogger<ChunkPipeline> logger)
{
    private readonly ChunkBuilder _builder = builder ?? throw new ArgumentNullException(nameof(builder));
    private readonly ChunkSorter _sorter = sorter ?? throw new ArgumentNullException(nameof(sorter));
    private readonly RunStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly SortPlan _plan = plan ?? throw new ArgumentNullException(nameof(plan));
    private readonly Func<IOutputWriter> _createOutput = createOutput ?? throw new ArgumentNullException(nameof(createOutput));
    private readonly SortStatistics _statistics = statistics ?? throw new ArgumentNullException(nameof(statistics));
    private readonly ILogger<ChunkPipeline> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private int _started;

    public async Task<ChunkPipelineResult> RunAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException("A chunk pipeline can only be run once.");
        ChunkSlots slots = new(_plan);
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationToken token = stop.Token;
        ExceptionDispatchInfo failure = null;
        List<SortedRun> runs = [];
        object catalogGate = new();
        List<Task> workers = [];
        bool outputCompleted = false;
        Stopwatch timer = Stopwatch.StartNew();

        void Fail(Exception exception)
        {
            ExceptionDispatchInfo captured = ExceptionDispatchInfo.Capture(exception);
            ExceptionDispatchInfo previous = exception is OutOfMemoryException
                ? Interlocked.Exchange(ref failure, captured)
                : Interlocked.CompareExchange(ref failure, captured, null);
            if (previous is not null && exception is not OperationCanceledException
                && !ReferenceEquals(previous.SourceException, exception))
                _logger.LogError(exception, "Another chunk pipeline operation failed during shutdown.");
            try { stop.Cancel(); }
            catch (Exception cancellationFailure)
            {
                if (cancellationFailure is OutOfMemoryException)
                    Interlocked.Exchange(ref failure, ExceptionDispatchInfo.Capture(cancellationFailure));
                else _logger.LogError(cancellationFailure, "Cancelling chunk pipeline participants failed.");
            }
        }

        async Task ProcessAsync(SortChunk chunk, bool directOutput)
        {
            bool workerStarted = false;
            IOutputWriter writer = null;
            try
            {
                slots.StartWorker();
                workerStarted = true;
                token.ThrowIfCancellationRequested();
                if (directOutput) _sorter.Sort(chunk, token);
                else await Task.Run(() => _sorter.Sort(chunk, token), token);
                SegmentedRunWriter runWriter = directOutput ? null : _store.CreateWriter(_plan.InitialSegmentTargetBytes);
                writer = directOutput ? _createOutput() : runWriter;
                for (int index = 0; index < chunk.Count; index++)
                    await writer.WriteLineAsync(chunk.Records[index].Line.AsMemory(), token);
                await writer.CompleteAsync(token);
                if (runWriter is not null)
                {
                    SortedRun run = runWriter.GetCompletedRun();
                    lock (catalogGate)
                    {
                        if (runs.Count == _plan.MaxRunCount)
                            throw new IOException($"The run catalog limit of {_plan.MaxRunCount} has been reached; increase the RAM budget or chunk size.");
                        if (runs.Count == runs.Capacity)
                            runs.Capacity = (int)Math.Min(_plan.MaxRunCount, Math.Max(4L, 2L * runs.Capacity));
                        runs.Add(run);
                    }
                }
            }
            catch (Exception exception) { Fail(exception); }
            finally
            {
                try
                {
                    if (writer is not null) await writer.DisposeAsync();
                }
                catch (Exception cleanup) { Fail(cleanup); }
                finally
                {
                    slots.Release(chunk);
                    if (workerStarted) slots.FinishWorker();
                }
            }
        }

        try
        {
            token.ThrowIfCancellationRequested();
            SortChunk chunk = slots.Acquire();
            ChunkReadResult read = await _builder.FillAsync(chunk, token);
            if (read.EndOfInput)
            {
                await ProcessAsync(chunk, directOutput: true);
                outputCompleted = failure is null;
            }
            else
            {
                while (true)
                {
                    if (workers.Count == _plan.WorkerCount)
                    {
                        Task finished = await Task.WhenAny(workers).WaitAsync(token);
                        workers.Remove(finished);
                        await finished;
                    }
                    token.ThrowIfCancellationRequested();
                    workers.Add(ProcessAsync(chunk, directOutput: false));
                    if (read.EndOfInput) break;
                    chunk = slots.Acquire();
                    read = await _builder.FillAsync(chunk, token);
                    if (chunk.Count == 0)
                    {
                        slots.Release(chunk);
                        break;
                    }
                }
            }
        }
        catch (Exception exception) { Fail(exception); }
        finally
        {
            try { await Task.WhenAll(workers); }
            catch (Exception exception) { Fail(exception); }
            slots.Clear();
            try { _store.CaptureActualPeak(); }
            catch (Exception exception) { Fail(exception); }
            if (failure is not null) _builder.Abort();
            timer.Stop();
            _statistics.ChunkPhaseTime = timer.Elapsed;
            _statistics.PeakEstimatedMemoryBytes = Math.Max(_statistics.PeakEstimatedMemoryBytes, slots.PeakEstimatedMemoryBytes);
        }

        failure?.Throw();
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogDebug("Chunk phase completed: {Chunks} chunks, {Runs} runs, {Workers} active workers at peak.",
            _statistics.ChunkCount, runs.Count, slots.PeakWorkerCount);
        return new(outputCompleted, runs.AsReadOnly(), _statistics.ReadLineCount, _statistics.SkippedLineCount,
            _statistics.ChunkCount, timer.Elapsed, slots.PeakChunkCount, slots.PeakWorkerCount);
    }
}
