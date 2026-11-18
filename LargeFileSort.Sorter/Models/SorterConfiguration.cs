namespace LargeFileSort.Sorter.Models;

internal sealed record SorterConfiguration(
    long MemoryBudgetBytes,
    int MaxParallelism,
    int MaxMergeSources,
    int ReaderBufferBytes,
    int WriterBufferCharacters);
