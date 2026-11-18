namespace LargeFileSort.Sorter.Models;

internal sealed record SortPlan(
    int WorkerCount,
    long ChunkBudgetBytes,
    long ReaderMemoryBytes,
    long WriterMemoryBytes,
    long ChunkPhaseReserveBytes,
    long MergePhaseReserveBytes,
    int MaxRunCount,
    long MergeMemoryBudgetBytes,
    int MaxMergeSources,
    int ReaderBufferBytes,
    int WriterBufferCharacters);
