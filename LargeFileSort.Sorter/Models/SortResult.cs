using System;

namespace LargeFileSort.Sorter.Models;

internal sealed record SortResult(
    long InputByteCount,
    long ReadLineCount,
    long SkippedLineCount,
    long WrittenLineCount,
    long WrittenByteCount,
    long ChunkCount,
    long MergePassCount,
    long MergeGroupCount,
    long PeakEstimatedMemoryBytes,
    long? PeakWorkingSetBytes,
    long PeakReservedDiskBytes,
    long PeakActualDiskBytes,
    TimeSpan PreparationTime,
    TimeSpan ChunkPhaseTime,
    TimeSpan MergePhaseTime,
    TimeSpan PublicationTime,
    TimeSpan TotalTime);
