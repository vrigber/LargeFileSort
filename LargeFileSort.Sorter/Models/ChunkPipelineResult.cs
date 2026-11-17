using System.Collections.Generic;

namespace LargeFileSort.Sorter.Models;

internal sealed record ChunkPipelineResult(
    bool OutputCompleted,
    IReadOnlyList<SortedRun> Runs);
