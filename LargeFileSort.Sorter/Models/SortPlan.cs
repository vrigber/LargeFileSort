namespace LargeFileSort.Sorter.Models;

internal sealed record SortPlan(
    long InputByteCount,
    long MemoryBudgetBytes,
    int WorkerCount,
    int ChunkSlotCount,
    long ChunkBudgetBytes,
    long ReaderMemoryBytes,
    long WriterMemoryBytes,
    long ReadScratchBudgetBytes,
    long RunCatalogBudgetBytes,
    long ControlBudgetBytes,
    long MergeMemoryBudgetBytes,
    int MaxMergeSources,
    long DiskBudgetBytes,
    long InitialSegmentTargetBytes,
    int ReaderBufferBytes,
    int WriterBufferCharacters);
