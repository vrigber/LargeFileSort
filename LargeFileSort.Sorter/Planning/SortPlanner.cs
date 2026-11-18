using System;
using LargeFileSort.Sorter.Models;

namespace LargeFileSort.Sorter.Planning;

internal static class SortPlanner
{
    public static SortPlan Create(SorterConfiguration settings, int processorCount)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(processorCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(settings.MemoryBudgetBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(settings.MaxParallelism);
        ArgumentOutOfRangeException.ThrowIfLessThan(settings.MaxMergeSources, 2);

        long readerBytes;
        long writerBytes;
        try
        {
            readerBytes = MemoryEstimator.ReaderBytes(settings.ReaderBufferBytes);
            writerBytes = MemoryEstimator.WriterBytes(settings.WriterBufferCharacters);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new ArgumentException("The configured reader or writer buffer cannot be represented by the UTF-8 implementation.", nameof(settings), exception);
        }

        long memory = settings.MemoryBudgetBytes;
        long catalogBytes = memory / 32;
        long baseControlBytes = Math.Max(65536, memory / 64);
        long minimumChunk = Math.Max(1_048_576, 4 * (readerBytes + writerBytes));
        long available = memory - catalogBytes - baseControlBytes;
        long phaseWorkerLimit = (available - readerBytes - 5 * minimumChunk)
            / (minimumChunk + writerBytes + MemoryEstimator.WorkerOverheadBytes);
        long mergeWorkerLimit = (available - writerBytes - 2 * readerBytes
            - 2 * MemoryEstimator.QueueSlotBytes - 6 * minimumChunk) / MemoryEstimator.WorkerOverheadBytes;
        long workerLimit = Math.Min(phaseWorkerLimit, mergeWorkerLimit);
        workerLimit = Math.Min(workerLimit, Math.Min(settings.MaxParallelism, processorCount));
        int workers = (int)Math.Clamp(workerLimit, 0, Array.MaxLength - 1L);

        long runCapacity = Math.Max(0, (catalogBytes / 2 - MemoryEstimator.ArrayHeaderBytes) / MemoryEstimator.RunSlotBytes - 1);
        int maxRuns = (int)Math.Min(runCapacity, Array.MaxLength - 1L);
        if (workers < 1 || maxRuns < 2)
        {
            throw new ArgumentException("The RAM budget is too small for the configured buffers, usable chunks and merge reserves.", nameof(settings));
        }

        long controlBytes = baseControlBytes + workers * (long)MemoryEstimator.WorkerOverheadBytes;
        long mergeBytes = memory - catalogBytes - controlBytes;
        long phaseChunk = (mergeBytes - readerBytes - workers * writerBytes) / (workers + 5L);
        long mergeChunk = (mergeBytes - writerBytes - 2 * readerBytes - 2 * MemoryEstimator.QueueSlotBytes) / 6;
        long chunkBytes = Math.Min(phaseChunk, mergeChunk) & ~7L;
        if (chunkBytes < minimumChunk)
        {
            throw new ArgumentException("The RAM budget cannot provide a usable chunk within both phase limits.", nameof(settings));
        }

        long minimumHead = MemoryEstimator.StringBytes(4);
        long sourceLimit = (mergeBytes - writerBytes - 4 * minimumHead)
            / (readerBytes + minimumHead + MemoryEstimator.QueueSlotBytes);
        int mergeSources = (int)Math.Min(Math.Min(sourceLimit, settings.MaxMergeSources), Array.MaxLength);

        long chunkReserve = readerBytes + workers * writerBytes + 4 * chunkBytes + catalogBytes + controlBytes;
        return new(workers, chunkBytes,
            readerBytes, writerBytes, chunkReserve, catalogBytes + controlBytes, maxRuns,
            mergeBytes, mergeSources,
            settings.ReaderBufferBytes, settings.WriterBufferCharacters);
    }
}
