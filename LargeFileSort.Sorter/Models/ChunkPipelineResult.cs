using System;
using System.Collections.Generic;

namespace LargeFileSort.Sorter.Models;

internal sealed record ChunkPipelineResult(
    bool OutputCompleted,
    IReadOnlyList<SortedRun> Runs,
    long ReadLineCount,
    long SkippedLineCount,
    long ChunkCount,
    TimeSpan Duration,
    int PeakChunkCount,
    int PeakWorkerCount);
