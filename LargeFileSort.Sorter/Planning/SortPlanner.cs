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

        const long minimumChunk = 1_048_576;
        long available = settings.MemoryBudgetBytes - settings.MemoryBudgetBytes / 4;
        long workerLimit = (available - readerBytes - minimumChunk) / (minimumChunk + writerBytes);
        int workers = (int)Math.Min(workerLimit, Math.Min(settings.MaxParallelism, processorCount));
        long minimumHead = MemoryEstimator.StringBytes(4);
        long sourceLimit = (available - writerBytes) / (readerBytes + minimumHead + MemoryEstimator.QueueSlotBytes);
        int mergeSources = (int)Math.Min(sourceLimit, settings.MaxMergeSources);
        if (workers < 1 || mergeSources < 2)
        {
            throw new ArgumentException("The RAM budget is too small for the configured buffers, usable chunks and at least two merge sources.", nameof(settings));
        }

        long chunkBytes = (available - readerBytes - workers * writerBytes) / (workers + 1L);
        return new(workers, chunkBytes,
            readerBytes, writerBytes, available, mergeSources,
            settings.ReaderBufferBytes, settings.WriterBufferCharacters);
    }
}
