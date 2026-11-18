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
    IComparer<SortRecord> comparer,
    RunStore store,
    SortPlan plan,
    Func<IOutputWriter> createOutput,
    SortResult statistics,
    ILogger<ChunkPipeline> logger)
{
    public async Task<ChunkPipelineResult> RunAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationToken token = stop.Token;
        ExceptionDispatchInfo failure = null;
        List<SortedRun> runs = [];
        List<Task<SortedRun>> workers = [];
        SortChunk filling = null;
        bool outputCompleted = false;
        Stopwatch timer = Stopwatch.StartNew();

        void ObserveMemory(int chunks) => statistics.PeakEstimatedMemoryBytes = Math.Max(
            statistics.PeakEstimatedMemoryBytes, plan.ChunkPhaseReserveBytes + chunks * plan.ChunkBudgetBytes);

        void Fail(Exception exception)
        {
            ExceptionDispatchInfo captured = ExceptionDispatchInfo.Capture(exception);
            ExceptionDispatchInfo previous = exception is OutOfMemoryException
                ? Interlocked.Exchange(ref failure, captured)
                : Interlocked.CompareExchange(ref failure, captured, null);
            if (previous is not null && exception is not OperationCanceledException
                && !ReferenceEquals(previous.SourceException, exception))
                logger.LogError(exception, "Another chunk pipeline operation failed during shutdown.");
            try { stop.Cancel(); }
            catch (Exception cancellationFailure)
            {
                if (cancellationFailure is OutOfMemoryException)
                    Interlocked.Exchange(ref failure, ExceptionDispatchInfo.Capture(cancellationFailure));
                else logger.LogError(cancellationFailure, "Cancelling chunk pipeline participants failed.");
            }
        }

        void AddRun(SortedRun run)
        {
            if (run is null) return;
            if (runs.Count == plan.MaxRunCount)
                throw new IOException($"The run catalog limit of {plan.MaxRunCount} has been reached; increase the RAM budget or chunk size.");
            if (runs.Count == runs.Capacity)
                runs.Capacity = (int)Math.Min(plan.MaxRunCount, Math.Max(4L, 2L * runs.Capacity));
            runs.Add(run);
        }

        async Task<SortedRun> ProcessAsync(SortChunk chunk, bool directOutput)
        {
            IOutputWriter writer = null;
            try
            {
                token.ThrowIfCancellationRequested();
                if (directOutput) chunk.Sort(comparer, token);
                else await Task.Run(() => chunk.Sort(comparer, token), token);
                SortedRun run = null;
                if (directOutput) writer = createOutput();
                else
                {
                    var created = store.CreateWriter(chunk.MaxLineCharacters);
                    writer = created.Writer;
                    run = created.Run;
                }
                for (int index = 0; index < chunk.Count; index++)
                    await writer.WriteLineAsync(chunk.Records[index].Line.AsMemory(), token);
                await writer.CompleteAsync(token);
                return run;
            }
            catch (Exception exception) { Fail(exception); return null; }
            finally
            {
                try { if (writer is not null) await writer.DisposeAsync(); }
                catch (Exception cleanup) { Fail(cleanup); }
                finally { chunk.Release(); }
            }
        }

        try
        {
            token.ThrowIfCancellationRequested();
            filling = new(plan.ChunkBudgetBytes);
            ObserveMemory(1);
            bool endOfInput = await builder.FillAsync(filling, token);
            if (endOfInput)
            {
                SortChunk ready = filling;
                filling = null;
                await ProcessAsync(ready, directOutput: true);
                outputCompleted = failure is null;
            }
            else
            {
                while (true)
                {
                    if (workers.Count == plan.WorkerCount)
                    {
                        Task<SortedRun> finished = await Task.WhenAny(workers).WaitAsync(token);
                        workers.Remove(finished);
                        AddRun(await finished);
                    }
                    token.ThrowIfCancellationRequested();
                    SortChunk ready = filling;
                    filling = null;
                    workers.Add(ProcessAsync(ready, directOutput: false));
                    if (endOfInput) break;
                    filling = new(plan.ChunkBudgetBytes);
                    ObserveMemory(workers.Count + 1);
                    endOfInput = await builder.FillAsync(filling, token);
                    if (filling.Count == 0) break;
                }
            }
        }
        catch (Exception exception) { Fail(exception); }
        finally
        {
            filling?.Release();
            try
            {
                SortedRun[] completed = await Task.WhenAll(workers);
                if (failure is null) foreach (SortedRun run in completed) AddRun(run);
            }
            catch (Exception exception) { Fail(exception); }
            if (failure is not null) builder.Abort();
            timer.Stop();
            statistics.ChunkPhaseTime = timer.Elapsed;
        }
        failure?.Throw();
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogDebug("Chunk phase completed: {Chunks} chunks, {Runs} runs.", statistics.ChunkCount, runs.Count);
        return new(outputCompleted, runs);
    }
}
