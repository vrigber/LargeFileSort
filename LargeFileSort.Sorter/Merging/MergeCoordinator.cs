using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using LargeFileSort.Sorter.Abstractions;
using LargeFileSort.Sorter.Models;
using LargeFileSort.Sorter.Planning;
using LargeFileSort.Sorter.Storage;
using Microsoft.Extensions.Logging;

namespace LargeFileSort.Sorter.Merging;

internal sealed class MergeCoordinator(
    MergeEngine engine,
    RunStore store,
    SortPlan plan,
    ResultPublisher publisher,
    SortResult statistics,
    ILogger<MergeCoordinator> logger)
{

    public async Task MergeAsync(IReadOnlyList<SortedRun> runs, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runs);
        Stopwatch timer = Stopwatch.StartNew();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<SortedRun> catalog = new(runs);
            while (true)
            {
                var group = SelectGroup(catalog, 0);
                if (group.Count == catalog.Count)
                {
                    await MergeGroupAsync(catalog, 0, group.Count, group.MemoryBytes, group.MaxCharacters, finalOutput: true, cancellationToken);
                    if (catalog.Count > 0) statistics.MergePassCount++;
                    break;
                }

                int read = 0;
                int write = 0;
                while (read < catalog.Count)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (read > 0) group = SelectGroup(catalog, read);
                    if (group.Count == 1)
                    {
                        catalog[write++] = catalog[read++];
                        continue;
                    }
                    SortedRun merged = await MergeGroupAsync(catalog, read, group.Count, group.MemoryBytes, group.MaxCharacters,
                        finalOutput: false, cancellationToken);
                    catalog[write++] = merged;
                    read += group.Count;
                }
                catalog.RemoveRange(write, catalog.Count - write);
                statistics.MergePassCount++;
            }
        }
        finally
        {
            timer.Stop();
            statistics.MergePhaseTime = timer.Elapsed;
        }
        logger.LogDebug("Merge phase completed: {Passes} passes, {Groups} groups.",
            statistics.MergePassCount, statistics.MergeGroupCount);
    }

    private (int Count, long MemoryBytes, int MaxCharacters) SelectGroup(List<SortedRun> runs, int start)
    {
        int limit = Math.Min(plan.MaxMergeSources, runs.Count - start);
        int count = 0;
        long sourcesBytes = 0;
        long maxStringBytes = 0;
        int maxCharacters = 0;
        long memoryBytes = plan.WriterMemoryBytes;
        for (; count < limit; count++)
        {
            long stringBytes = MemoryEstimator.StringBytes(runs[start + count].MaxLineCharacters);
            long nextSources = checked(sourcesBytes + plan.ReaderMemoryBytes + stringBytes + MemoryEstimator.QueueSlotBytes);
            long nextMax = Math.Max(maxStringBytes, stringBytes);
            long nextMemory = checked(plan.WriterMemoryBytes + nextSources + 4 * nextMax);
            if (nextMemory > plan.MergeMemoryBudgetBytes) break;
            sourcesBytes = nextSources;
            maxStringBytes = nextMax;
            maxCharacters = Math.Max(maxCharacters, runs[start + count].MaxLineCharacters);
            memoryBytes = nextMemory;
        }
        int minimum = Math.Min(2, runs.Count - start);
        if (count < minimum || memoryBytes > plan.MergeMemoryBudgetBytes)
            throw new InvalidDataException("The RAM budget cannot fit the next merge group; increase the memory budget or reduce buffer sizes.");
        return (count, memoryBytes, maxCharacters);
    }

    private async Task<SortedRun> MergeGroupAsync(
        List<SortedRun> runs, int start, int count, long memoryBytes, int maxCharacters, bool finalOutput, CancellationToken cancellationToken)
    {
        statistics.PeakEstimatedMemoryBytes = Math.Max(statistics.PeakEstimatedMemoryBytes,
            checked(plan.MergePhaseReserveBytes + memoryBytes));
        ILineReader[] readers = new ILineReader[count];
        IOutputWriter writer = null;
        SortedRun result = null;
        Exception failure = null;

        async ValueTask CloseAsync(IAsyncDisposable resource)
        {
            if (resource is null) return;
            try { await resource.DisposeAsync(); }
            catch (Exception cleanup)
            {
                if (failure is null || cleanup is OutOfMemoryException) failure = cleanup;
                else if (failure is not OutOfMemoryException)
                    logger.LogError(cleanup, "Closing a merge resource failed after an earlier error.");
            }
        }

        try
        {
            for (int index = 0; index < count; index++) readers[index] = store.OpenReader(runs[start + index]);
            if (finalOutput) writer = publisher.CreateWriter();
            else
            {
                var created = store.CreateWriter(maxCharacters);
                writer = created.Writer;
                result = created.Run;
            }
            await engine.MergeAsync(readers, writer, cancellationToken);
            await writer.CompleteAsync(cancellationToken);
        }
        catch (Exception exception) { failure = exception; }
        finally
        {
            await CloseAsync(writer);
            foreach (ILineReader reader in readers) await CloseAsync(reader);
        }
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        if (count > 0) statistics.MergeGroupCount++;
        return result;
    }
}
