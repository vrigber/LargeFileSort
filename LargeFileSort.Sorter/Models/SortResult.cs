using System;

namespace LargeFileSort.Sorter.Models;

internal sealed class SortResult
{
    public long InputByteCount { get; set; }

    public long ReadLineCount { get; set; }

    public long SkippedLineCount { get; set; }

    public long WrittenLineCount { get; set; }

    public long WrittenByteCount { get; set; }

    public long ChunkCount { get; set; }

    public long MergePassCount { get; set; }

    public long MergeGroupCount { get; set; }

    public long PeakEstimatedMemoryBytes { get; set; }

    public long? PeakWorkingSetBytes { get; set; }

    public TimeSpan PreparationTime { get; set; }

    public TimeSpan ChunkPhaseTime { get; set; }

    public TimeSpan MergePhaseTime { get; set; }

    public TimeSpan PublicationTime { get; set; }

    public TimeSpan TotalTime { get; set; }
}
